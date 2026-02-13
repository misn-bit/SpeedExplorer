using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SpeedExplorer;

public struct LlmImageStats
{
    public string Path;
    public int OrigW;
    public int OrigH;
    public int NewW;
    public int NewH;
    public long Bytes;
}

/// <summary>
/// Service for communicating with LM Studio's local API.
/// Uses structured output to enforce JSON schema compliance.
/// </summary>
public class LlmService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

    public string ApiUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
    // Properties are now primarily used for direct access, but AppSettings.Current is source of truth during execution
    
    /// <summary>
    /// Fetches available models from the standard OpenAI-compatible /v1/models endpoint.
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync(string baseUrl)
    {
        try
        {
            baseUrl = baseUrl.TrimEnd('/');
            string url = $"{baseUrl}/models";
            
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");
            
            var models = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    models.Add(id.GetString() ?? "");
                }
            }
            return models;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to fetch models: {ex.Message}");
            throw;
        }
    }

    public string LastReasoning { get; private set; } = "";
    private string? _lastChatResponseId;
    private string? _lastChatContext;
    private string? _lastChatDirectory;

    public void ClearChatSession()
    {
        _lastChatResponseId = null;
        _lastChatContext = null;
        _lastChatDirectory = null;
    }

    /// <summary>
    /// Sends a chat history to the LLM and returns the response.
    /// Uses the Chat API URL if configured.
    /// Supports both standard OpenAI /v1/chat/completions and LM Studio native /api/v1/chat.
    /// </summary>
    /// <summary>
    /// Sends a chat history to the LLM and returns the response.
    /// Uses the Chat API URL if configured.
    /// Supports both standard OpenAI /v1/chat/completions and LM Studio native /api/v1/chat.
    /// Mirrors command mode features (system prompt, schema, toggles).
    /// </summary>
    public async Task<string> SendChatAsync(List<ChatMessage> history, bool taggingEnabled, bool searchEnabled, bool fullContext, bool thinkingEnabled, string? currentContext = null, string? currentDirectory = null)
    {
        var settings = AppSettings.Current;
        string apiUrl = settings.ChatModeEnabled ? settings.LlmChatApiUrl : settings.LlmApiUrl;
        string model = settings.LlmModelName;

        bool isNativeContext = apiUrl.Contains("/api/v1/chat") && !apiUrl.Contains("/completions");

        object requestData;

        if (isNativeContext)
        {
            var lastUserMsg = history.LastOrDefault(m => m.Role == "user");
            string input = lastUserMsg?.Content ?? "";
            
            if (string.IsNullOrEmpty(_lastChatResponseId))
            {
                string systemPrompt = LlmPromptBuilder.GetChatSystemPrompt(taggingEnabled, searchEnabled, fullContext, thinkingEnabled, currentContext);
                input = $"{systemPrompt}\n\n[Constraint: Output MUST be a JSON object matching the required command schema if performing actions.]\n\nUser Question: {input}";
            }
            else if (currentContext != _lastChatContext || currentDirectory != _lastChatDirectory)
            {
                input = $"[SYSTEM NOTE: The directory context has changed.]\nNEW Directory: {currentDirectory}\nFiles:\n{currentContext}\n\n---\n\nUser Question: {input}";
            }
            
            _lastChatContext = currentContext;
            _lastChatDirectory = currentDirectory;

            var payload = new Dictionary<string, object>
            {
                { "model", model },
                { "input", input },
                { "temperature", settings.LlmTemperature },
                { "max_output_tokens", settings.LlmMaxTokens },
                { "store", true }
            };

            if (!string.IsNullOrEmpty(_lastChatResponseId))
            {
                payload["previous_response_id"] = _lastChatResponseId;
            }
            
            requestData = payload;
        }
        else
        {
            var messages = new List<object>();
            string systemPrompt = LlmPromptBuilder.GetChatSystemPrompt(taggingEnabled, searchEnabled, fullContext, thinkingEnabled, currentContext);
            messages.Add(new { role = "system", content = systemPrompt });

            foreach (var msg in history)
            {
                if (msg.Role == "system") continue;
                messages.Add(new { role = msg.Role, content = msg.Content });
            }

            requestData = new
            {
                model = model,
                messages = messages,
                response_format = LlmPromptBuilder.GetJsonSchema(taggingEnabled, searchEnabled, fullContext, thinkingEnabled),
                temperature = settings.LlmTemperature,
                max_tokens = settings.LlmMaxTokens,
                stream = false
            };
        }

        var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions { WriteIndented = true });
        
        if (isNativeContext)
        {
            var lastUserMsg = history.LastOrDefault(m => m.Role == "user");
            string systemPrompt = LlmPromptBuilder.GetChatSystemPrompt(taggingEnabled, searchEnabled, fullContext, thinkingEnabled, currentContext);
            LlmDebugLogger.LogRequest("", lastUserMsg?.Content ?? "", systemPrompt, json);
        }
        else
        {
            string systemPrompt = LlmPromptBuilder.GetChatSystemPrompt(taggingEnabled, searchEnabled, fullContext, thinkingEnabled, currentContext);
            LlmDebugLogger.LogRequest("", "Chat History (Stateless)", systemPrompt, json);
        }

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            LastReasoning = "";
            var response = await _httpClient.PostAsync(apiUrl, content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"Chat API Error: {response.StatusCode} - {responseString}");
                return $"Error: {response.StatusCode} - {responseString}";
            }

            using var doc = JsonDocument.Parse(responseString);
            
            if (isNativeContext)
            {
                LlmDebugLogger.LogResponse($"[Native Raw] {responseString}");
                
                string contentStr = "";
                string reasoningStr = "";

                if (doc.RootElement.TryGetProperty("output", out var outputArray) && outputArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in outputArray.EnumerateArray())
                    {
                        string type = part.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        string val = part.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                        if (type == "reasoning") reasoningStr += val;
                        else if (type == "message") contentStr += val;
                    }
                }
                else if (doc.RootElement.TryGetProperty("content", out var c))
                {
                    contentStr = c.GetString() ?? "";
                }
                else if (doc.RootElement.TryGetProperty("choices", out var choices))
                {
                     contentStr = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }

                if (doc.RootElement.TryGetProperty("response_id", out var rid))
                    _lastChatResponseId = rid.GetString();
                
                LastReasoning = reasoningStr;
                return contentStr;
            }
            else
            {
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    return message.GetProperty("content").GetString() ?? "";
                }
            }
            return "";
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Chat Exception: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }


    /// <summary>
    /// Sends a prompt to the LLM and returns the raw response content.
    /// Supports optional images for Vision models.
    /// </summary>
    public async Task<string> SendPromptAsync(string userPrompt, string currentDir, bool fullContext, bool taggingEnabled, bool searchEnabled, bool thinkingEnabled, List<string>? imagePaths = null)
    {
        string dirContext = fullContext ? LlmPromptBuilder.BuildFullDirectoryContext(currentDir) : LlmPromptBuilder.BuildExtensionContext(currentDir);
        var enrichedPrompt = $"{userPrompt}\n\nContext items in directory:\n{dirContext}";

        string systemPrompt = LlmPromptBuilder.GetSystemPrompt(taggingEnabled, searchEnabled, fullContext, thinkingEnabled);

        // Get settings from AppSettings
        var settings = AppSettings.Current;
        
        object messages;
        string requestJson = "";
        
        if (imagePaths != null && imagePaths.Count > 0)
        {
            // Vision payload
            var contentList = new List<object>
            {
                new { type = "text", text = enrichedPrompt }
            };

            var imageStats = new List<LlmImageStats>();
            foreach (var imgPath in imagePaths)
            {
                try 
                {
                    var (imageBytes, stats) = LlmImageProcessor.PrepareImageForVision(imgPath);
                    imageStats.Add(stats);
                    string base64 = Convert.ToBase64String(imageBytes);
                    string ext = Path.GetExtension(imgPath).ToLowerInvariant().TrimStart('.');
                    if (ext == "jpg") ext = "jpeg";
                    // If we resized, it's now a JPEG stream from PrepareImageForVision
                    string mime = "image/jpeg"; 

                    contentList.Add(new 
                    { 
                        type = "image_url", 
                        image_url = new 
                        { 
                            url = $"data:{mime};base64,{base64}" 
                        } 
                    });
                }
                catch (Exception ex)
                {
                    LlmDebugLogger.LogError($"Failed to load image for vision: {imgPath} - {ex.Message}");
                }
            }

            messages = new[]
            {
                new { role = "system", content = (object)systemPrompt }, // Cast to object to match array type
                new { role = "user", content = (object)contentList } 
            };

            var requestBody = new
            {
                model = settings.LlmModelName,
                messages = messages,
                response_format = LlmPromptBuilder.GetJsonSchema(taggingEnabled, searchEnabled, fullContext, thinkingEnabled),
                temperature = settings.LlmTemperature, 
                max_tokens = settings.LlmMaxTokens, 
                stream = false
            };

            requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
            LlmDebugLogger.LogRequest(currentDir, userPrompt, systemPrompt, requestJson, imagePaths, imageStats);
        }
        else
        {
            // Standard text payload
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = enrichedPrompt }
            };

            var requestBody = new
            {
                model = settings.LlmModelName,
                messages = messages,
                response_format = LlmPromptBuilder.GetJsonSchema(taggingEnabled, searchEnabled, fullContext, thinkingEnabled),
                temperature = settings.LlmTemperature, 
                max_tokens = settings.LlmMaxTokens, 
                stream = false
            };

            requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
            LlmDebugLogger.LogRequest(currentDir, userPrompt, systemPrompt, requestJson);
        }

        try
        {
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(ApiUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"API Error {response.StatusCode}: {responseText}");
                throw new Exception($"LLM API returned {response.StatusCode}: {responseText}");
            }

            // Parse OpenAI-style response
            using var doc = JsonDocument.Parse(responseText);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) throw new Exception("LLM returned no choices");
            
            var messageContent = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            LlmDebugLogger.LogResponse(messageContent);
            return messageContent;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Request failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Specialized method for getting tags from an image based on user criteria.
    /// Returns a list of tags.
    /// </summary>
    public async Task<List<string>> GetImageTagsAsync(string userPrompt, string imagePath)
    {
        var settings = AppSettings.Current;
        
        string systemPrompt = "You are an automated image tagger. Analyze the provided image and generate relevant tags based strictly on the user's instructions. Output purely a JSON object with a 'tags' array.";

        var contentList = new List<object>
        {
            new { type = "text", text = userPrompt }
        };

        var stats = new LlmImageStats { Path = imagePath };
        try 
        {
            var (imageBytes, s) = LlmImageProcessor.PrepareImageForVision(imagePath);
            stats = s;
            string base64 = Convert.ToBase64String(imageBytes);
            
            // Vision models prefer JPEG for efficiency
            string mime = "image/jpeg";

            contentList.Add(new 
            { 
                type = "image_url", 
                image_url = new { url = $"data:{mime};base64,{base64}" } 
            });
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to load image for tagging: {imagePath} - {ex.Message}");
            return new List<string>();
        }

        var messages = new[]
        {
            new { role = "system", content = (object)systemPrompt },
            new { role = "user", content = (object)contentList } 
        };

        var requestBody = new
        {
            model = settings.LlmModelName,
            messages = messages,
            response_format = LlmPromptBuilder.GetTaggingJsonSchema(),
            temperature = settings.LlmTemperature, 
            max_tokens = settings.LlmMaxTokens, 
            stream = false
        };

        var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
        LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, new[] { stats });

        try
        {
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(ApiUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"API Error {response.StatusCode}: {responseText}");
                return new List<string>();
            }

            using var doc = JsonDocument.Parse(responseText);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return new List<string>();
            
            var messageContent = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            LlmDebugLogger.LogResponse(messageContent);

            using var resultDoc = JsonDocument.Parse(messageContent);
            if (resultDoc.RootElement.TryGetProperty("tags", out var tagsArray))
            {
                return tagsArray.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            return new List<string>();
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Tagging request failed: {ex.Message}");
            return new List<string>();
        }
    }



    /// <summary>
    /// Parses the LLM response JSON into a list of commands.
    /// </summary>
    public static List<LlmCommand> ParseCommands(string json)
    {
        var result = new List<LlmCommand>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        string cleanJson = json.Trim();

        // If it looks like markdown (e.g. Chat Mode response), try to extract the JSON block
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
            
            // Log thought if present
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
                var llmCmd = new LlmCommand
                {
                    Cmd = cmd.GetProperty("cmd").GetString() ?? "",
                    Name = cmd.TryGetProperty("name", out var n) ? n.GetString() : null,
                    Path = cmd.TryGetProperty("path", out var path) ? path.GetString() : null,
                    Pattern = cmd.TryGetProperty("pattern", out var p) ? p.GetString() : null,
                    To = cmd.TryGetProperty("to", out var t) ? t.GetString() : null,
                    File = cmd.TryGetProperty("file", out var f) ? f.GetString() : null,
                    NewName = cmd.TryGetProperty("newName", out var nn) ? nn.GetString() : null,
                    Content = cmd.TryGetProperty("content", out var c) ? c.GetString() : null,
                    Tags = cmd.TryGetProperty("tags", out var tags) ? 
                           tags.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null,
                    Files = cmd.TryGetProperty("files", out var files) ? 
                           files.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null
                };
                result.Add(llmCmd);
                sb.AppendLine($"{i++}. {llmCmd.Cmd}: {llmCmd.Name ?? llmCmd.Pattern ?? llmCmd.To ?? (llmCmd.Tags != null ? string.Join(", ", llmCmd.Tags) : (llmCmd.Files != null ? $"{llmCmd.Files.Count} files" : ""))}");
            }

            LlmDebugLogger.LogResponse("", sb.ToString());
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to parse commands: {ex.Message}\nCleaned JSON: {cleanJson}");
            // Don't throw if in chat mode - it might just be conversational text
            if (json.Length > 2000) // heuristic: if it's very long, maybe it's just text
                 return result;
            
            throw new Exception($"Failed to parse LLM response: {ex.Message}");
        }
        return result;
    }
}

public class LlmCommand
{
    public string Cmd { get; set; } = "";
    public string? Name { get; set; }       // For create_file name
    public string? Path { get; set; }       // For create_folder path (absolute or relative)
    public string? Pattern { get; set; }    // For move by pattern
    public string? To { get; set; }         // Destination for move
    public string? File { get; set; }       // Single file for rename
    public string? NewName { get; set; }    // New name for rename
    public string? Content { get; set; }    // Content for create_file
    public List<string>? Tags { get; set; } // Tags for tag command
    public List<string>? Files { get; set; } // Files to move or tag
}
