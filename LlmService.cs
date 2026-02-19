using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;

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

public enum LlmTaskKind
{
    Text,
    Vision
}

public enum LlmUsageKind
{
    Assistant,
    Batch
}

public sealed class LlmModelInfo
{
    public string Id { get; set; } = "";
    public bool IsLoaded { get; set; }
    public bool IsVision { get; set; }
    public List<string> LoadedInstanceIds { get; set; } = new();
}

public sealed class LlmModelCatalog
{
    public List<LlmModelInfo> AvailableModels { get; set; } = new();
    public List<LlmModelInfo> LoadedModels => AvailableModels.Where(m => m.IsLoaded).ToList();
}

public sealed class LlmImageTextBlock
{
    public string Text { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float FontSize { get; set; }
}

public sealed class LlmImageTextResult
{
    public string FullText { get; set; } = "";
    public string DetectedLanguage { get; set; } = "";
    public List<LlmImageTextBlock> Blocks { get; set; } = new();
}

public sealed class LlmTextTranslationResult
{
    public string TranslatedFullText { get; set; } = "";
    public List<string> Translations { get; set; } = new();
    public string TargetLanguage { get; set; } = "";
}

/// <summary>
/// Service for communicating with LM Studio's local API.
/// Uses structured output to enforce JSON schema compliance.
/// </summary>
public class LlmService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly object _sessionModelLock = new();
    private static readonly Dictionary<string, string> _sessionResolvedModels = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] VisionModelHints =
    {
        "vision", "llava", "vlm", "moondream", "minicpm-v", "internvl", "qwen-vl", "qwen2-vl",
        "qwen2.5-vl", "qvq", "pixtral", "phi-3.5-vision", "paligemma", "smolvlm", "janus", "omni"
    };

    public string ApiUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
    // Properties are now primarily used for direct access, but AppSettings.Current is source of truth during execution

    public static string GetServerBaseUrl(string apiUrl)
    {
        if (Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return "http://localhost:1234";
    }

    /// <summary>
    /// Returns a safe OpenAI-compatible chat completions endpoint.
    /// If a native chat endpoint (/api/v1/chat) is provided, this maps it to /v1/chat/completions.
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

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            string path = uri.AbsolutePath.Trim();
            if (string.IsNullOrEmpty(path) || path == "/")
                return $"{uri.Scheme}://{uri.Host}:{uri.Port}/v1/chat/completions";
        }

        return candidate;
    }

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

    /// <summary>
    /// Resolves model for a task by checking loaded models first, then prompting user when needed.
    /// For image/vision tasks it only offers vision-capable models.
    /// </summary>
    public async Task<string?> ResolveModelForTaskAsync(LlmUsageKind usage, LlmTaskKind taskKind, IWin32Window? owner = null)
    {
        var settings = AppSettings.Current;
        string serverApiUrl = string.IsNullOrWhiteSpace(ApiUrl) ? settings.LlmApiUrl : ApiUrl;
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
                var response = await _httpClient.PostAsync(endpoint, content);
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
                var response = await _httpClient.PostAsync(endpoint, content);
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

    private static bool IsModelUnloadedError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("model unloaded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("model not loaded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("no model loaded", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("model_not_loaded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailedToProcessImageError(HttpStatusCode statusCode, string? text)
    {
        return statusCode == HttpStatusCode.BadRequest &&
               !string.IsNullOrWhiteSpace(text) &&
               text.Contains("failed to process image", StringComparison.OrdinalIgnoreCase);
    }

    private static int ComputeOcrTimeoutSeconds(int maxTokens)
    {
        // OCR on larger vision models can be slow even with moderate token counts.
        // Keep a sane floor above previous fixed 75s and cap to avoid indefinite waits.
        return Math.Clamp(60 + (maxTokens / 10), 120, 240);
    }

    private static int ComputeTranslationTimeoutSeconds(int maxTokens)
    {
        return Math.Clamp(50 + (maxTokens / 12), 90, 210);
    }

    private async Task<bool> TryRecoverVisionModelAsync(string apiUrl, string modelId, string stage)
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
                    var response = await _httpClient.PostAsync(endpoint, content);
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

    private static async Task<List<LlmModelInfo>> TryGetModelsFromEndpointAsync(string url, bool assumeLoaded)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
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

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string propertyName, out JsonElement value)
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
    public async Task<string> SendChatAsync(List<ChatMessage> history, bool taggingEnabled, bool searchEnabled, bool fullContext, bool thinkingEnabled, string? currentContext = null, string? currentDirectory = null, string? modelOverride = null)
    {
        var settings = AppSettings.Current;
        string apiUrl = settings.ChatModeEnabled ? settings.LlmChatApiUrl : settings.LlmApiUrl;
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;

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
    public async Task<string> SendPromptAsync(string userPrompt, string currentDir, bool fullContext, bool taggingEnabled, bool searchEnabled, bool thinkingEnabled, List<string>? imagePaths = null, string? modelOverride = null)
    {
        string dirContext = fullContext ? LlmPromptBuilder.BuildFullDirectoryContext(currentDir) : LlmPromptBuilder.BuildExtensionContext(currentDir);
        var enrichedPrompt = $"{userPrompt}\n\nContext items in directory:\n{dirContext}";

        string systemPrompt = LlmPromptBuilder.GetSystemPrompt(taggingEnabled, searchEnabled, fullContext, thinkingEnabled);

        // Get settings from AppSettings
        var settings = AppSettings.Current;
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = GetCompletionsApiUrl(string.IsNullOrWhiteSpace(ApiUrl) ? settings.LlmApiUrl : ApiUrl, null);
        
        object messages;
        string requestJson = "";
        bool isVisionRequest = imagePaths != null && imagePaths.Count > 0;

        (string json, List<LlmImageStats> stats) BuildVisionRequestJson(long maxPixels, int jpegQuality)
        {
            var contentList = new List<object>
            {
                new { type = "text", text = enrichedPrompt }
            };

            var imageStats = new List<LlmImageStats>();
            foreach (var imgPath in imagePaths!)
            {
                try
                {
                    var (imageBytes, stats) = LlmImageProcessor.PrepareImageForVision(imgPath, maxPixels, jpegQuality);
                    imageStats.Add(stats);
                    string base64 = Convert.ToBase64String(imageBytes);
                    contentList.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = $"data:image/jpeg;base64,{base64}"
                        }
                    });
                }
                catch (Exception ex)
                {
                    LlmDebugLogger.LogError($"Failed to load image for vision: {imgPath} - {ex.Message}");
                }
            }

            if (contentList.Count <= 1)
            {
                throw new InvalidOperationException("No valid images could be prepared for vision request.");
            }

            var requestMessages = new[]
            {
                new { role = "system", content = (object)systemPrompt },
                new { role = "user", content = (object)contentList }
            };

            var requestBody = new
            {
                model = model,
                messages = requestMessages,
                response_format = LlmPromptBuilder.GetJsonSchema(taggingEnabled, searchEnabled, fullContext, thinkingEnabled),
                temperature = settings.LlmTemperature,
                max_tokens = settings.LlmMaxTokens,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
            return (json, imageStats);
        }
        
        if (isVisionRequest)
        {
            var (json, imageStats) = BuildVisionRequestJson(1536L * 1536L, 85);
            requestJson = json;
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
                model = model,
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
            LlmDebugLogger.LogExecution($"SendPrompt endpoint: {requestUrl} | model: {model} | vision: {isVisionRequest}");
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(requestUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode &&
                isVisionRequest &&
                (IsModelUnloadedError(responseText) || IsFailedToProcessImageError(response.StatusCode, responseText)))
            {
                string reason = IsModelUnloadedError(responseText) ? "model-unloaded" : "failed-to-process-image";
                if (await TryRecoverVisionModelAsync(requestUrl, model, $"SendPrompt primary {reason}"))
                {
                    using var recoveryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(requestUrl, recoveryContent);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode &&
                isVisionRequest &&
                IsFailedToProcessImageError(response.StatusCode, responseText))
            {
                LlmDebugLogger.LogExecution("Vision request retry with aggressive resize/compression", success: false);
                var (retryJson, retryStats) = BuildVisionRequestJson(1024L * 1024L, 70);
                LlmDebugLogger.LogRequest(currentDir, userPrompt, systemPrompt, retryJson, imagePaths, retryStats);

                using var retryContent = new StringContent(retryJson, Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync(requestUrl, retryContent);
                responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode &&
                    (IsModelUnloadedError(responseText) || IsFailedToProcessImageError(response.StatusCode, responseText)))
                {
                    string reason = IsModelUnloadedError(responseText) ? "model-unloaded" : "failed-to-process-image";
                    if (await TryRecoverVisionModelAsync(requestUrl, model, $"SendPrompt retry {reason}"))
                    {
                        using var retryRecoveryContent = new StringContent(retryJson, Encoding.UTF8, "application/json");
                        response = await _httpClient.PostAsync(requestUrl, retryRecoveryContent);
                        responseText = await response.Content.ReadAsStringAsync();
                    }
                }
            }

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
    public async Task<List<string>> GetImageTagsAsync(string userPrompt, string imagePath, string? modelOverride = null)
    {
        var settings = AppSettings.Current;
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = GetCompletionsApiUrl(string.IsNullOrWhiteSpace(ApiUrl) ? settings.LlmApiUrl : ApiUrl, null);
        
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
            model = model,
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
            LlmDebugLogger.LogExecution($"GetImageTags endpoint: {requestUrl} | model: {model} | vision: true");
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(requestUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode &&
                (IsModelUnloadedError(responseText) || IsFailedToProcessImageError(response.StatusCode, responseText)))
            {
                string reason = IsModelUnloadedError(responseText) ? "model-unloaded" : "failed-to-process-image";
                if (await TryRecoverVisionModelAsync(requestUrl, model, $"GetImageTags primary {reason}"))
                {
                    using var recoveryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(requestUrl, recoveryContent);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode &&
                IsFailedToProcessImageError(response.StatusCode, responseText))
            {
                LlmDebugLogger.LogExecution("GetImageTags retry with aggressive resize/compression", success: false);
                var (retryBytes, retryStats) = LlmImageProcessor.PrepareImageForVision(imagePath, 1024L * 1024L, 70);
                string retryBase64 = Convert.ToBase64String(retryBytes);
                var retryContentList = new List<object>
                {
                    new { type = "text", text = userPrompt },
                    new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{retryBase64}" } }
                };
                var retryMessages = new[]
                {
                    new { role = "system", content = (object)systemPrompt },
                    new { role = "user", content = (object)retryContentList }
                };
                var retryBody = new
                {
                    model = model,
                    messages = retryMessages,
                    response_format = LlmPromptBuilder.GetTaggingJsonSchema(),
                    temperature = settings.LlmTemperature,
                    max_tokens = settings.LlmMaxTokens,
                    stream = false
                };

                requestJson = JsonSerializer.Serialize(retryBody, new JsonSerializerOptions { WriteIndented = true });
                LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, new[] { retryStats });

                using var retryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync(requestUrl, retryContent);
                responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode &&
                    (IsModelUnloadedError(responseText) || IsFailedToProcessImageError(response.StatusCode, responseText)))
                {
                    string reason = IsModelUnloadedError(responseText) ? "model-unloaded" : "failed-to-process-image";
                    if (await TryRecoverVisionModelAsync(requestUrl, model, $"GetImageTags retry {reason}"))
                    {
                        using var retryRecoveryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        response = await _httpClient.PostAsync(requestUrl, retryRecoveryContent);
                        responseText = await response.Content.ReadAsStringAsync();
                    }
                }
            }

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
    /// Performs OCR-like extraction with optional text blocks and normalized coordinates.
    /// Coordinates are normalized to [0..1] for image width/height.
    /// </summary>
    public async Task<LlmImageTextResult?> ExtractImageTextAsync(string imagePath, string? modelOverride = null)
    {
        var settings = AppSettings.Current;
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = GetCompletionsApiUrl(string.IsNullOrWhiteSpace(ApiUrl) ? settings.LlmApiUrl : ApiUrl, null);
        int ocrMaxTokens = Math.Min(settings.LlmMaxTokens, 1200);
        if (ocrMaxTokens < 256) ocrMaxTokens = 256;
        int ocrTimeoutSeconds = ComputeOcrTimeoutSeconds(ocrMaxTokens);

        async Task<bool> TryReloadModelAsync(string stage)
        {
            return await TryRecoverVisionModelAsync(requestUrl, model, $"ExtractImageText {stage}");
        }

        string systemPrompt =
            "You are an OCR extractor. Return strict JSON only. " +
            "Extract readable text from the image. " +
            "Be conservative with block count: merge nearby lines from the same text region and avoid duplicate/overlapping blocks. " +
            "Return blocks with coordinates x,y,w,h and optional font_size.";

        string userPrompt =
            "Extract readable text from this image.\n" +
            "Return text blocks in reading order.\n" +
            "Prefer fewer complete phrase blocks instead of one block per line.\n" +
            "Output JSON with: detected_language, full_text, blocks[{text,x,y,w,h,font_size?}].";

        var schema = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "image_ocr_blocks",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        detected_language = new { type = "string" },
                        full_text = new { type = "string" },
                        blocks = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    text = new { type = "string" },
                                    x = new { type = "number" },
                                    y = new { type = "number" },
                                    w = new { type = "number" },
                                    h = new { type = "number" },
                                    font_size = new { type = "number" }
                                },
                                required = new[] { "text", "x", "y", "w", "h" },
                                additionalProperties = false
                            }
                        }
                    },
                    required = new[] { "detected_language", "full_text", "blocks" },
                    additionalProperties = false
                }
            }
        };

        (string json, List<LlmImageStats> stats) BuildRequest(long maxPixels, int jpegQuality)
        {
            var (imageBytes, imageStats) = LlmImageProcessor.PrepareImageForVision(imagePath, maxPixels, jpegQuality);
            string base64 = Convert.ToBase64String(imageBytes);

            var contentList = new List<object>
            {
                new { type = "text", text = userPrompt },
                new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64}" } }
            };

            var messages = new[]
            {
                new { role = "system", content = (object)systemPrompt },
                new { role = "user", content = (object)contentList }
            };

            var requestBody = new
            {
                model = model,
                messages = messages,
                response_format = schema,
                temperature = Math.Min(settings.LlmTemperature, 0.2),
                max_tokens = ocrMaxTokens,
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
            return (json, new List<LlmImageStats> { imageStats });
        }

        async Task<LlmImageTextResult?> RunFallbackWithoutSchemaAsync(long maxPixels, int jpegQuality, string reason)
        {
            string fallbackSystemPrompt =
                "You are an OCR extractor. Return JSON if possible with keys: detected_language, full_text, blocks. " +
                "Blocks should contain objects {text,x,y,w,h,font_size?}. " +
                "Use conservative block count, merge nearby lines from the same region, and avoid duplicate/overlapping blocks. " +
                "If coordinates are uncertain, return blocks as empty array.";
            string fallbackUserPrompt =
                userPrompt + "\nIf JSON is not possible, return plain extracted text only.";

            var attempts = new List<(long MaxPixels, int Quality)>
            {
                (maxPixels, jpegQuality),
                (768L * 768L, 60),
                (640L * 640L, 55),
                (512L * 512L, 50),
                (448L * 448L, 45)
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attempt in attempts)
            {
                string attemptKey = $"{attempt.MaxPixels}:{attempt.Quality}";
                if (!seen.Add(attemptKey))
                    continue;

                try
                {
                    var (imageBytes, imageStats) = LlmImageProcessor.PrepareImageForVision(imagePath, attempt.MaxPixels, attempt.Quality);
                    string base64 = Convert.ToBase64String(imageBytes);

                    var contentList = new List<object>
                    {
                        new { type = "text", text = fallbackUserPrompt },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64}" } }
                    };

                    var messages = new[]
                    {
                        new { role = "system", content = (object)fallbackSystemPrompt },
                        new { role = "user", content = (object)contentList }
                    };

                    var requestBody = new
                    {
                        model = model,
                        messages = messages,
                        temperature = Math.Min(settings.LlmTemperature, 0.2),
                        max_tokens = ocrMaxTokens,
                        stream = false
                    };

                    string fallbackJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
                    LlmDebugLogger.LogExecution(
                        $"ExtractImageText fallback without response_format ({reason}) attempt {attempt.MaxPixels}px q{attempt.Quality}",
                        success: false);
                    LlmDebugLogger.LogRequest(
                        Path.GetDirectoryName(imagePath) ?? "",
                        fallbackUserPrompt,
                        fallbackSystemPrompt,
                        fallbackJson,
                        new[] { imagePath },
                        new[] { imageStats });

                    using var fallbackContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");
                    using var fallbackCts = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                    var fallbackResponse = await _httpClient.PostAsync(requestUrl, fallbackContent, fallbackCts.Token);
                    string fallbackResponseText = await fallbackResponse.Content.ReadAsStringAsync();

                    if (!fallbackResponse.IsSuccessStatusCode &&
                        (IsModelUnloadedError(fallbackResponseText) || IsFailedToProcessImageError(fallbackResponse.StatusCode, fallbackResponseText)))
                    {
                        if (await TryReloadModelAsync($"fallback {attempt.MaxPixels}px"))
                        {
                            using var fallbackRetryContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");
                            using var fallbackRetryCts = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                            fallbackResponse = await _httpClient.PostAsync(requestUrl, fallbackRetryContent, fallbackRetryCts.Token);
                            fallbackResponseText = await fallbackResponse.Content.ReadAsStringAsync();
                        }
                    }

                    if (!fallbackResponse.IsSuccessStatusCode)
                    {
                        LlmDebugLogger.LogError($"ExtractImageText fallback API Error {fallbackResponse.StatusCode}: {fallbackResponseText}");
                        if (IsFailedToProcessImageError(fallbackResponse.StatusCode, fallbackResponseText))
                        {
                            await Task.Delay(120);
                            continue;
                        }
                        continue;
                    }

                    using var fallbackDoc = JsonDocument.Parse(fallbackResponseText);
                    if (!fallbackDoc.RootElement.TryGetProperty("choices", out var fallbackChoices) || fallbackChoices.GetArrayLength() == 0)
                        continue;

                    string fallbackMessage = fallbackChoices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                    LlmDebugLogger.LogResponse(fallbackMessage);

                    try
                    {
                        return ParseImageTextResult(fallbackMessage);
                    }
                    catch
                    {
                        string plain = fallbackMessage.Trim();
                        if (string.IsNullOrWhiteSpace(plain))
                            continue;

                        return new LlmImageTextResult
                        {
                            DetectedLanguage = "",
                            FullText = plain,
                            Blocks = new List<LlmImageTextBlock>()
                        };
                    }
                }
                catch (Exception ex)
                {
                    LlmDebugLogger.LogError($"ExtractImageText fallback failed for {attempt.MaxPixels}px q{attempt.Quality}: {ex.Message}");
                }
            }

            return null;
        }

        string requestJson;
        List<LlmImageStats> stats;
        try
        {
            (requestJson, stats) = BuildRequest(1536L * 1536L, 85);
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"ExtractImageText: failed to prepare image {imagePath}: {ex.Message}");
            return null;
        }

        LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, stats);

        try
        {
            LlmDebugLogger.LogExecution($"ExtractImageText endpoint: {requestUrl} | model: {model} | vision: true | timeout: {ocrTimeoutSeconds}s");
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
            var response = await _httpClient.PostAsync(requestUrl, content, requestCts.Token);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode &&
                (IsModelUnloadedError(responseText) || IsFailedToProcessImageError(response.StatusCode, responseText)))
            {
                if (await TryReloadModelAsync("primary"))
                {
                    using var reloadRetryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using var reloadRetryCts = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                    response = await _httpClient.PostAsync(requestUrl, reloadRetryContent, reloadRetryCts.Token);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode &&
                IsFailedToProcessImageError(response.StatusCode, responseText))
            {
                LlmDebugLogger.LogExecution("ExtractImageText retry with aggressive resize/compression", success: false);
                (requestJson, stats) = BuildRequest(1024L * 1024L, 70);
                LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, stats);

                using var retryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var retryCts = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                response = await _httpClient.PostAsync(requestUrl, retryContent, retryCts.Token);
                responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode &&
                    (IsModelUnloadedError(responseText) || IsFailedToProcessImageError(response.StatusCode, responseText)))
                {
                    if (await TryReloadModelAsync("retry-1"))
                    {
                        using var retryReloadContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        using var retryReloadCts = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                        response = await _httpClient.PostAsync(requestUrl, retryReloadContent, retryReloadCts.Token);
                        responseText = await response.Content.ReadAsStringAsync();
                    }
                }

                if (!response.IsSuccessStatusCode &&
                    IsFailedToProcessImageError(response.StatusCode, responseText))
                {
                    LlmDebugLogger.LogExecution("ExtractImageText second retry with ultra resize/compression", success: false);
                    (requestJson, stats) = BuildRequest(768L * 768L, 60);
                    LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, stats);

                    using var retryContent2 = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using var retryCts2 = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                    response = await _httpClient.PostAsync(requestUrl, retryContent2, retryCts2.Token);
                    responseText = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode &&
                        (IsModelUnloadedError(responseText) || IsFailedToProcessImageError(response.StatusCode, responseText)))
                    {
                        if (await TryReloadModelAsync("retry-2"))
                        {
                            using var retryReloadContent2 = new StringContent(requestJson, Encoding.UTF8, "application/json");
                            using var retryReloadCts2 = new CancellationTokenSource(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                            response = await _httpClient.PostAsync(requestUrl, retryReloadContent2, retryReloadCts2.Token);
                            responseText = await response.Content.ReadAsStringAsync();
                        }
                    }
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"ExtractImageText API Error {response.StatusCode}: {responseText}");
                return await RunFallbackWithoutSchemaAsync(896L * 896L, 65, $"{(int)response.StatusCode} {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            string messageContent = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            LlmDebugLogger.LogResponse(messageContent);
            try
            {
                return ParseImageTextResult(messageContent);
            }
            catch
            {
                string plain = messageContent.Trim();
                if (string.IsNullOrWhiteSpace(plain))
                    return null;

                return new LlmImageTextResult
                {
                    DetectedLanguage = "",
                    FullText = plain,
                    Blocks = new List<LlmImageTextBlock>()
                };
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"ExtractImageText failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Translates OCR block text preserving input order.
    /// </summary>
    public async Task<LlmTextTranslationResult?> TranslateTextBlocksAsync(
        IReadOnlyList<string> sourceBlocks,
        string targetLanguage,
        string? sourceLanguage = null,
        string? modelOverride = null)
    {
        var settings = AppSettings.Current;
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = GetCompletionsApiUrl(string.IsNullOrWhiteSpace(ApiUrl) ? settings.LlmApiUrl : ApiUrl, null);
        string target = string.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
        int translationMaxTokens = Math.Min(settings.LlmMaxTokens, 1400);
        if (translationMaxTokens < 256) translationMaxTokens = 256;
        int translationTimeoutSeconds = ComputeTranslationTimeoutSeconds(translationMaxTokens);

        var cleanedBlocks = sourceBlocks?
            .Select(b => b?.Trim() ?? "")
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList() ?? new List<string>();

        if (cleanedBlocks.Count == 0)
        {
            return new LlmTextTranslationResult
            {
                TargetLanguage = target,
                TranslatedFullText = "",
                Translations = new List<string>()
            };
        }

        string systemPrompt =
            "You are a translation engine. Return strict JSON only. " +
            "Translate each input text block into the requested target language. " +
            "Do not omit blocks. Preserve order exactly.";

        var numbered = new StringBuilder();
        for (int i = 0; i < cleanedBlocks.Count; i++)
        {
            numbered.AppendLine($"{i + 1}. {cleanedBlocks[i]}");
        }

        string userPrompt =
            $"Target language: {target}\n" +
            $"Source language hint: {(string.IsNullOrWhiteSpace(sourceLanguage) ? "unknown" : sourceLanguage)}\n" +
            "Translate each numbered block. Keep line breaks where meaningful.\n" +
            "Input blocks:\n" +
            numbered.ToString();

        var schema = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "translated_text_blocks",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        translated_full_text = new { type = "string" },
                        translations = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "translated_full_text", "translations" },
                    additionalProperties = false
                }
            }
        };

        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        var requestBody = new
        {
            model = model,
            messages = messages,
            response_format = schema,
            temperature = Math.Min(settings.LlmTemperature, 0.2),
            max_tokens = translationMaxTokens,
            stream = false
        };

        var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
        LlmDebugLogger.LogRequest("", userPrompt, systemPrompt, requestJson);

        try
        {
            LlmDebugLogger.LogExecution($"TranslateTextBlocks endpoint: {requestUrl} | model: {model} | vision: false | timeout: {translationTimeoutSeconds}s");
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(translationTimeoutSeconds));
            var response = await _httpClient.PostAsync(requestUrl, content, requestCts.Token);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"TranslateTextBlocks API Error {response.StatusCode}: {responseText}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            string messageContent = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            LlmDebugLogger.LogResponse(messageContent);

            var parsed = ParseTranslationResult(messageContent, target);
            if (parsed == null)
                return null;

            if (parsed.Translations.Count != cleanedBlocks.Count)
            {
                if (parsed.Translations.Count == 0 && !string.IsNullOrWhiteSpace(parsed.TranslatedFullText))
                {
                    parsed.Translations = new List<string> { parsed.TranslatedFullText };
                }
                else if (parsed.Translations.Count < cleanedBlocks.Count)
                {
                    while (parsed.Translations.Count < cleanedBlocks.Count)
                        parsed.Translations.Add(parsed.TranslatedFullText);
                }
                else if (parsed.Translations.Count > cleanedBlocks.Count)
                {
                    parsed.Translations = parsed.Translations.Take(cleanedBlocks.Count).ToList();
                }
            }

            return parsed;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"TranslateTextBlocks failed: {ex.Message}");
            return null;
        }
    }

    private static LlmImageTextResult? ParseImageTextResult(string rawContent)
    {
        string cleanJson = ExtractJsonObject(rawContent);
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

                // Keep raw coordinates as returned by model. Some models return pixels, not normalized values.
                float x = ReadJsonFloat(block, "x");
                float y = ReadJsonFloat(block, "y");
                float w = ReadJsonFloat(block, "w");
                float h = ReadJsonFloat(block, "h");
                float fontSize = ReadJsonFloatAny(block, "font_size", "fontSize", "size");
                if (w <= 0f || h <= 0f)
                    continue;

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

    private static LlmTextTranslationResult? ParseTranslationResult(string rawContent, string targetLanguage)
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

    private static float ReadJsonFloat(JsonElement obj, string key)
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

    private static float ReadJsonFloatAny(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            float value = ReadJsonFloat(obj, key);
            if (value > 0f)
                return value;
        }

        return 0f;
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    private static string ExtractJsonObject(string content)
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
