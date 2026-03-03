using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SpeedExplorer;

/// <summary>
/// Provides centralized JSON parsing and extraction for LLM responses.
/// </summary>
public static class LlmParsers
{
    public static LlmImageTextResult? ParseImageTextResult(string rawContent)
    {
        string cleanJson = "";
        try
        {
            cleanJson = ExtractJsonObject(rawContent);
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            var result = new LlmImageTextResult();
            if (root.TryGetProperty("detected_language", out var language))
                result.DetectedLanguage = language.GetString() ?? "";

            if (root.TryGetProperty("full_text", out var fullText))
                result.FullText = fullText.GetString() ?? "";

            if (root.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in blocks.EnumerateArray())
                {
                    if (block.ValueKind != JsonValueKind.Object)
                        continue;

                    string text = block.TryGetProperty("text", out var textEl) ? (textEl.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    float x = ReadJsonFloat(block, "x");
                    float y = ReadJsonFloat(block, "y");
                    float w = ReadJsonFloat(block, "w");
                    float h = ReadJsonFloat(block, "h");
                    float fontSize = ReadJsonFloatAny(block, "font_size", "fontSize", "size");
                    
                    if (w <= 0f || h <= 0f)
                    {
                        LlmDebugLogger.LogExecution($"Skipping OCR block with zero dimensions: {text} (w={w}, h={h})", success: false);
                        continue;
                    }

                    result.Blocks.Add(new LlmImageTextBlock
                    {
                        Text = text.Trim(),
                        X = x,
                        Y = y,
                        W = w,
                        H = h,
                        FontSize = fontSize
                    });
                }
            }

            if (string.IsNullOrWhiteSpace(result.FullText) && result.Blocks.Count > 0)
                result.FullText = string.Join(Environment.NewLine, result.Blocks.Select(b => b.Text));

            return result;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Fatal OCR parse error: {ex.Message}\nCleaned JSON: {cleanJson}");
            throw;
        }
    }

    public static LlmTextTranslationResult? ParseTranslationResult(string rawContent, string targetLanguage)
    {
        string cleanJson = ExtractJsonObject(rawContent);
        using var doc = JsonDocument.Parse(cleanJson);
        var root = doc.RootElement;

        var result = new LlmTextTranslationResult
        {
            TargetLanguage = targetLanguage
        };

        if (root.TryGetProperty("translated_full_text", out var fullText))
            result.TranslatedFullText = fullText.GetString() ?? "";

        if (root.TryGetProperty("translations", out var translations) && translations.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in translations.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    result.Translations.Add(value.Trim());
            }
        }

        if (string.IsNullOrWhiteSpace(result.TranslatedFullText) && result.Translations.Count > 0)
            result.TranslatedFullText = string.Join(Environment.NewLine, result.Translations);

        return result;
    }

    public static LlmAgentChatDecision ParseAgentChatDecision(string json)
    {
        var result = new LlmAgentChatDecision();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        string cleanJson = ExtractJsonObject(json);

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.String)
                result.Thought = thought.GetString() ?? "";
            if (root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String)
                result.Action = action.GetString() ?? "reply";
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                result.Message = message.GetString() ?? "";
            if (root.TryGetProperty("run_task", out var runTask) && runTask.ValueKind == JsonValueKind.String)
                result.RunTask = runTask.GetString() ?? "";

            if (root.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
            {
                foreach (var cmd in commands.EnumerateArray())
                {
                    if (cmd.ValueKind != JsonValueKind.Object)
                        continue;

                    string cmdName = cmd.TryGetProperty("cmd", out var cmdEl) ? (cmdEl.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(cmdName))
                        continue;

                    var parsed = new LlmCommand
                    {
                        Cmd = cmdName,
                        Path = cmd.TryGetProperty("path", out var path) ? path.GetString() : null,
                        Root = cmd.TryGetProperty("root", out var rootPath) ? rootPath.GetString() : null,
                        Pattern = cmd.TryGetProperty("pattern", out var pattern) ? pattern.GetString() : null,
                        IncludeMetadata = cmd.TryGetProperty("include_metadata", out var includeMetadata) &&
                                          (includeMetadata.ValueKind == JsonValueKind.True || includeMetadata.ValueKind == JsonValueKind.False) &&
                                          includeMetadata.GetBoolean(),
                        Tags = cmd.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                            ? tags.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                            : null
                    };
                    result.Commands.Add(parsed);
                }
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to parse agent chat decision: {ex.Message}\nCleaned JSON: {cleanJson}");
            result.Action = "reply";
            result.Message = json.Trim();
            result.Commands.Clear();
        }

        if (string.IsNullOrWhiteSpace(result.Action))
            result.Action = "reply";
        if (string.IsNullOrWhiteSpace(result.Message))
            result.Message = "I can help with that.";

        return result;
    }

    public static LlmAgentResponse ParseAgenticResponse(string json)
    {
        var result = new LlmAgentResponse();
        if (string.IsNullOrWhiteSpace(json)) return result;

        string cleanJson = json.Trim();

        if (cleanJson.Contains("```json"))
        {
            int start = cleanJson.IndexOf("```json") + 7;
            int end = cleanJson.IndexOf("```", start);
            if (end > start) cleanJson = cleanJson.Substring(start, end - start).Trim();
        }
        else if (cleanJson.Contains("```"))
        {
            int start = cleanJson.IndexOf("```") + 3;
            int end = cleanJson.IndexOf("```", start);
            if (end > start) cleanJson = cleanJson.Substring(start, end - start).Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
            
            if (doc.RootElement.TryGetProperty("thought", out var thought))
            {
                result.Thought = thought.GetString();
                LlmDebugLogger.LogResponse($"[Thought Process]\n{result.Thought}\n");
            }
            if (doc.RootElement.TryGetProperty("plan", out var plan))
                result.Plan = plan.GetString();
            if (doc.RootElement.TryGetProperty("is_done", out var isDone))
                result.IsDone = isDone.GetBoolean();
            if (doc.RootElement.TryGetProperty("context_policy", out var contextPolicy) &&
                contextPolicy.ValueKind == JsonValueKind.Object)
            {
                var parsedPolicy = new LlmAgentContextPolicy();
                if (contextPolicy.TryGetProperty("use_file_context", out var useCtx) &&
                    (useCtx.ValueKind == JsonValueKind.True || useCtx.ValueKind == JsonValueKind.False))
                {
                    parsedPolicy.UseFileContext = useCtx.GetBoolean();
                }
                if (contextPolicy.TryGetProperty("level", out var level) && level.ValueKind == JsonValueKind.String)
                {
                    parsedPolicy.Level = level.GetString() ?? "none";
                }
                if (contextPolicy.TryGetProperty("path", out var cpPath) && cpPath.ValueKind == JsonValueKind.String)
                {
                    parsedPolicy.Path = cpPath.GetString();
                }
                if (contextPolicy.TryGetProperty("refresh_each_loop", out var refresh) &&
                    (refresh.ValueKind == JsonValueKind.True || refresh.ValueKind == JsonValueKind.False))
                {
                    parsedPolicy.RefreshEachLoop = refresh.GetBoolean();
                }
                result.ContextPolicy = parsedPolicy;
            }

            if (!doc.RootElement.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
                return result;
            
            var sb = new StringBuilder();
            int i = 1;
            
            foreach (var cmd in commands.EnumerateArray())
            {
                var llmCmd = new LlmCommand
                {
                    Cmd = cmd.GetProperty("cmd").GetString() ?? "",
                    Name = cmd.TryGetProperty("name", out var n) ? n.GetString() : null,
                    Path = cmd.TryGetProperty("path", out var path) ? path.GetString() : null,
                    Root = cmd.TryGetProperty("root", out var root) ? root.GetString() : null,
                    Pattern = cmd.TryGetProperty("pattern", out var p) ? p.GetString() : null,
                    To = cmd.TryGetProperty("to", out var t) ? t.GetString() : null,
                    File = cmd.TryGetProperty("file", out var f) ? f.GetString() : null,
                    NewName = cmd.TryGetProperty("newName", out var nn) ? nn.GetString() : null,
                    Content = cmd.TryGetProperty("content", out var c) ? c.GetString() : null,
                    IncludeMetadata = cmd.TryGetProperty("include_metadata", out var inc) && inc.ValueKind == JsonValueKind.True,
                    Tags = cmd.TryGetProperty("tags", out var tags) ? 
                           tags.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null,
                    Files = cmd.TryGetProperty("files", out var files) ? 
                           files.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null
                };
                result.Commands.Add(llmCmd);
                sb.AppendLine($"{i++}. {llmCmd.Cmd}: {llmCmd.Name ?? llmCmd.Pattern ?? llmCmd.To ?? (llmCmd.Tags != null ? string.Join(", ", llmCmd.Tags) : (llmCmd.Files != null ? $"{llmCmd.Files.Count} files" : ""))}");
            }

            LlmDebugLogger.LogResponse("", sb.ToString());
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to parse agent commands: {ex.Message}\nCleaned JSON: {cleanJson}");
            throw new Exception($"Failed to parse LLM agent response: {ex.Message}");
        }
        return result;
    }

    public static string ExtractMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        string cleanJson = json.Trim();

        // Strip markdown code blocks if present
        if (cleanJson.Contains("```json"))
        {
            int start = cleanJson.IndexOf("```json") + 7;
            int end = cleanJson.IndexOf("```", start);
            if (end > start) cleanJson = cleanJson.Substring(start, end - start).Trim();
        }
        else if (cleanJson.Contains("```"))
        {
            int start = cleanJson.IndexOf("```") + 3;
            int end = cleanJson.IndexOf("```", start);
            if (end > start) cleanJson = cleanJson.Substring(start, end - start).Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString() ?? cleanJson;
        }
        catch
        {
            // Ignore parse errors, return raw
        }

        return json;
    }

    public static List<LlmCommand> ParseCommands(string json)
    {
        var result = new List<LlmCommand>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        string cleanJson = json.Trim();

        if (cleanJson.Contains("```json"))
        {
            int start = cleanJson.IndexOf("```json") + 7;
            int end = cleanJson.IndexOf("```", start);
            if (end > start)
            {
                cleanJson = cleanJson.Substring(start, end - start).Trim();
            }
        }
        else if (cleanJson.Contains("```"))
        {
            int start = cleanJson.IndexOf("```") + 3;
            int end = cleanJson.IndexOf("```", start);
            if (end > start)
            {
                cleanJson = cleanJson.Substring(start, end - start).Trim();
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(cleanJson);
            
            if (doc.RootElement.TryGetProperty("thought", out var thought))
            {
                LlmDebugLogger.LogResponse($"[Thought Process]\n{thought.GetString()}\n");
            }

            if (!doc.RootElement.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
                return result;
            
            var sb = new StringBuilder();
            int i = 1;
            
            foreach (var cmd in commands.EnumerateArray())
            {
                string cmdStr = "";
                if (cmd.TryGetProperty("cmd", out var cVal)) cmdStr = cVal.GetString() ?? "";
                else if (cmd.TryGetProperty("action", out var aVal)) cmdStr = aVal.GetString() ?? "";
                else if (cmd.TryGetProperty("command", out var coVal)) cmdStr = coVal.GetString() ?? "";

                var llmCmd = new LlmCommand
                {
                    Cmd = cmdStr,
                    Name = cmd.TryGetProperty("name", out var n) ? n.GetString() : null,
                    Path = cmd.TryGetProperty("path", out var path) ? path.GetString() : null,
                    Root = cmd.TryGetProperty("root", out var root) ? root.GetString() : null,
                    Pattern = cmd.TryGetProperty("pattern", out var p) ? p.GetString() : null,
                    To = cmd.TryGetProperty("to", out var t) ? t.GetString() : null,
                    File = cmd.TryGetProperty("file", out var f) ? f.GetString() : null,
                    NewName = cmd.TryGetProperty("newName", out var nn) ? nn.GetString() : null,
                    Content = cmd.TryGetProperty("content", out var c) ? c.GetString() : null,
                    IncludeMetadata = cmd.TryGetProperty("include_metadata", out var includeMetadata) &&
                                      (includeMetadata.ValueKind == JsonValueKind.True || includeMetadata.ValueKind == JsonValueKind.False) &&
                                      includeMetadata.GetBoolean(),
                    Tags = cmd.TryGetProperty("tags", out var tags) ? 
                           (tags.ValueKind == JsonValueKind.Array ? tags.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string> { tags.GetString() ?? "" }) : null,
                    Files = cmd.TryGetProperty("files", out var files) ? 
                           (files.ValueKind == JsonValueKind.Array ? files.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string> { files.GetString() ?? "" }) : null
                };
                result.Add(llmCmd);
                sb.AppendLine($"{i++}. {llmCmd.Cmd}: {llmCmd.Name ?? llmCmd.Pattern ?? llmCmd.To ?? (llmCmd.Tags != null ? string.Join(", ", llmCmd.Tags) : (llmCmd.Files != null ? $"{llmCmd.Files.Count} files" : ""))}");
            }

            LlmDebugLogger.LogResponse("", sb.ToString());
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to parse commands: {ex.Message}\nCleaned JSON: {cleanJson}");
            if (json.Length > 2000)
                 return result;
            
            throw new Exception($"Failed to parse LLM response: {ex.Message}");
        }
        return result;
    }

    public static float ReadJsonFloat(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var value))
            return 0f;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float number))
            return number;

        if (value.ValueKind == JsonValueKind.String &&
            float.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
        {
            return parsed;
        }

        return 0f;
    }

    public static float ReadJsonFloatAny(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            float value = ReadJsonFloat(obj, key);
            if (value > 0f)
                return value;
        }

        return 0f;
    }

    public static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    public static string ExtractJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "{}";

        string clean = content.Trim();
        int fenceStart = clean.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart >= 0)
        {
            fenceStart += 7;
            int fenceEnd = clean.IndexOf("```", fenceStart, StringComparison.OrdinalIgnoreCase);
            if (fenceEnd > fenceStart)
                clean = clean.Substring(fenceStart, fenceEnd - fenceStart).Trim();
        }
        else if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            int start = clean.IndexOf('\n');
            int end = clean.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
                clean = clean.Substring(start + 1, end - start - 1).Trim();
        }

        int objStart = clean.IndexOf('{');
        int objEnd = clean.LastIndexOf('}');
        if (objStart >= 0 && objEnd > objStart)
            return clean.Substring(objStart, objEnd - objStart + 1);

        return clean;
    }

    public static string TrimForHistory(string? text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxLen)
            return normalized;

        return normalized.Substring(0, maxLen) + "...";
    }

    public static bool IsReadOnlyAgentCommand(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
            return false;

        return cmd.Equals("list_dir", StringComparison.OrdinalIgnoreCase) ||
               cmd.Equals("search", StringComparison.OrdinalIgnoreCase) ||
               cmd.Equals("search_tags", StringComparison.OrdinalIgnoreCase);
    }
}
