using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SpeedExplorer;

/// <summary>
/// Manages the execution loop and state for autonomous autonomous operations.
/// </summary>
public class LlmAgentRunner
{
    private readonly LlmModelManager _modelManager;

    public string LastAgentFinalResponse { get; private set; } = "";
    public LlmAgentRunReport? LastAgentRunReport { get; private set; }

    public LlmAgentRunner(LlmModelManager modelManager)
    {
        _modelManager = modelManager;
    }

    /// <summary>
    /// Runs the LLM in an autonomous loop, reporting status updates and returning executed operations.
    /// </summary>
    public async Task<List<FileOperation>> RunAgenticTaskAsync(
        List<ChatMessage> history,
        string currentDirectory,
        bool taggingEnabled,
        bool searchEnabled,
        LlmExecutor executor,
        IProgress<string>? progress,
        string apiUrl,
        string? modelOverride = null)
    {
        var settings = AppSettings.Current;
        int maxLoops = Math.Clamp(settings.LlmAgentMaxLoops, 1, 100);
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = LlmModelManager.GetCompletionsApiUrl(settings.LlmChatApiUrl, apiUrl);
        double agentTemperature = Math.Min(settings.LlmTemperature, 0.3);
        int agentMaxTokens = Math.Max(256, settings.LlmMaxTokens);
        int retryTextMaxTokens = Math.Max(256, (int)Math.Round(agentMaxTokens * 0.8));
        int retryNoSchemaMaxTokens = Math.Max(256, (int)Math.Round(agentMaxTokens * 0.6));
        int loopCount = 0;
        LastAgentFinalResponse = "";
        LastAgentRunReport = null;

        // Build a fresh per-request working history so prior agent internal turns
        // cannot pollute future runs.
        string userObjective = history.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(userObjective))
            userObjective = "(no user request provided)";

        string systemPrompt = LlmPromptBuilder.GetAgenticSystemPrompt(taggingEnabled, searchEnabled);
        string accumulatedExecutionChain = "(none)";
        LlmAgentContextPolicy? selectedContextPolicy = null;
        bool contextPolicyLocked = false;
        string cachedInjectedFileContext = "";

        var allOps = new List<FileOperation>();
        var runNotes = new List<string>();
        bool completed = false;
        string stopReason = "";
        int totalCommandsExecuted = 0;
        int totalReadOnlyCommandsExecuted = 0;
        int totalWriteCommandsExecuted = 0;
        string? previousWriteCommandSignature = null;
        int repeatedNoChangeWriteLoops = 0;
        var seenNoChangeWriteSignatures = new HashSet<string>(StringComparer.Ordinal);
        string? previousReadOnlyCommandSignature = null;
        int repeatedReadOnlyLoops = 0;
        bool ranClosureVerification = false;

        while (loopCount < maxLoops)
        {
            loopCount++;
            progress?.Report($"[Loop {loopCount}/{maxLoops}] Thinking...");

            string loopDirContext = LlmPromptBuilder.BuildExtensionContext(currentDirectory);
            string injectedFileContext = "";
            bool injectedFileContextThisLoop = false;
            if (selectedContextPolicy != null &&
                selectedContextPolicy.UseFileContext &&
                !string.Equals(selectedContextPolicy.Level, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (!selectedContextPolicy.RefreshEachLoop && !string.IsNullOrWhiteSpace(cachedInjectedFileContext))
                {
                    injectedFileContext = cachedInjectedFileContext;
                    injectedFileContextThisLoop = true;
                }
                else
                {
                    injectedFileContext = BuildInjectedFileContextSnapshot(currentDirectory, selectedContextPolicy);
                    if (!string.IsNullOrWhiteSpace(injectedFileContext))
                    {
                        injectedFileContextThisLoop = true;
                        if (!selectedContextPolicy.RefreshEachLoop)
                            cachedInjectedFileContext = injectedFileContext;
                    }
                }
            }

            string loopState = BuildAgentLoopStateMessage(
                loopCount,
                maxLoops,
                accumulatedExecutionChain,
                selectedContextPolicy,
                injectedFileContextThisLoop);
            string loopSystemContent = BuildAgentSystemMessage(
                systemPrompt,
                currentDirectory,
                loopDirContext,
                loopState,
                injectedFileContext);
            var loopMessages = new List<object>
            {
                new { role = "system", content = loopSystemContent },
                new { role = "user", content = userObjective }
            };

            var requestData = new
            {
                model = model,
                messages = loopMessages,
                response_format = LlmPromptBuilder.GetAgenticJsonSchema(taggingEnabled, searchEnabled),
                temperature = agentTemperature,
                max_tokens = agentMaxTokens,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions { WriteIndented = true });
            LlmDebugLogger.LogRequest("", $"Agent Loop {loopCount}", "", json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            string responseString;
            try
            {
                var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content);
                responseString = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    bool retried = false;
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || IsLikelyChannelError(responseString))
                    {
                        // Retry 1: text mode for backends that reject structured output variants.
                        var retryRequestData = new
                        {
                            model = model,
                            messages = loopMessages,
                            response_format = new { type = "text" },
                            temperature = agentTemperature,
                            max_tokens = retryTextMaxTokens,
                            stream = false
                        };
                        string retryJson = JsonSerializer.Serialize(retryRequestData, new JsonSerializerOptions { WriteIndented = true });
                        LlmDebugLogger.LogRequest("", $"Agent Loop {loopCount} Retry(text)", "", retryJson);

                        using var retryContent = new StringContent(retryJson, Encoding.UTF8, "application/json");
                        var retryResponse = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryContent);
                        responseString = await retryResponse.Content.ReadAsStringAsync();
                        if (retryResponse.IsSuccessStatusCode)
                        {
                            retried = true;
                        }
                        else
                        {
                            // Retry 2: no response_format.
                            var retry2RequestData = new
                            {
                                model = model,
                                messages = loopMessages,
                                temperature = agentTemperature,
                                max_tokens = retryNoSchemaMaxTokens,
                                stream = false
                            };
                            string retry2Json = JsonSerializer.Serialize(retry2RequestData, new JsonSerializerOptions { WriteIndented = true });
                            LlmDebugLogger.LogRequest("", $"Agent Loop {loopCount} Retry(no_schema)", "", retry2Json);

                            using var retry2Content = new StringContent(retry2Json, Encoding.UTF8, "application/json");
                            var retry2Response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retry2Content);
                            responseString = await retry2Response.Content.ReadAsStringAsync();
                            if (retry2Response.IsSuccessStatusCode)
                            {
                                retried = true;
                            }
                            else
                            {
                                LlmDebugLogger.LogError($"Agent loop API failed after retries: {retry2Response.StatusCode} - {responseString}");
                            }
                        }
                    }

                    if (!retried)
                    {
                        stopReason = $"API failed: {response.StatusCode}";
                        LlmDebugLogger.LogError($"Agent loop API failed: {response.StatusCode} - {responseString}");
                        progress?.Report($"[Error] {stopReason}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                stopReason = $"Network error: {ex.Message}";
                progress?.Report($"[Error] {stopReason}");
                break;
            }

            string contentStr = LlmModelManager.ExtractAssistantContent(responseString);
            if (string.IsNullOrWhiteSpace(contentStr))
                contentStr = responseString;

            LlmAgentResponse agentResp;
            try
            {
                agentResp = LlmParsers.ParseAgenticResponse(contentStr);
            }
            catch (Exception ex)
            {
                progress?.Report($"[Repair] Agent returned malformed JSON. Attempting repair...");
                var repaired = await TryRepairAgentResponseAsync(
                    requestUrl,
                    model,
                    contentStr,
                    taggingEnabled,
                    searchEnabled,
                    agentTemperature,
                    Math.Max(256, Math.Min(agentMaxTokens, 1400)));
                if (repaired != null)
                {
                    agentResp = repaired;
                    progress?.Report("[Repair] Recovered agent response.");
                }
                else
                {
                    stopReason = $"Agent returned invalid JSON: {ex.Message}";
                    progress?.Report($"[Error] {stopReason}");
                    break;
                }
            }

            if (!string.IsNullOrEmpty(agentResp.Thought))
                progress?.Report($"💡 Thought: {agentResp.Thought}");
            if (!string.IsNullOrEmpty(agentResp.Plan))
                progress?.Report($"📋 Plan: {agentResp.Plan}");

            if (!contextPolicyLocked && loopCount == 1)
            {
                selectedContextPolicy = DetermineFirstLoopContextPolicy(agentResp, userObjective);
                contextPolicyLocked = true;
                progress?.Report($"📁 Context policy locked: {FormatContextPolicyForStatus(selectedContextPolicy)}");
            }

            var commandsToExecute = ApplyAgentPerLoopCommandPolicy(
                agentResp.Commands,
                out string policyNote,
                out bool policySuppressedAllCommands);
            if (!string.IsNullOrWhiteSpace(policyNote))
                progress?.Report($"🧭 {policyNote}");

            bool hasWriteCommands = commandsToExecute.Any(c => !IsReadOnlyAgentCommand(c.Cmd));
            string writeSignature = hasWriteCommands ? BuildWriteCommandSignature(commandsToExecute) : "";
            string readOnlySignature = !hasWriteCommands && commandsToExecute.Any()
                ? BuildReadOnlyCommandSignature(commandsToExecute)
                : "";
            int loopOpsCount = 0;

            if (commandsToExecute.Any())
            {
                int readOnlyCount = commandsToExecute.Count(c => IsReadOnlyAgentCommand(c.Cmd));
                totalCommandsExecuted += commandsToExecute.Count;
                totalReadOnlyCommandsExecuted += readOnlyCount;
                totalWriteCommandsExecuted += commandsToExecute.Count - readOnlyCount;

                progress?.Report($"⚙️ Executing {commandsToExecute.Count} commands...");
                var (feedbackList, ops) = executor.ExecuteAgenticCommands(commandsToExecute);
                if (!string.IsNullOrWhiteSpace(policyNote))
                    feedbackList.Insert(0, $"[policy] {policyNote}");
                loopOpsCount = ops.Count;
                allOps.AddRange(ops);
                
                string feedback = BuildAgentFeedbackForHistory(feedbackList);
                string currentLoopSummary = BuildAgentLoopCarrySummary(loopCount, agentResp, commandsToExecute, loopOpsCount, policyNote);
                accumulatedExecutionChain = AppendExecutionChain(accumulatedExecutionChain, loopCount, currentLoopSummary, feedback);
                runNotes.Add($"Loop {loopCount}: Executed {commandsToExecute.Count} command(s); feedback items: {feedbackList.Count}; new ops: {loopOpsCount}.");
                progress?.Report($"✅ Feedback received ({feedbackList.Count} items).");
            }
            else
            {
                var noExec = new StringBuilder();
                noExec.AppendLine("System Execution Feedback:");
                if (!string.IsNullOrWhiteSpace(policyNote))
                    noExec.AppendLine($"- [policy] {policyNote}");
                noExec.AppendLine("- No commands were executed in the previous loop.");
                string noExecFeedback = noExec.ToString().TrimEnd();
                string currentLoopSummary = BuildAgentLoopCarrySummary(loopCount, agentResp, commandsToExecute, loopOpsCount, policyNote);
                accumulatedExecutionChain = AppendExecutionChain(accumulatedExecutionChain, loopCount, currentLoopSummary, noExecFeedback);
            }

            if (hasWriteCommands)
            {
                previousReadOnlyCommandSignature = null;
                repeatedReadOnlyLoops = 0;

                if (loopOpsCount == 0 &&
                    !string.IsNullOrWhiteSpace(writeSignature) &&
                    string.Equals(writeSignature, previousWriteCommandSignature, StringComparison.Ordinal))
                {
                    repeatedNoChangeWriteLoops++;
                    if (repeatedNoChangeWriteLoops >= 1)
                    {
                        completed = allOps.Count > 0;
                        stopReason = completed
                            ? "Detected repeated write commands with no new changes after successful changes; treating request as complete."
                            : "Detected repeated write commands with no new changes; stopping to avoid a loop.";
                        progress?.Report($"🟡 [{stopReason}]");
                        break;
                    }
                }
                else if (loopOpsCount == 0 && !string.IsNullOrWhiteSpace(writeSignature))
                {
                    if (!seenNoChangeWriteSignatures.Add(writeSignature))
                    {
                        completed = allOps.Count > 0;
                        stopReason = completed
                            ? "Detected repeated no-change write command set; stopping to avoid a loop."
                            : "Detected repeated no-change write commands; stopping to avoid a loop.";
                        progress?.Report($"🟡 [{stopReason}]");
                        break;
                    }
                    repeatedNoChangeWriteLoops = 0;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(writeSignature))
                        seenNoChangeWriteSignatures.Remove(writeSignature);
                    repeatedNoChangeWriteLoops = 0;
                }

                previousWriteCommandSignature = writeSignature;
            }
            else
            {
                repeatedNoChangeWriteLoops = 0;
                if (!string.IsNullOrWhiteSpace(readOnlySignature))
                {
                    if (string.Equals(readOnlySignature, previousReadOnlyCommandSignature, StringComparison.Ordinal))
                    {
                        repeatedReadOnlyLoops++;
                        if (repeatedReadOnlyLoops >= 1)
                        {
                            completed = allOps.Count > 0;
                            stopReason = completed
                                ? "Detected repeated read-only verification with no further changes after successful writes; treating request as complete."
                                : "Detected repeated read-only verification with no progress; stopping to avoid a loop.";
                            progress?.Report($"🟡 [{stopReason}]");
                            break;
                        }
                    }
                    else
                    {
                        repeatedReadOnlyLoops = 0;
                    }

                    previousReadOnlyCommandSignature = readOnlySignature;
                }
                else
                {
                    previousReadOnlyCommandSignature = null;
                    repeatedReadOnlyLoops = 0;
                }
            }

            if (agentResp.IsDone)
            {
                completed = true;
                stopReason = $"Agent marked task complete in loop {loopCount}.";
                progress?.Report($"✨ [Agent finished successfully in {loopCount} loops]");
                break;
            }
            
            if (!commandsToExecute.Any() && !agentResp.IsDone)
            {
                if (policySuppressedAllCommands)
                {
                    progress?.Report("🟡 [Policy suppressed redundant commands; continuing to next loop.]");
                    continue;
                }

                stopReason = "Agent output no commands and did not set is_done.";
                progress?.Report($"⚠️ [{stopReason}]");
                break;
            }

            if (loopCount >= maxLoops)
            {
                stopReason = $"Reached max loop limit ({maxLoops}).";
                progress?.Report($"🛑 [Agent reached max loop limit ({maxLoops})]");
            }
        }

        if (!completed &&
            loopCount >= maxLoops &&
            allOps.Count > 0)
        {
            ranClosureVerification = true;
            progress?.Report("[Closure Verification] Checking completion and applying minimal fixes if needed...");

            LlmAgentContextPolicy? effectiveClosurePolicy = selectedContextPolicy;
            if (effectiveClosurePolicy == null ||
                !effectiveClosurePolicy.UseFileContext ||
                string.Equals(effectiveClosurePolicy.Level, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsClearlyPatternOnlyTask(userObjective))
                {
                    effectiveClosurePolicy = new LlmAgentContextPolicy
                    {
                        UseFileContext = true,
                        Level = "names",
                        Path = "./",
                        RefreshEachLoop = true
                    };
                }
            }

            string loopDirContext = LlmPromptBuilder.BuildExtensionContext(currentDirectory);
            string injectedFileContext = "";
            bool injectedFileContextThisLoop = false;
            if (effectiveClosurePolicy != null &&
                effectiveClosurePolicy.UseFileContext &&
                !string.Equals(effectiveClosurePolicy.Level, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (!effectiveClosurePolicy.RefreshEachLoop && !string.IsNullOrWhiteSpace(cachedInjectedFileContext))
                {
                    injectedFileContext = cachedInjectedFileContext;
                    injectedFileContextThisLoop = true;
                }
                else
                {
                    injectedFileContext = BuildInjectedFileContextSnapshot(currentDirectory, effectiveClosurePolicy);
                    if (!string.IsNullOrWhiteSpace(injectedFileContext))
                        injectedFileContextThisLoop = true;
                }
            }

            string closureState = BuildAgentClosureStateMessage(
                maxLoops,
                accumulatedExecutionChain,
                effectiveClosurePolicy,
                injectedFileContextThisLoop);
            string closureSystemContent = BuildAgentSystemMessage(
                systemPrompt,
                currentDirectory,
                loopDirContext,
                closureState,
                injectedFileContext);
            var closureMessages = new List<object>
            {
                new { role = "system", content = closureSystemContent },
                new { role = "user", content = userObjective }
            };

            var closureRequestData = new
            {
                model = model,
                messages = closureMessages,
                response_format = LlmPromptBuilder.GetAgenticJsonSchema(taggingEnabled, searchEnabled),
                temperature = agentTemperature,
                max_tokens = agentMaxTokens,
                stream = false
            };

            var closureJson = JsonSerializer.Serialize(closureRequestData, new JsonSerializerOptions { WriteIndented = true });
            LlmDebugLogger.LogRequest("", "Agent Closure Verification", "", closureJson);

            try
            {
                using var closureContent = new StringContent(closureJson, Encoding.UTF8, "application/json");
                var closureResponse = await LlmModelManager.HttpClient.PostAsync(requestUrl, closureContent);
                var closureResponseString = await closureResponse.Content.ReadAsStringAsync();

                if (closureResponse.IsSuccessStatusCode)
                {
                    string closureContentStr = LlmModelManager.ExtractAssistantContent(closureResponseString);
                    if (string.IsNullOrWhiteSpace(closureContentStr))
                        closureContentStr = closureResponseString;

                    var closureResp = LlmParsers.ParseAgenticResponse(closureContentStr);
                    var closureCommands = ApplyAgentPerLoopCommandPolicy(
                        closureResp.Commands,
                        out string closurePolicyNote,
                        out bool closureSuppressedAllCommands);

                    int closureOpsCount = 0;
                    if (closureCommands.Any())
                    {
                        int closureReadOnlyCount = closureCommands.Count(c => IsReadOnlyAgentCommand(c.Cmd));
                        totalCommandsExecuted += closureCommands.Count;
                        totalReadOnlyCommandsExecuted += closureReadOnlyCount;
                        totalWriteCommandsExecuted += closureCommands.Count - closureReadOnlyCount;

                        var (closureFeedbackList, closureOps) = executor.ExecuteAgenticCommands(closureCommands);
                        if (!string.IsNullOrWhiteSpace(closurePolicyNote))
                            closureFeedbackList.Insert(0, $"[policy] {closurePolicyNote}");

                        closureOpsCount = closureOps.Count;
                        allOps.AddRange(closureOps);
                        string closureFeedback = BuildAgentFeedbackForHistory(closureFeedbackList);
                        string closureSummary = BuildAgentLoopCarrySummary(maxLoops + 1, closureResp, closureCommands, closureOpsCount, closurePolicyNote);
                        accumulatedExecutionChain = AppendExecutionChain(accumulatedExecutionChain, maxLoops + 1, closureSummary, closureFeedback);
                        runNotes.Add($"Closure verification: Executed {closureCommands.Count} command(s); feedback items: {closureFeedbackList.Count}; new ops: {closureOpsCount}.");
                    }
                    else
                    {
                        string closureNoExecFeedback = "System Execution Feedback:\n- No commands were executed in closure verification.";
                        string closureSummary = BuildAgentLoopCarrySummary(maxLoops + 1, closureResp, closureCommands, closureOpsCount, closurePolicyNote);
                        accumulatedExecutionChain = AppendExecutionChain(accumulatedExecutionChain, maxLoops + 1, closureSummary, closureNoExecFeedback);
                        runNotes.Add("Closure verification: No commands executed.");
                    }

                    if (closureResp.IsDone)
                    {
                        completed = true;
                        stopReason = "Closure verification confirmed completion.";
                        progress?.Report("✨ [Closure verification marked task complete]");
                    }
                    else if (!closureCommands.Any() && !closureSuppressedAllCommands)
                    {
                        completed = true;
                        stopReason = "Closure verification found no further actions after prior successful changes; treated as complete.";
                        progress?.Report("🟢 [Closure verification found no further actions; treated as complete]");
                    }
                    else if (closureOpsCount > 0)
                    {
                        completed = true;
                        stopReason = "Closure verification applied final corrections.";
                        progress?.Report("🟢 [Closure verification applied final corrections]");
                    }
                }
            }
            catch (Exception ex)
            {
                LlmDebugLogger.LogError($"Closure verification failed: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(stopReason))
            stopReason = completed ? "Completed." : "Stopped.";
        else if (ranClosureVerification && completed && stopReason.StartsWith("Reached max loop limit", StringComparison.OrdinalIgnoreCase))
            stopReason = "Closure verification completed after max loop budget.";

        string finalResponse = await GenerateAgentFinalResponseAsync(
            userObjective,
            runNotes,
            allOps.Count,
            loopCount,
            completed,
            stopReason,
            model,
            requestUrl);
        LastAgentFinalResponse = string.IsNullOrWhiteSpace(finalResponse)
            ? BuildAgentFallbackResponse(allOps.Count, completed, stopReason)
            : finalResponse;

        LastAgentRunReport = new LlmAgentRunReport
        {
            Request = userObjective,
            Model = model,
            LoopsUsed = loopCount,
            MaxLoops = maxLoops,
            Completed = completed,
            ClosureVerificationRan = ranClosureVerification,
            CommandsExecuted = totalCommandsExecuted,
            ReadOnlyCommandsExecuted = totalReadOnlyCommandsExecuted,
            WriteCommandsExecuted = totalWriteCommandsExecuted,
            UndoOperationRecords = allOps.Count,
            StopReason = stopReason,
            Events = runNotes.TakeLast(12).ToList(),
            ModelSummary = LastAgentFinalResponse,
            FinishedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };
        
        return allOps;
    }

    private async Task<string> GenerateAgentFinalResponseAsync(
        string userObjective,
        List<string> runNotes,
        int operationCount,
        int loopsUsed,
        bool completed,
        string stopReason,
        string model,
        string requestUrl)
    {
        try
        {
            string systemPrompt = "You write final user-facing summaries for autonomous file assistant runs.";
            string notes = runNotes.Count == 0
                ? "(no notable intermediate events)"
                : string.Join("\n", runNotes.TakeLast(8));

            string summaryPrompt =
                $"User request:\n{userObjective}\n\n" +
                "Run result:\n" +
                $"- loops used: {loopsUsed}\n" +
                $"- completed flag: {completed}\n" +
                $"- undo operation records: {operationCount}\n" +
                $"- stop reason: {stopReason}\n\n" +
                $"Events:\n{notes}\n\n" +
                "Write a direct reply to the user with:\n" +
                "1) what was done,\n" +
                "2) what is uncertain or failed (if any),\n" +
                "3) what they can do next if needed.\n\n" +
                "Return STRICT JSON only: {\"message\":\"...\"}.";

            var requestData = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = summaryPrompt }
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "agent_final_response",
                        strict = true,
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                message = new { type = "string" }
                            },
                            required = new[] { "message" },
                            additionalProperties = false
                        }
                    }
                },
                temperature = 0.2,
                max_tokens = Math.Min(300, AppSettings.Current.LlmMaxTokens),
                stream = false
            };

            var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions { WriteIndented = true });
            LlmDebugLogger.LogRequest("", "Agent Final Response", systemPrompt, json);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content);
            var responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"Agent final response API failed: {response.StatusCode} - {responseString}");
                return "";
            }

            string messageRaw = LlmModelManager.ExtractAssistantContent(responseString);
            if (!string.IsNullOrWhiteSpace(messageRaw))
            {
                try
                {
                    string cleanJson = LlmParsers.ExtractJsonObject(messageRaw);
                    using var doc = JsonDocument.Parse(cleanJson);
                    if (doc.RootElement.TryGetProperty("message", out var msgElem))
                    {
                        string msg = msgElem.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(msg))
                        {
                            LlmDebugLogger.LogResponse(msg);
                            return msg.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LlmDebugLogger.LogError($"Agent final response parse failed: {ex.Message}");
                }

                // Some models ignore json_schema and return plain text.
                // Use it as-is so the user still gets a final response.
                string plain = messageRaw.Trim();
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    LlmDebugLogger.LogResponse(plain);
                    return plain;
                }
            }

            return "";
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Agent final response failed: {ex.Message}");
            return "";
        }
    }

    private static string BuildAgentFallbackResponse(int operationCount, bool completed, string stopReason)
    {
        if (completed)
        {
            if (operationCount > 0)
                return $"Done. I completed the task and recorded {operationCount} undoable operation(s).";
            return "Done. I completed the task.";
        }

        if (operationCount > 0)
            return $"I made some changes ({operationCount} undoable operation(s)), but the run ended early: {stopReason}";

        return $"I could not complete the task: {stopReason}";
    }
    
    // --- Helper Methods ---

    private static string BuildAgentLoopStateMessage(
        int loopCount,
        int maxLoops,
        string executionChain,
        LlmAgentContextPolicy? contextPolicy,
        bool injectedFileContextThisLoop)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Loop State]");
        sb.AppendLine($"- loop: {loopCount}/{maxLoops}");
        sb.AppendLine("- execution policy: multiple create_folder commands are allowed in one loop.");
        sb.AppendLine("- execution policy: for file-modifying writes, keep operations incremental; all move commands are allowed in one loop.");
        sb.AppendLine("- avoid repeating writes that previously produced no new changes.");
        sb.AppendLine("- if more writes are needed, keep is_done=false and continue next loop.");
        sb.AppendLine("- after successful writes, one targeted verification pass is usually enough; do not repeat the same verification commands unless feedback shows a concrete mismatch.");
        if (contextPolicy == null)
        {
            sb.AppendLine("- persistent file context: not selected yet.");
        }
        else if (contextPolicy.UseFileContext && !string.Equals(contextPolicy.Level, "none", StringComparison.OrdinalIgnoreCase))
        {
            string path = string.IsNullOrWhiteSpace(contextPolicy.Path) ? "./" : contextPolicy.Path!;
            sb.AppendLine($"- persistent file context: ON (level={contextPolicy.Level}, path={path}, refresh_each_loop={contextPolicy.RefreshEachLoop}).");
            sb.AppendLine($"- file context injected this loop: {injectedFileContextThisLoop}.");
            sb.AppendLine("- when injected context covers the same path/level, avoid list_dir and continue with next actions.");
        }
        else
        {
            sb.AppendLine("- persistent file context: OFF.");
        }
        sb.AppendLine();
        sb.AppendLine("Accumulated execution chain:");
        sb.AppendLine(string.IsNullOrWhiteSpace(executionChain) ? "(none)" : executionChain);
        return sb.ToString().TrimEnd();
    }

    private static string BuildAgentClosureStateMessage(
        int maxLoops,
        string executionChain,
        LlmAgentContextPolicy? contextPolicy,
        bool injectedFileContextThisLoop)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Closure Verification]");
        sb.AppendLine($"- main loop budget ({maxLoops}) has been reached.");
        sb.AppendLine("- first verify whether the user request is already fully satisfied using current and injected context.");
        sb.AppendLine("- if satisfied: set is_done=true and return no commands.");
        sb.AppendLine("- if not satisfied: output only minimal corrective commands and keep is_done=false.");
        sb.AppendLine("- you may move files again to correct mistakes.");
        sb.AppendLine("- do not repeat the same verification command set twice in closure verification.");
        if (contextPolicy != null && contextPolicy.UseFileContext && !string.Equals(contextPolicy.Level, "none", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- persistent file context is ON (level={contextPolicy.Level}, injected={injectedFileContextThisLoop}).");
            sb.AppendLine("- avoid redundant list_dir when injected context already covers your check path.");
        }

        sb.AppendLine();
        sb.AppendLine("Accumulated execution chain:");
        sb.AppendLine(string.IsNullOrWhiteSpace(executionChain) ? "(none)" : executionChain);
        return sb.ToString().TrimEnd();
    }

    private static string AppendExecutionChain(string existingChain, int loopNumber, string loopSummary, string loopFeedback)
    {
        var sb = new StringBuilder();
        string normalizedExistingChain = NormalizeHistoricalExecutionChain(existingChain);
        if (!string.IsNullOrWhiteSpace(normalizedExistingChain) && !string.Equals(normalizedExistingChain, "(none)", StringComparison.Ordinal))
        {
            sb.AppendLine(normalizedExistingChain.TrimEnd());
            sb.AppendLine();
        }

        sb.AppendLine($"=== Loop {loopNumber} ===");
        sb.AppendLine("Summary:");
        sb.AppendLine(string.IsNullOrWhiteSpace(loopSummary) ? "(none)" : loopSummary.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("Feedback:");
        sb.AppendLine(string.IsNullOrWhiteSpace(loopFeedback) ? "(none)" : loopFeedback.TrimEnd());
        return sb.ToString().TrimEnd();
    }

    private static string NormalizeHistoricalExecutionChain(string chain)
    {
        if (string.IsNullOrWhiteSpace(chain) || string.Equals(chain, "(none)", StringComparison.Ordinal))
            return chain;

        var sections = SplitExecutionChainSections(chain);
        if (sections.Count <= 1)
            return chain;

        for (int i = 0; i < sections.Count - 1; i++)
            sections[i] = CompactListDirDetailsInSection(sections[i]);

        return string.Join(Environment.NewLine + Environment.NewLine, sections.Select(s => s.TrimEnd()));
    }

    private static List<string> SplitExecutionChainSections(string chain)
    {
        var result = new List<string>();
        var matches = Regex.Matches(chain, @"(?m)^=== Loop \d+ ===\s*$");
        if (matches.Count == 0)
        {
            result.Add(chain);
            return result;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : chain.Length;
            result.Add(chain.Substring(start, end - start).TrimEnd());
        }

        return result;
    }

    private static string CompactListDirDetailsInSection(string section)
    {
        var normalized = section.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var sb = new StringBuilder();
        bool skippingListDirDetails = false;

        foreach (var line in lines)
        {
            string trimmed = line.TrimStart();

            if (!skippingListDirDetails &&
                (trimmed.StartsWith("- [list_dir ", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("[list_dir ", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = line.IndexOf("[list_dir ", StringComparison.OrdinalIgnoreCase);
                string listDirHeader = idx >= 0 ? line.Substring(idx).TrimEnd() : trimmed;
                string prefix = trimmed.StartsWith("- ", StringComparison.Ordinal) ? "- " : "";
                sb.AppendLine($"{prefix}{listDirHeader} (details omitted from older loop feedback)");
                skippingListDirDetails = true;
                continue;
            }

            if (skippingListDirDetails)
            {
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("Write commands attempted:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("System Execution Feedback:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Feedback:", StringComparison.OrdinalIgnoreCase))
                {
                    skippingListDirDetails = false;
                }
                else
                {
                    continue;
                }
            }

            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildAgentSystemMessage(
        string systemPrompt,
        string currentDirectory,
        string loopDirContext,
        string stateMessage,
        string? injectedFileContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt.TrimEnd());
        sb.AppendLine();
        sb.AppendLine($"[Current Context - {currentDirectory}]:");
        sb.AppendLine(loopDirContext);

        if (!string.IsNullOrWhiteSpace(injectedFileContext))
        {
            sb.AppendLine();
            sb.AppendLine("[Injected File Context]");
            sb.AppendLine(injectedFileContext.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stateMessage))
        {
            sb.AppendLine();
            sb.AppendLine(stateMessage.Trim());
        }

        return sb.ToString().TrimEnd();
    }

    private static LlmAgentContextPolicy DetermineFirstLoopContextPolicy(LlmAgentResponse response, string? userObjective)
    {
        var normalized = NormalizeContextPolicy(response.ContextPolicy);
        if (normalized != null)
            return normalized;

        var firstListDir = (response.Commands ?? new List<LlmCommand>())
            .FirstOrDefault(c => string.Equals(c.Cmd, "list_dir", StringComparison.OrdinalIgnoreCase));
        if (firstListDir != null)
        {
            return new LlmAgentContextPolicy
            {
                UseFileContext = true,
                Level = firstListDir.IncludeMetadata ? "metadata" : "names",
                Path = string.IsNullOrWhiteSpace(firstListDir.Path) ? "./" : firstListDir.Path,
                RefreshEachLoop = true
            };
        }

        if (IsClearlyPatternOnlyTask(userObjective))
        {
            return new LlmAgentContextPolicy
            {
                UseFileContext = false,
                Level = "none",
                Path = "./",
                RefreshEachLoop = true
            };
        }

        return new LlmAgentContextPolicy
        {
            UseFileContext = true,
            Level = "names",
            Path = "./",
            RefreshEachLoop = true
        };
    }

    private static bool IsClearlyPatternOnlyTask(string? objective)
    {
        if (string.IsNullOrWhiteSpace(objective))
            return false;

        string text = objective.ToLowerInvariant();
        bool hasExtensionMoveSignal =
            text.Contains("move all .") ||
            text.Contains("move *.") ||
            text.Contains("by extension") ||
            text.Contains("all jpg") ||
            text.Contains("all jpeg") ||
            text.Contains("all png") ||
            text.Contains("all webp") ||
            text.Contains("all bmp");

        bool hasConditionalSignal =
            text.Contains("if ") ||
            text.Contains("cyrillic") ||
            text.Contains("filename") ||
            text.Contains("file name") ||
            text.Contains("contains") ||
            text.Contains("starts with") ||
            text.Contains("ends with") ||
            text.Contains("except") ||
            text.Contains("but ") ||
            text.Contains("split");

        return hasExtensionMoveSignal && !hasConditionalSignal;
    }

    private static LlmAgentContextPolicy? NormalizeContextPolicy(LlmAgentContextPolicy? policy)
    {
        if (policy == null)
            return null;

        string level = (policy.Level ?? "none").Trim().ToLowerInvariant();
        if (level != "none" && level != "names" && level != "metadata")
            level = "names";

        bool useFileContext = policy.UseFileContext && level != "none";
        if (!useFileContext)
            level = "none";

        return new LlmAgentContextPolicy
        {
            UseFileContext = useFileContext,
            Level = level,
            Path = string.IsNullOrWhiteSpace(policy.Path) ? "./" : policy.Path,
            RefreshEachLoop = policy.RefreshEachLoop
        };
    }

    private static string FormatContextPolicyForStatus(LlmAgentContextPolicy? policy)
    {
        if (policy == null)
            return "none (default).";
        if (!policy.UseFileContext || string.Equals(policy.Level, "none", StringComparison.OrdinalIgnoreCase))
            return "off.";

        string path = string.IsNullOrWhiteSpace(policy.Path) ? "./" : policy.Path!;
        return $"on (level={policy.Level}, path={path}, refresh_each_loop={policy.RefreshEachLoop}).";
    }

    private static string BuildInjectedFileContextSnapshot(string currentDirectory, LlmAgentContextPolicy policy)
    {
        try
        {
            string resolvedPath = ResolveAgentContextPath(currentDirectory, policy.Path);
            if (!Directory.Exists(resolvedPath))
                return $"[Injected File Context]\n- path: {resolvedPath}\n- error: directory not found";

            const int maxItems = 140;
            var items = new DirectoryInfo(resolvedPath).GetFileSystemInfos("*", SearchOption.TopDirectoryOnly);
            var sb = new StringBuilder();
            sb.AppendLine("[Injected File Context]");
            sb.AppendLine($"- path: {resolvedPath}");
            sb.AppendLine($"- level: {policy.Level}");
            sb.AppendLine($"- refreshed_utc: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("- entries:");

            int count = 0;
            foreach (var item in items)
            {
                if (count >= maxItems)
                    break;

                if (item is DirectoryInfo)
                {
                    sb.AppendLine($"[DIR]  {item.Name}");
                }
                else if (string.Equals(policy.Level, "metadata", StringComparison.OrdinalIgnoreCase) && item is FileInfo fi)
                {
                    sb.AppendLine($"[FILE] {item.Name} ({fi.Length} bytes, {fi.LastWriteTimeUtc:yyyy-MM-dd})");
                }
                else
                {
                    sb.AppendLine($"[FILE] {item.Name}");
                }

                count++;
            }

            if (items.Length > maxItems)
                sb.AppendLine($"...(truncated {items.Length - maxItems} more entries)");
            sb.AppendLine($"[summary] total_items={items.Length}");

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"[Injected File Context]\n- error: {ex.Message}";
        }
    }

    private static string ResolveAgentContextPath(string baseDirectory, string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            return Path.GetFullPath(baseDirectory);

        string path = requestedPath.Trim();
        if (path == "~" || path == "." || path == "./" || path == ".\\")
            return Path.GetFullPath(baseDirectory);

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = "." + path.Substring(1);

        if ((path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith(".\\", StringComparison.Ordinal)) &&
            path.Length > 2)
        {
            string afterDot = path.Substring(2);
            if (Path.IsPathRooted(afterDot))
                return Path.GetFullPath(afterDot);
        }

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static string BuildAgentLoopCarrySummary(
        int loopNumber,
        LlmAgentResponse response,
        List<LlmCommand> executedCommands,
        int newOps,
        string policyNote)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Loop {loopNumber} summary:");
        if (!string.IsNullOrWhiteSpace(policyNote))
            sb.AppendLine($"- policy: {policyNote}");
        if (!string.IsNullOrWhiteSpace(response.Plan))
            sb.AppendLine($"- plan: {TrimForHistory(response.Plan, 1200)}");
        sb.AppendLine($"- commands: {SummarizeAgentCommands(executedCommands)}");
        sb.AppendLine($"- new_ops: {newOps}");
        sb.AppendLine($"- is_done: {response.IsDone}");

        bool onlyCreateFolder = executedCommands.Count > 0 &&
            executedCommands.All(c => string.Equals(c.Cmd, "create_folder", StringComparison.OrdinalIgnoreCase));
        if (onlyCreateFolder && newOps == 0)
        {
            sb.AppendLine("- next_hint: folder preparation likely complete; avoid repeating create_folder and continue with list_dir/move.");
        }

        bool hasMove = executedCommands.Any(c => string.Equals(c.Cmd, "move", StringComparison.OrdinalIgnoreCase));
        if (hasMove && newOps > 0)
        {
            sb.AppendLine("- next_hint: continue remaining move steps, or set is_done=true if the request is fully satisfied.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string SummarizeAgentCommands(List<LlmCommand> commands)
    {
        if (commands == null || commands.Count == 0)
            return "(none)";

        var parts = new List<string>();
        foreach (var cmd in commands.Take(4))
        {
            string name = (cmd.Cmd ?? "").Trim().ToLowerInvariant();
            switch (name)
            {
                case "move":
                    string src = cmd.Files != null && cmd.Files.Count > 0
                        ? $"{cmd.Files.Count} file(s)"
                        : (!string.IsNullOrWhiteSpace(cmd.Pattern) ? $"pattern {cmd.Pattern}" : "unspecified source");
                    parts.Add($"move {src} -> {cmd.To}");
                    break;
                case "create_folder":
                    parts.Add($"create_folder {cmd.Path}");
                    break;
                case "list_dir":
                    parts.Add($"list_dir {cmd.Path}");
                    break;
                case "search":
                    parts.Add($"search {cmd.Pattern} in {cmd.Root}");
                    break;
                default:
                    parts.Add(name);
                    break;
            }
        }

        if (commands.Count > 4)
            parts.Add($"+{commands.Count - 4} more");

        return string.Join("; ", parts);
    }

    private static string TrimForHistory(string? text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxLen)
            return normalized;

        return normalized.Substring(0, maxLen) + "...";
    }

    private static string BuildAgentFeedbackForHistory(List<string> feedbackItems)
    {
        const int maxItems = 16;
        const int maxCharsPerItem = 3500;
        const int maxTotalChars = 9000;

        var sb = new StringBuilder();
        sb.AppendLine("System Execution Feedback:");

        int totalChars = 0;
        int count = 0;
        foreach (var raw in feedbackItems)
        {
            if (count >= maxItems)
                break;

            string item = CompactFeedbackItem(raw ?? "");
            if (item.Length > maxCharsPerItem)
                item = item.Substring(0, maxCharsPerItem) + "\n...(truncated)";

            if (totalChars + item.Length > maxTotalChars)
                break;

            sb.AppendLine($"- {item}");
            totalChars += item.Length;
            count++;
        }

        if (feedbackItems.Count > count)
            sb.AppendLine($"- ...(truncated {feedbackItems.Count - count} more feedback item(s))");

        return sb.ToString();
    }

    private static string CompactFeedbackItem(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            return item;

        if (!item.StartsWith("[list_dir ", StringComparison.OrdinalIgnoreCase))
            return item;

        var lines = item.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("[FILE] ", StringComparison.Ordinal))
            {
                int metaStart = line.IndexOf(" (", StringComparison.Ordinal);
                if (metaStart > 0)
                    line = line.Substring(0, metaStart);
            }
            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    private static bool IsReadOnlyAgentCommand(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
            return false;

        return cmd.Equals("list_dir", StringComparison.OrdinalIgnoreCase) ||
               cmd.Equals("search", StringComparison.OrdinalIgnoreCase) ||
               cmd.Equals("search_tags", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyChannelError(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        return responseText.IndexOf("Channel Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
               responseText.IndexOf("channel_error", StringComparison.OrdinalIgnoreCase) >= 0 ||
               responseText.IndexOf("invalid_request", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildWriteCommandSignature(List<LlmCommand> commands)
    {
        var parts = commands
            .Where(c => !IsReadOnlyAgentCommand(c.Cmd))
            .Select(c =>
            {
                string files = c.Files == null ? "" : string.Join(",", c.Files.Select(f => f?.Trim().ToLowerInvariant()).Where(f => !string.IsNullOrWhiteSpace(f)));
                string tags = c.Tags == null ? "" : string.Join(",", c.Tags.Select(t => t?.Trim().ToLowerInvariant()).Where(t => !string.IsNullOrWhiteSpace(t)));
                return string.Join("|", new[]
                {
                    (c.Cmd ?? "").Trim().ToLowerInvariant(),
                    (c.Path ?? "").Trim().ToLowerInvariant(),
                    (c.Root ?? "").Trim().ToLowerInvariant(),
                    (c.To ?? "").Trim().ToLowerInvariant(),
                    (c.Pattern ?? "").Trim().ToLowerInvariant(),
                    (c.File ?? "").Trim().ToLowerInvariant(),
                    (c.NewName ?? "").Trim().ToLowerInvariant(),
                    (c.Name ?? "").Trim().ToLowerInvariant(),
                    files,
                    tags
                });
            })
            .OrderBy(x => x, StringComparer.Ordinal);

        return string.Join(";", parts);
    }

    private static string BuildReadOnlyCommandSignature(List<LlmCommand> commands)
    {
        var parts = commands
            .Where(c => IsReadOnlyAgentCommand(c.Cmd))
            .Select(c =>
            {
                string tags = c.Tags == null
                    ? ""
                    : string.Join(",", c.Tags.Select(t => t?.Trim().ToLowerInvariant()).Where(t => !string.IsNullOrWhiteSpace(t)));

                return string.Join("|", new[]
                {
                    (c.Cmd ?? "").Trim().ToLowerInvariant(),
                    (c.Path ?? "").Trim().ToLowerInvariant(),
                    (c.Root ?? "").Trim().ToLowerInvariant(),
                    (c.Pattern ?? "").Trim().ToLowerInvariant(),
                    c.IncludeMetadata ? "1" : "0",
                    tags
                });
            })
            .OrderBy(x => x, StringComparer.Ordinal);

        return string.Join(";", parts);
    }

    private static List<LlmCommand> ApplyAgentPerLoopCommandPolicy(
        List<LlmCommand> commands,
        out string policyNote,
        out bool suppressedAllByPolicy)
    {
        policyNote = "";
        suppressedAllByPolicy = false;
        var safeCommands = commands ?? new List<LlmCommand>();
        if (safeCommands.Count == 0)
            return safeCommands;

        var selected = new List<LlmCommand>();
        int skipped = 0;
        bool otherWriteKept = false;

        foreach (var cmd in safeCommands)
        {
            string name = (cmd.Cmd ?? "").Trim().ToLowerInvariant();

            if (IsReadOnlyAgentCommand(name))
            {
                selected.Add(cmd);
                continue;
            }

            if (name == "create_folder")
            {
                selected.Add(cmd);
                continue;
            }

            if (name == "move")
            {
                selected.Add(cmd);
                continue;
            }

            if (!otherWriteKept)
            {
                selected.Add(cmd);
                otherWriteKept = true;
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            if (selected.Count == 0)
            {
                suppressedAllByPolicy = true;
                policyNote = $"Suppressed all operations (enforcing incremental workflow policy).";
            }
            else
            {
                policyNote = $"Allowed read-only, create_folder, and up to one write block. Skipped {skipped} subsequent operation(s).";
            }
        }

        return selected;
    }

    private static async Task<LlmAgentResponse?> TryRepairAgentResponseAsync(
        string requestUrl,
        string model,
        string rawAssistantContent,
        bool taggingEnabled,
        bool searchEnabled,
        double temperature,
        int maxTokens)
    {
        try
        {
            string repairPrompt =
                "Rewrite the assistant message as valid JSON that matches the required schema. " +
                "Preserve the original intent. Return JSON only. If unsure, keep commands empty and is_done false.";

            var repairMessages = new List<object>
            {
                new { role = "system", content = repairPrompt },
                new { role = "user", content = rawAssistantContent }
            };

            var requestData = new
            {
                model = model,
                messages = repairMessages,
                response_format = LlmPromptBuilder.GetAgenticJsonSchema(taggingEnabled, searchEnabled),
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };

            string json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions { WriteIndented = true });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content);
            string responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return null;

            string repairedContent = LlmModelManager.ExtractAssistantContent(responseString);
            if (string.IsNullOrWhiteSpace(repairedContent))
                repairedContent = responseString;

            return LlmParsers.ParseAgenticResponse(repairedContent);
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Agent response repair failed: {ex.Message}");
            return null;
        }
    }
}
