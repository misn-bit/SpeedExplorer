using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpeedExplorer;

/// <summary>
/// Manages LLM model discovery, resolution, loading, unloading, and endpoint normalization.
/// Owns the shared HttpClient used by all LLM HTTP operations.
/// </summary>
public class LlmModelManager
{
    internal static readonly HttpClient HttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly object _sessionModelLock = new();
    private static readonly Dictionary<string, string> _sessionResolvedModels = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] VisionModelHints =
    {
        "vision", "llava", "vlm", "moondream", "minicpm-v", "internvl", "qwen-vl", "qwen2-vl",
        "qwen2.5-vl", "qvq", "pixtral", "phi-3.5-vision", "paligemma", "smolvlm", "janus", "omni"
    };

    // ─── Endpoint Normalization ──────────────────────────────────────

    public static string GetServerBaseUrl(string apiUrl)
    {
        if (Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return "http://localhost:1234";
    }

    /// <summary>
    /// Returns a safe OpenAI-compatible chat completions endpoint.
    /// If a native chat endpoint (/api/v1/chat) or responses endpoint (/v1/responses) is provided,
    /// this maps it to /v1/chat/completions.
    /// </summary>
    public static string GetCompletionsApiUrl(string? preferredApiUrl, string? chatApiUrl = null)
    {
        string candidate = !string.IsNullOrWhiteSpace(preferredApiUrl)
            ? preferredApiUrl!.Trim()
            : (chatApiUrl ?? "").Trim();

        if (string.IsNullOrWhiteSpace(candidate))
            return "http://localhost:1234/v1/chat/completions";

        bool isNativeChat = candidate.Contains("/api/v1/chat", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.Contains("/completions", StringComparison.OrdinalIgnoreCase);
        if (isNativeChat)
        {
            string baseUrl = GetServerBaseUrl(candidate).TrimEnd('/');
            return $"{baseUrl}/v1/chat/completions";
        }

        bool isResponsesApi = candidate.Contains("/v1/responses", StringComparison.OrdinalIgnoreCase) ||
                              candidate.EndsWith("/responses", StringComparison.OrdinalIgnoreCase);
        if (isResponsesApi)
        {
            string baseUrl = GetServerBaseUrl(candidate).TrimEnd('/');
            return $"{baseUrl}/v1/chat/completions";
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            string path = uri.AbsolutePath.Trim();
            string normalizedPath = path.TrimEnd('/');
            if (string.IsNullOrEmpty(path) || path == "/")
                return $"{uri.Scheme}://{uri.Host}:{uri.Port}/v1/chat/completions";
            if (normalizedPath.Equals("/v1/responses", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("/responses", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("/api/v1/responses", StringComparison.OrdinalIgnoreCase))
            {
                return $"{uri.Scheme}://{uri.Host}:{uri.Port}/v1/chat/completions";
            }
        }

        return candidate;
    }

    // ─── Model Discovery ─────────────────────────────────────────────

    /// <summary>
    /// Fetches known models from LM Studio and annotates loaded/vision capabilities where possible.
    /// </summary>
    public async Task<LlmModelCatalog> GetModelCatalogAsync(string apiUrl)
    {
        string baseUrl = GetServerBaseUrl(apiUrl).TrimEnd('/');

        var merged = new Dictionary<string, LlmModelInfo>(StringComparer.OrdinalIgnoreCase);
        void Merge(List<LlmModelInfo> source)
        {
            foreach (var model in source)
            {
                if (string.IsNullOrWhiteSpace(model.Id))
                    continue;

                if (!merged.TryGetValue(model.Id, out var existing))
                {
                    merged[model.Id] = new LlmModelInfo
                    {
                        Id = model.Id,
                        IsLoaded = model.IsLoaded,
                        IsVision = model.IsVision,
                        LoadedInstanceIds = new List<string>(model.LoadedInstanceIds)
                    };
                }
                else
                {
                    existing.IsLoaded |= model.IsLoaded;
                    existing.IsVision |= model.IsVision;
                    foreach (var instanceId in model.LoadedInstanceIds)
                    {
                        if (!existing.LoadedInstanceIds.Contains(instanceId, StringComparer.OrdinalIgnoreCase))
                            existing.LoadedInstanceIds.Add(instanceId);
                    }
                }
            }
        }

        // LM Studio native endpoints (downloaded + loaded state)
        Merge(await TryGetModelsFromEndpointAsync($"{baseUrl}/api/v0/models", assumeLoaded: false));
        Merge(await TryGetModelsFromEndpointAsync($"{baseUrl}/api/v1/models", assumeLoaded: false));
        // OpenAI-compatible endpoint (do NOT assume loaded state).
        Merge(await TryGetModelsFromEndpointAsync($"{baseUrl}/v1/models", assumeLoaded: false));

        var list = merged.Values
            .OrderByDescending(m => m.IsLoaded)
            .ThenByDescending(m => m.IsVision)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LlmModelCatalog { AvailableModels = list };
    }

    /// <summary>
    /// Legacy compatibility for settings fetch button.
    /// </summary>
    public async Task<List<string>> GetAvailableModelsAsync(string apiUrl)
    {
        var catalog = await GetModelCatalogAsync(apiUrl);
        return catalog.AvailableModels.Select(m => m.Id).ToList();
    }

    // ─── Model Resolution ────────────────────────────────────────────

    /// <summary>
    /// Resolves model for a task by checking loaded models first, then prompting user when needed.
    /// For image/vision tasks it only offers vision-capable models.
    /// </summary>
    public async Task<string?> ResolveModelForTaskAsync(LlmUsageKind usage, LlmTaskKind taskKind, string apiUrl, IWin32Window? owner = null)
    {
        var settings = AppSettings.Current;
        string serverApiUrl = string.IsNullOrWhiteSpace(apiUrl) ? settings.LlmApiUrl : apiUrl;
        string preferredModel = usage switch
        {
            LlmUsageKind.Batch when taskKind == LlmTaskKind.Vision => settings.LlmBatchVisionModelName?.Trim() ?? "",
            _ => settings.LlmModelName?.Trim() ?? ""
        };

        var catalog = await GetModelCatalogAsync(serverApiUrl);
        var candidates = FilterCandidates(catalog.AvailableModels, taskKind);
        var loaded = candidates.Where(m => m.IsLoaded).ToList();

        if (candidates.Count == 0)
        {
            MessageBox.Show(owner, BuildNoModelMessage(taskKind), "LM Studio Model", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        string cacheKey = BuildSessionKey(usage, taskKind);
        bool hasPreferred = !string.IsNullOrWhiteSpace(preferredModel);
        var preferredLoaded = hasPreferred
            ? loaded.FirstOrDefault(m => string.Equals(m.Id, preferredModel, StringComparison.OrdinalIgnoreCase))
            : null;

        if (preferredLoaded != null)
        {
            CacheSessionModel(cacheKey, preferredLoaded.Id);
            return preferredLoaded.Id;
        }

        // Reuse previous session choice only when no preferred mismatch exists.
        bool hasMismatchWithPreferred = hasPreferred && preferredLoaded == null && loaded.Count > 0;
        if (!hasMismatchWithPreferred)
        {
            string? cached = GetCachedSessionModel(cacheKey);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                var cachedLoaded = loaded.FirstOrDefault(m => string.Equals(m.Id, cached, StringComparison.OrdinalIgnoreCase));
                if (cachedLoaded != null)
                    return cachedLoaded.Id;
            }
        }

        // Smart auto-choice only when there is no preferred model conflict.
        if (!hasPreferred && loaded.Count == 1)
        {
            CacheSessionModel(cacheKey, loaded[0].Id);
            return loaded[0].Id;
        }

        string title = taskKind == LlmTaskKind.Vision ? "Select Vision Model" : "Select Model";
        string infoText;

        if (loaded.Count == 0)
        {
            infoText = taskKind == LlmTaskKind.Vision
                ? "No vision model is currently loaded. Select any vision-capable model and click 'Use Selected'. The model will be loaded if needed."
                : "No model is currently loaded. Select a model and click 'Use Selected'. The model will be loaded if needed.";
        }
        else if (hasPreferred && preferredLoaded == null)
        {
            infoText = $"Preferred model '{preferredModel}' is not currently loaded. Choose a loaded model, or choose any model and load it.";
        }
        else
        {
            infoText = loaded.Count > 1
                ? "Multiple models are loaded. Choose the one to use for this task."
                : "Select a model to use for this task.";
        }

        using var selector = new LlmModelSelectorForm(this, serverApiUrl, usage, taskKind, preferredModel, title, infoText);
        var result = owner != null ? selector.ShowDialog(owner) : selector.ShowDialog();
        if (result != DialogResult.OK || string.IsNullOrWhiteSpace(selector.SelectedModelId))
            return null;

        CacheSessionModel(cacheKey, selector.SelectedModelId);
        return selector.SelectedModelId;
    }

    // ─── Model Load / Unload ─────────────────────────────────────────

    public async Task LoadModelAsync(string apiUrl, string modelId)
    {
        string baseUrl = GetServerBaseUrl(apiUrl).TrimEnd('/');
        var body = JsonSerializer.Serialize(new { model = modelId });
        var contentType = "application/json";

        var endpoints = new[]
        {
            $"{baseUrl}/api/v1/models/load",
            $"{baseUrl}/api/v1/model/load",
            $"{baseUrl}/api/v0/model/load",
            $"{baseUrl}/api/v0/models/load"
        };

        string lastError = "No response";
        foreach (var endpoint in endpoints)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, contentType);
                var response = await HttpClient.PostAsync(endpoint, content);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return;

                lastError = $"{(int)response.StatusCode} {response.StatusCode}: {responseBody}";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        throw new InvalidOperationException(lastError);
    }

    public async Task UnloadModelInstanceAsync(string apiUrl, string instanceId)
    {
        string baseUrl = GetServerBaseUrl(apiUrl).TrimEnd('/');
        var body = JsonSerializer.Serialize(new { instance_id = instanceId });
        var contentType = "application/json";

        var endpoints = new[]
        {
            $"{baseUrl}/api/v1/models/unload",
            $"{baseUrl}/api/v1/model/unload",
            $"{baseUrl}/api/v0/models/unload",
            $"{baseUrl}/api/v0/model/unload"
        };

        string lastError = "No response";
        foreach (var endpoint in endpoints)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, contentType);
                var response = await HttpClient.PostAsync(endpoint, content);
                var responseBody = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return;

                lastError = $"{(int)response.StatusCode} {response.StatusCode}: {responseBody}";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        throw new InvalidOperationException(lastError);
    }

    public async Task UnloadModelByIdAsync(string apiUrl, string modelId)
    {
        string baseUrl = GetServerBaseUrl(apiUrl).TrimEnd('/');
        string contentType = "application/json";
        var endpoints = new[]
        {
            $"{baseUrl}/api/v1/models/unload",
            $"{baseUrl}/api/v1/model/unload",
            $"{baseUrl}/api/v0/models/unload",
            $"{baseUrl}/api/v0/model/unload"
        };
        var bodies = new[]
        {
            JsonSerializer.Serialize(new { model = modelId }),
            JsonSerializer.Serialize(new { model_id = modelId }),
            JsonSerializer.Serialize(new { id = modelId })
        };

        string lastError = "No response";
        foreach (var endpoint in endpoints)
        {
            foreach (var body in bodies)
            {
                try
                {
                    using var content = new StringContent(body, Encoding.UTF8, contentType);
                    var response = await HttpClient.PostAsync(endpoint, content);
                    var responseBody = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                        return;

                    lastError = $"{(int)response.StatusCode} {response.StatusCode}: {responseBody}";
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }
        }

        throw new InvalidOperationException(lastError);
    }

    // ─── Vision Model Recovery ───────────────────────────────────────

    public async Task<bool> TryRecoverVisionModelAsync(string apiUrl, string modelId, string stage)
    {
        LlmModelInfo? target = null;
        bool modelAppearsLoaded = false;
        int loadedInstanceCount = 0;

        try
        {
            var catalog = await GetModelCatalogAsync(apiUrl);
            target = catalog.AvailableModels.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
            modelAppearsLoaded = target?.IsLoaded == true;
            loadedInstanceCount = target?.LoadedInstanceIds?.Count ?? 0;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Vision model recovery catalog fetch failed ({stage}): {ex.Message}");
        }

        if (modelAppearsLoaded)
        {
            LlmDebugLogger.LogExecution($"Vision model recovery: model already loaded ({loadedInstanceCount} instances), skipping soft reload ({stage})");
        }
        else
        {
            try
            {
                LlmDebugLogger.LogExecution($"Vision model recovery: loading model ({stage})");
                await LoadModelAsync(apiUrl, modelId);
                await Task.Delay(250);
                return true;
            }
            catch (Exception ex)
            {
                LlmDebugLogger.LogError($"Vision model recovery load failed ({stage}): {ex.Message}");
            }
        }

        bool unloadedAny = false;
        try
        {
            if (target?.LoadedInstanceIds?.Count > 0)
            {
                LlmDebugLogger.LogExecution($"Vision model recovery: unloading {target.LoadedInstanceIds.Count} instances ({stage})");
                foreach (var instanceId in target.LoadedInstanceIds)
                {
                    try
                    {
                        await UnloadModelInstanceAsync(apiUrl, instanceId);
                        unloadedAny = true;
                    }
                    catch (Exception unloadEx)
                    {
                        LlmDebugLogger.LogError($"Vision model unload instance failed ({instanceId}, {stage}): {unloadEx.Message}");
                    }
                }
            }
            else if (modelAppearsLoaded)
            {
                // Fallback for endpoints that do not expose instance ids reliably.
                LlmDebugLogger.LogExecution($"Vision model recovery: trying unload by model id ({stage})");
                await UnloadModelByIdAsync(apiUrl, modelId);
                unloadedAny = true;
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Vision model recovery unload failed ({stage}): {ex.Message}");
        }

        if (modelAppearsLoaded && !unloadedAny)
        {
            LlmDebugLogger.LogError($"Vision model recovery aborted to avoid duplicate instance ({stage}).");
            return false;
        }

        try
        {
            if (unloadedAny)
                await Task.Delay(220);

            LlmDebugLogger.LogExecution($"Vision model recovery: loading model after unload ({stage})");
            await LoadModelAsync(apiUrl, modelId);
            await Task.Delay(320);
            return true;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Vision model recovery final load failed ({stage}): {ex.Message}");
            return false;
        }
    }

    // ─── Error Detection ─────────────────────────────────────────────

    public static bool IsModelUnloadedError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("model unloaded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("model not loaded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("no model loaded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("model_not_loaded", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLikelyChannelError(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        return responseText.IndexOf("Channel Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
               responseText.IndexOf("channel_error", StringComparison.OrdinalIgnoreCase) >= 0 ||
               responseText.IndexOf("invalid_request", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsFailedToProcessImageError(HttpStatusCode statusCode, string? text)
    {
        return statusCode == HttpStatusCode.BadRequest &&
               !string.IsNullOrWhiteSpace(text) &&
               text.Contains("failed to process image", StringComparison.OrdinalIgnoreCase);
    }

    public static int ComputeOcrTimeoutSeconds(int maxTokens)
    {
        return 3600; // 1 hour (effectively unlimited as per user request)
    }

    public static int ComputeTranslationTimeoutSeconds(int maxTokens)
    {
        return 3600; // 1 hour (effectively unlimited as per user request)
    }

    // ─── Response Extraction ─────────────────────────────────────────

    public static string ExtractAssistantContent(string responseString)
    {
        if (string.IsNullOrWhiteSpace(responseString))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            }
            if (doc.RootElement.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? "";
            }
        }
        catch
        {
            // Fallback to raw if response isn't JSON.
        }

        return responseString;
    }

    // ─── Model List Parsing (internal) ───────────────────────────────

    private static async Task<List<LlmModelInfo>> TryGetModelsFromEndpointAsync(string url, bool assumeLoaded)
    {
        try
        {
            var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return new List<LlmModelInfo>();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return ParseModelList(doc.RootElement, assumeLoaded);
        }
        catch
        {
            return new List<LlmModelInfo>();
        }
    }

    private static List<LlmModelInfo> ParseModelList(JsonElement root, bool assumeLoaded)
    {
        var models = new List<LlmModelInfo>();

        JsonElement collection = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyIgnoreCase(root, "data", out var data) && data.ValueKind == JsonValueKind.Array)
                collection = data;
            else if (TryGetPropertyIgnoreCase(root, "models", out var modelArray) && modelArray.ValueKind == JsonValueKind.Array)
                collection = modelArray;
            else
                return models;
        }

        if (collection.ValueKind != JsonValueKind.Array)
            return models;

        foreach (var item in collection.EnumerateArray())
        {
            var id = ExtractModelId(item);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var loadedInstanceIds = ExtractLoadedInstanceIds(item);

            models.Add(new LlmModelInfo
            {
                Id = id,
                IsLoaded = loadedInstanceIds.Count > 0 || assumeLoaded || ExtractLoadedState(item),
                IsVision = ExtractVisionState(item, id),
                LoadedInstanceIds = loadedInstanceIds
            });
        }

        return models;
    }

    private static string ExtractModelId(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
            return item.GetString() ?? "";

        if (item.ValueKind != JsonValueKind.Object)
            return "";

        var keys = new[] { "id", "key", "model", "model_id", "name" };
        foreach (var key in keys)
        {
            if (TryGetPropertyIgnoreCase(item, key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var id = value.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    return id!;
            }
        }

        return "";
    }

    private static bool ExtractLoadedState(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return false;

        if (TryGetPropertyIgnoreCase(item, "loaded_instances", out var loadedInstances) &&
            loadedInstances.ValueKind == JsonValueKind.Array &&
            loadedInstances.GetArrayLength() > 0)
        {
            return true;
        }

        var boolKeys = new[] { "loaded", "is_loaded", "isLoaded", "active", "is_active", "isActive" };
        foreach (var key in boolKeys)
        {
            if (TryGetPropertyIgnoreCase(item, key, out var value) && value.ValueKind == JsonValueKind.True)
                return true;
        }

        var textKeys = new[] { "state", "status" };
        foreach (var key in textKeys)
        {
            if (TryGetPropertyIgnoreCase(item, key, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = (value.GetString() ?? "").Trim().ToLowerInvariant();
                    if (text.Contains("not-loaded", StringComparison.Ordinal) ||
                        text.Contains("not_loaded", StringComparison.Ordinal) ||
                        text.Contains("unloaded", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (text is "loaded" or "active" or "running" or "ready" ||
                        text.StartsWith("loaded ", StringComparison.Ordinal) ||
                        text.Contains(" active", StringComparison.Ordinal) ||
                        text.Contains(" running", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                else if (value.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetPropertyIgnoreCase(value, "loaded", out var nestedLoaded) && nestedLoaded.ValueKind == JsonValueKind.True)
                        return true;
                }
            }
        }

        return false;
    }

    private static List<string> ExtractLoadedInstanceIds(JsonElement item)
    {
        var ids = new List<string>();

        if (item.ValueKind != JsonValueKind.Object)
            return ids;

        if (!TryGetPropertyIgnoreCase(item, "loaded_instances", out var loadedInstances) ||
            loadedInstances.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var instance in loadedInstances.EnumerateArray())
        {
            string? id = ExtractLoadedInstanceId(instance);
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                ids.Add(id);
        }

        return ids;
    }

    private static string? ExtractLoadedInstanceId(JsonElement instance)
    {
        if (instance.ValueKind == JsonValueKind.String)
            return instance.GetString();

        if (instance.ValueKind != JsonValueKind.Object)
            return null;

        var keys = new[] { "instance_id", "id", "identifier", "key" };
        foreach (var key in keys)
        {
            if (TryGetPropertyIgnoreCase(instance, key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var id = value.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
        }

        return null;
    }

    private static bool ExtractVisionState(JsonElement item, string modelId)
    {
        if (LooksLikeVisionString(modelId))
            return true;

        if (item.ValueKind != JsonValueKind.Object)
            return false;

        return ContainsVisionSignal(item, depth: 0);
    }

    private static bool ContainsVisionSignal(JsonElement element, int depth)
    {
        if (depth > 5)
            return false;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var name = prop.Name.ToLowerInvariant();
                bool keyImpliesVision =
                    name.Contains("vision") ||
                    name.Contains("image") ||
                    name.Contains("multimodal") ||
                    name.Contains("modalit") ||
                    name.Contains("capabilit") ||
                    name is "task" or "tasks" or "type" or "architecture" or "arch";

                if (keyImpliesVision && ValueImpliesVision(prop.Value, depth + 1))
                    return true;

                if (ContainsVisionSignal(prop.Value, depth + 1))
                    return true;
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (ContainsVisionSignal(child, depth + 1))
                    return true;
            }
        }

        return false;
    }

    private static bool ValueImpliesVision(JsonElement value, int depth)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => LooksLikeVisionString(value.GetString()),
            JsonValueKind.Object => ContainsVisionSignal(value, depth + 1),
            JsonValueKind.Array => value.EnumerateArray().Any(child => ValueImpliesVision(child, depth + 1)),
            _ => false
        };
    }

    private static bool LooksLikeVisionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim().ToLowerInvariant();
        if (VisionModelHints.Any(h => text.Contains(h, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (text is "vision" or "image" or "images" or "vlm")
            return true;

        if (text.Contains("-vl") || text.Contains("vl-") || text.EndsWith(" vl", StringComparison.Ordinal) || text.Contains("multi-modal") || text.Contains("multimodal"))
            return true;

        return false;
    }

    // ─── Session Cache ───────────────────────────────────────────────

    private static string BuildSessionKey(LlmUsageKind usage, LlmTaskKind taskKind)
        => $"{usage}:{taskKind}";

    private static string? GetCachedSessionModel(string key)
    {
        lock (_sessionModelLock)
        {
            return _sessionResolvedModels.TryGetValue(key, out var value) ? value : null;
        }
    }

    private static void CacheSessionModel(string key, string modelId)
    {
        lock (_sessionModelLock)
        {
            _sessionResolvedModels[key] = modelId;
        }
    }

    private static List<LlmModelInfo> FilterCandidates(List<LlmModelInfo> models, LlmTaskKind taskKind)
    {
        if (taskKind == LlmTaskKind.Vision)
            return models.Where(m => m.IsVision).ToList();
        return models.ToList();
    }

    private static string BuildNoModelMessage(LlmTaskKind taskKind)
    {
        if (taskKind == LlmTaskKind.Vision)
            return "No vision-capable models were found in LM Studio.\nLoad a vision model and try again.";

        return "No models were found in LM Studio.\nLoad a model and try again.";
    }

    // ─── JSON Helpers ────────────────────────────────────────────────

    internal static bool TryGetPropertyIgnoreCase(JsonElement obj, string propertyName, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
