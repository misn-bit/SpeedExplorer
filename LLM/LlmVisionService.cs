using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Processing;

namespace SpeedExplorer;

/// <summary>
/// Handles vision-specific LLM tasks like OCR, image tagging, and text translation.
/// Relies on LlmModelManager for HTTP client and model recovery.
/// </summary>
public class LlmVisionService
{
    private readonly LlmModelManager _modelManager;

    public LlmVisionService(LlmModelManager modelManager)
    {
        _modelManager = modelManager;
    }

    private static string WithReasoningDirective(string prompt, bool useReasoning)
    {
        string directive = useReasoning
            ? "Reasoning mode: enabled. If the requested JSON schema contains a thought field, use it for brief reasoning before the final answer fields."
            : "Reasoning mode: disabled. Answer directly in the requested format.";
        return $"{prompt}\n\n{directive}";
    }

    private static string FormatOptionalPromptLine(string label, string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : $"{label}: {value.Trim()}\n";
    }

    /// <summary>
    /// Specialized method for getting tags from an image based on user criteria.
    /// Returns a list of tags.
    /// </summary>
    public async Task<List<string>> GetImageTagsAsync(string userPrompt, string imagePath, string apiUrl, string? modelOverride = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = AppSettings.Current;
        long visionMaxPixels = LlmImageProcessor.GetConfiguredVisionMaxPixels();
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = LlmModelManager.GetCompletionsApiUrl(string.IsNullOrWhiteSpace(apiUrl) ? settings.LlmApiUrl : apiUrl, null);
        
        string systemPrompt = "You are an automated image tagger. Analyze the provided image and generate relevant tags based strictly on the user's instructions. Output purely a JSON object with a 'tags' array.";

        var contentList = new List<object>
        {
            new { type = "text", text = userPrompt }
        };

        var stats = new LlmImageStats { Path = imagePath };
        try 
        {
            var (imageBytes, s) = LlmImageProcessor.PrepareImageForVision(imagePath, visionMaxPixels);
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
            max_tokens = Math.Max(settings.LlmMaxTokens, 2048), // Allow more for tags if setting is low
            stream = false
        };

        var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
        LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, new[] { stats });

        try
        {
            LlmDebugLogger.LogExecution($"GetImageTags endpoint: {requestUrl} | model: {model} | vision: true");
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode &&
                LlmModelManager.IsModelUnloadedError(responseText))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await _modelManager.TryRecoverVisionModelAsync(requestUrl, model, "GetImageTags primary model-unloaded", cancellationToken))
                {
                    using var recoveryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    response = await LlmModelManager.HttpClient.PostAsync(requestUrl, recoveryContent, cancellationToken);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode &&
                LlmModelManager.IsFailedToProcessImageError(response.StatusCode, responseText))
            {
                LlmDebugLogger.LogExecution("GetImageTags retry with aggressive resize/compression", success: false);
                var (retryBytes, retryStats) = LlmImageProcessor.PrepareImageForVision(imagePath, Math.Min(visionMaxPixels, 1024L * 1024L), 70);
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
                response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryContent, cancellationToken);
                responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode &&
                    LlmModelManager.IsModelUnloadedError(responseText))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await _modelManager.TryRecoverVisionModelAsync(requestUrl, model, "GetImageTags retry model-unloaded", cancellationToken))
                    {
                        using var retryRecoveryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryRecoveryContent, cancellationToken);
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
            
            var messageContent = LlmParsers.ExtractAssistantMessageText(
                choices[0].GetProperty("message"),
                allowReasoningFallback: true);
            LlmDebugLogger.LogResponse(messageContent);

            using var resultDoc = JsonDocument.Parse(LlmParsers.ExtractJsonObject(messageContent));
            if (resultDoc.RootElement.TryGetProperty("tags", out var tagsArray))
            {
                return tagsArray.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
            return new List<string>();
        }
        catch (OperationCanceledException)
        {
            throw;
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
    public async Task<LlmImageTextResult?> ExtractImageTextAsync(string imagePath, string apiUrl, string? modelOverride = null, CancellationToken cancellationToken = default, bool useReasoning = false, string? sourceLanguageHint = null, string? ocrHint = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = AppSettings.Current;
        long visionMaxPixels = LlmImageProcessor.GetConfiguredVisionMaxPixels();
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = LlmModelManager.GetCompletionsApiUrl(string.IsNullOrWhiteSpace(apiUrl) ? settings.LlmApiUrl : apiUrl, null);
        int ocrMaxTokens = Math.Max(settings.LlmMaxTokens, 5000);
        if (ocrMaxTokens < 256) ocrMaxTokens = 256;
        int ocrTimeoutSeconds = LlmModelManager.ComputeOcrTimeoutSeconds(ocrMaxTokens);

        async Task<bool> TryReloadModelAsync(string stage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _modelManager.TryRecoverVisionModelAsync(requestUrl, model, $"ExtractImageText {stage}", cancellationToken);
        }

        string systemPrompt =
            "You are an OCR extractor. Return strict JSON only. " +
            "Extract readable text from the image. " +
            "Be conservative with block count: merge nearby lines from the same text region and avoid duplicate/overlapping blocks. " +
            "Return blocks with coordinates x,y,w,h and optional font_size. Coordinate range is from 0 to 1." +
            (useReasoning ? " Include a concise thought field explaining OCR interpretation choices before the final OCR fields." : "");

        string userPrompt = WithReasoningDirective(
            "Extract readable text from this image.\n" +
            FormatOptionalPromptLine("Expected source language or script", sourceLanguageHint) +
            FormatOptionalPromptLine("OCR hint", ocrHint) +
            "Return text blocks in reading order.\n" +
            "Prefer fewer complete phrase blocks instead of one block per line.\n" +
            "Output JSON with: detected_language, full_text, blocks[{text,x,y,w,h,font_size?}].",
            useReasoning);

        var properties = new Dictionary<string, object>
        {
            { "detected_language", new { type = "string" } },
            { "full_text", new { type = "string" } },
            {
                "blocks", new
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
            }
        };
        var required = new List<string> { "detected_language", "full_text", "blocks" };
        if (useReasoning)
        {
            properties = new Dictionary<string, object>
            {
                { "thought", new { type = "string" } },
                { "detected_language", new { type = "string" } },
                { "full_text", new { type = "string" } },
                {
                    "blocks", new
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
                }
            };
            required.Insert(0, "thought");
        }

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
                    properties,
                    required = required.ToArray(),
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
                userPrompt +
                "\nIf JSON is not possible, return plain extracted text only.";

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
                cancellationToken.ThrowIfCancellationRequested();
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
                    using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    fallbackCts.CancelAfter(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                    var fallbackResponse = await LlmModelManager.HttpClient.PostAsync(requestUrl, fallbackContent, fallbackCts.Token);
                    string fallbackResponseText = await fallbackResponse.Content.ReadAsStringAsync();

                    if (!fallbackResponse.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(fallbackResponseText))
                    {
                        if (await TryReloadModelAsync($"fallback {attempt.MaxPixels}px"))
                        {
                            using var fallbackRetryContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");
                            using var fallbackRetryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            fallbackRetryCts.CancelAfter(TimeSpan.FromSeconds(ocrTimeoutSeconds));
                            fallbackResponse = await LlmModelManager.HttpClient.PostAsync(requestUrl, fallbackRetryContent, fallbackRetryCts.Token);
                            fallbackResponseText = await fallbackResponse.Content.ReadAsStringAsync();
                        }
                    }

                    if (!fallbackResponse.IsSuccessStatusCode)
                    {
                        LlmDebugLogger.LogError($"ExtractImageText fallback API Error {fallbackResponse.StatusCode}: {fallbackResponseText}");
                        if (LlmModelManager.IsFailedToProcessImageError(fallbackResponse.StatusCode, fallbackResponseText))
                        {
                            await Task.Delay(120, cancellationToken);
                            continue;
                        }
                        continue;
                    }

                    using var fallbackDoc = JsonDocument.Parse(fallbackResponseText);
                    if (!fallbackDoc.RootElement.TryGetProperty("choices", out var fallbackChoices) || fallbackChoices.GetArrayLength() == 0)
                        continue;

                    string fallbackMessage = LlmParsers.ExtractAssistantMessageText(
                        fallbackChoices[0].GetProperty("message"),
                        allowReasoningFallback: useReasoning);
                    LlmDebugLogger.LogResponse(fallbackMessage);

                    try
                    {
                        return LlmParsers.ParseImageTextResult(fallbackMessage);
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
                catch (OperationCanceledException)
                {
                    throw;
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
            (requestJson, stats) = BuildRequest(visionMaxPixels, 85);
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
            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(responseText))
            {
                if (await TryReloadModelAsync("primary"))
                {
                    using var reloadRetryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    response = await LlmModelManager.HttpClient.PostAsync(requestUrl, reloadRetryContent, cancellationToken);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode &&
                LlmModelManager.IsFailedToProcessImageError(response.StatusCode, responseText))
            {
                LlmDebugLogger.LogExecution("ExtractImageText early fallback without response_format (primary failed to process image)", success: false);
                var earlyFallback = await RunFallbackWithoutSchemaAsync(visionMaxPixels, 85, "primary failed_to_process_image");
                if (earlyFallback != null)
                    return earlyFallback;

                LlmDebugLogger.LogExecution("ExtractImageText retry with aggressive resize/compression", success: false);
                (requestJson, stats) = BuildRequest(Math.Min(visionMaxPixels, 1024L * 1024L), 70);
                LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, stats);

                using var retryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryContent, cancellationToken);
                responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(responseText))
                {
                    if (await TryReloadModelAsync("retry-1"))
                    {
                        using var retryReloadContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryReloadContent, cancellationToken);
                        responseText = await response.Content.ReadAsStringAsync();
                    }
                }

                if (!response.IsSuccessStatusCode &&
                    LlmModelManager.IsFailedToProcessImageError(response.StatusCode, responseText))
                {
                    LlmDebugLogger.LogExecution("ExtractImageText second retry with ultra resize/compression", success: false);
                    (requestJson, stats) = BuildRequest(Math.Min(visionMaxPixels, 768L * 768L), 60);
                    LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, stats);

                    using var retryContent2 = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryContent2, cancellationToken);
                    responseText = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(responseText))
                    {
                        if (await TryReloadModelAsync("retry-2"))
                        {
                            using var retryReloadContent2 = new StringContent(requestJson, Encoding.UTF8, "application/json");
                            response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryReloadContent2, cancellationToken);
                            responseText = await response.Content.ReadAsStringAsync();
                        }
                    }
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"ExtractImageText API Error {response.StatusCode}: {responseText}");
                return await RunFallbackWithoutSchemaAsync(Math.Min(visionMaxPixels, 896L * 896L), 65, $"{(int)response.StatusCode} {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            string messageContent = LlmParsers.ExtractAssistantMessageText(
                choices[0].GetProperty("message"),
                allowReasoningFallback: useReasoning);
            LlmDebugLogger.LogResponse(messageContent);
            try
            {
                return LlmParsers.ParseImageTextResult(messageContent);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"ExtractImageText failed: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> ExtractSnippetTextAsync(string imagePath, string apiUrl, string? modelOverride = null, CancellationToken cancellationToken = default, bool useReasoning = false, string? sourceLanguageHint = null, string? ocrHint = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string model = string.IsNullOrWhiteSpace(modelOverride) ? AppSettings.Current.LlmModelName : modelOverride;
        string requestUrl = LlmModelManager.GetCompletionsApiUrl(string.IsNullOrWhiteSpace(apiUrl) ? AppSettings.Current.LlmApiUrl : apiUrl, null);
        int timeoutSeconds = LlmModelManager.ComputeOcrTimeoutSeconds(Math.Max(AppSettings.Current.LlmMaxTokens, 2048));

        async Task<bool> TryReloadModelAsync(string stage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _modelManager.TryRecoverVisionModelAsync(requestUrl, model, $"ExtractSnippetText {stage}", cancellationToken);
        }

        string systemPrompt =
            useReasoning
                ? "You are to OCR the image snippet. Return strict JSON only. Include a concise thought field explaining OCR interpretation choices before the final text field."
                : "You are to OCR the image snippet.";
        string userPrompt = WithReasoningDirective(
            (useReasoning
                ? "Extract text from this image snippet.\n"
                : "Return only the extracted text from this image snippet.\n") +
            FormatOptionalPromptLine("Expected source language or script", sourceLanguageHint) +
            FormatOptionalPromptLine("OCR hint", ocrHint) +
            "Preserve meaningful line breaks.\n" +
            (useReasoning
                ? "If no readable text is present, return an empty text field."
                : "If no readable text is present, return an empty response."),
            useReasoning);

        var snippetSchema = useReasoning
            ? new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "image_ocr_snippet",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        thought = new { type = "string" },
                        text = new { type = "string" }
                    },
                    required = new[] { "thought", "text" },
                    additionalProperties = false
                }
            }
        }
            : null;

        (string Json, List<LlmImageStats> Stats) BuildRequest(long maxPixels, int jpegQuality)
        {
            var (imageBytes, stats) = LlmImageProcessor.PrepareImageForVision(imagePath, maxPixels, jpegQuality);
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

            object requestBody = useReasoning
                ? new
                {
                    model = model,
                    messages = messages,
                    response_format = snippetSchema,
                    stream = false
                }
                : new
                {
                    model = model,
                    messages = messages,
                    stream = false
                };

            return (JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true }), new List<LlmImageStats> { stats });
        }

        async Task<string?> SendAsync(string requestJson, List<LlmImageStats> stats, string stage)
        {
            LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, stats);
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content, cts.Token);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(responseText))
            {
                if (await TryReloadModelAsync(stage))
                {
                    using var retryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    retryCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryContent, retryCts.Token);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"ExtractSnippetText API Error {response.StatusCode}: {responseText}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return "";

            string messageContent = LlmParsers.ExtractAssistantMessageText(
                choices[0].GetProperty("message"),
                allowReasoningFallback: useReasoning);
            LlmDebugLogger.LogResponse(messageContent);
            return ExtractSnippetTextFromResponse(messageContent);
        }

        try
        {
            LlmDebugLogger.LogExecution($"ExtractSnippetText endpoint: {requestUrl} | model: {model} | vision: true | timeout: {timeoutSeconds}s");

            long snippetMaxPixels = Math.Min(LlmImageProcessor.GetConfiguredVisionMaxPixels(), 1024L * 1024L);
            var (requestJson, stats) = BuildRequest(snippetMaxPixels, 95);
            string? result = await SendAsync(requestJson, stats, "primary");
            if (result != null)
                return result;

            LlmDebugLogger.LogExecution("ExtractSnippetText retry with smaller prepared JPEG payload", success: false);
            var (fallbackJson, fallbackStats) = BuildRequest(Math.Min(snippetMaxPixels, 768L * 768L), 85);
            return await SendAsync(fallbackJson, fallbackStats, "retry-jpeg");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"ExtractSnippetText failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Translates OCR block text preserving input order.
    /// </summary>
    public async Task<LlmTextTranslationResult?> TranslateTextBlocksAsync(
        IReadOnlyList<string> sourceBlocks,
        string targetLanguage,
        string apiUrl,
        string? sourceLanguage = null,
        string? contextHint = null,
        string? modelOverride = null,
        CancellationToken cancellationToken = default,
        bool useReasoning = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = AppSettings.Current;
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = LlmModelManager.GetCompletionsApiUrl(string.IsNullOrWhiteSpace(apiUrl) ? settings.LlmApiUrl : apiUrl, null);
        string target = string.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
        int translationMaxTokens = Math.Max(settings.LlmMaxTokens, 5000);
        if (translationMaxTokens < 256) translationMaxTokens = 256;
        int translationTimeoutSeconds = LlmModelManager.ComputeTranslationTimeoutSeconds(translationMaxTokens);

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
            "Do not omit blocks. Preserve order exactly. " +
            "Return exactly one string in the translations array for each input block. " +
            "If a single block needs multiple translated lines, keep them inside that one string using line breaks, not separate array entries.";

        var numbered = new StringBuilder();
        for (int i = 0; i < cleanedBlocks.Count; i++)
        {
            numbered.AppendLine($"{i + 1}. {cleanedBlocks[i]}");
        }

        string userPrompt = WithReasoningDirective(
            $"Target language: {target}\n" +
            $"Source language hint: {(string.IsNullOrWhiteSpace(sourceLanguage) ? "unknown" : sourceLanguage)}\n" +
            FormatOptionalPromptLine("General context hint", contextHint) +
            $"There are {cleanedBlocks.Count} input blocks.\n" +
            "Translate each numbered block.\n" +
            "The translations array must contain exactly one item per input block, in the same order.\n" +
            "If one block translates to multiple lines, keep those lines inside the same array string using \\n.\n" +
            "Input blocks:\n" +
            numbered.ToString(),
            useReasoning);

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
                        translations = new
                        {
                            type = "array",
                            minItems = cleanedBlocks.Count,
                            maxItems = cleanedBlocks.Count,
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "translations" },
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
            stream = false
        };

        var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
        LlmDebugLogger.LogRequest("", userPrompt, systemPrompt, requestJson);

        try
        {
            LlmDebugLogger.LogExecution($"TranslateTextBlocks endpoint: {requestUrl} | model: {model} | vision: false | timeout: {translationTimeoutSeconds}s");
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"TranslateTextBlocks API Error {response.StatusCode}: {responseText}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            string messageContent = LlmParsers.ExtractAssistantMessageText(
                choices[0].GetProperty("message"),
                allowReasoningFallback: useReasoning);
            LlmDebugLogger.LogResponse(messageContent);

            var parsed = LlmParsers.ParseTranslationResult(messageContent, target);
            if (parsed == null)
                return null;

            parsed.Translations = NormalizeTranslationLines(parsed.Translations, parsed.TranslatedFullText, cleanedBlocks.Count);
            parsed.TranslatedFullText = BuildNormalizedTranslationFullText(parsed.Translations, parsed.TranslatedFullText);

            return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"TranslateTextBlocks failed: {ex.Message}");
            return null;
        }
    }

    public async Task<LlmTextTranslationResult?> TranslateTextBlocksWithContextImageAsync(
        IReadOnlyList<string> sourceBlocks,
        string targetLanguage,
        string imagePath,
        string apiUrl,
        string? sourceLanguage = null,
        string? contextHint = null,
        string? modelOverride = null,
        CancellationToken cancellationToken = default,
        bool useReasoning = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = AppSettings.Current;
        string model = string.IsNullOrWhiteSpace(modelOverride) ? settings.LlmModelName : modelOverride;
        string requestUrl = LlmModelManager.GetCompletionsApiUrl(string.IsNullOrWhiteSpace(apiUrl) ? settings.LlmApiUrl : apiUrl, null);
        string target = string.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
        int translationMaxTokens = Math.Max(settings.LlmMaxTokens, 5000);
        if (translationMaxTokens < 256) translationMaxTokens = 256;
        int translationTimeoutSeconds = LlmModelManager.ComputeTranslationTimeoutSeconds(translationMaxTokens);

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
            "The OCR text blocks are the primary source of truth. " +
            "The attached image is for context clues only, such as ambiguous names or scene context. " +
            "Do not replace, invent, expand, or omit text based on what you think you see in the image. " +
            "Translate each input text block into the requested target language. " +
            "Preserve order exactly and return exactly one string in the translations array for each input block. " +
            "If a single block needs multiple translated lines, keep them inside that one string using line breaks, not separate array entries." +
            (useReasoning ? " Include a concise thought field explaining translation choices before the final translation fields." : "");

        var numbered = new StringBuilder();
        for (int i = 0; i < cleanedBlocks.Count; i++)
            numbered.AppendLine($"{i + 1}. {cleanedBlocks[i]}");

        string userPrompt = WithReasoningDirective(
            $"Target language: {target}\n" +
            $"Source language hint: {(string.IsNullOrWhiteSpace(sourceLanguage) ? "unknown" : sourceLanguage)}\n" +
            FormatOptionalPromptLine("General context hint", contextHint) +
            $"There are {cleanedBlocks.Count} OCR text blocks.\n" +
            "Focus on the OCR text blocks below.\n" +
            "Use the attached image only as supporting context when the OCR text is ambiguous.\n" +
            "Do not translate text you only think you see in the image if it is not present in the OCR blocks.\n" +
            "Return exactly one translation item per OCR block, in the same order.\n" +
            "If one block translates to multiple lines, keep those lines inside the same array string using \\n.\n" +
            "OCR text blocks:\n" +
            numbered.ToString(),
            useReasoning);

        var properties = new Dictionary<string, object>
        {
            {
                "translations", new
                {
                    type = "array",
                    minItems = cleanedBlocks.Count,
                    maxItems = cleanedBlocks.Count,
                    items = new { type = "string" }
                }
            }
        };
        var required = new List<string> { "translations" };
        if (useReasoning)
        {
            properties = new Dictionary<string, object>
            {
                { "thought", new { type = "string" } },
                {
                    "translations", new
                    {
                        type = "array",
                        minItems = cleanedBlocks.Count,
                        maxItems = cleanedBlocks.Count,
                        items = new { type = "string" }
                    }
                }
            };
            required.Insert(0, "thought");
        }

        var schema = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "translated_text_blocks_with_context",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties,
                    required = required.ToArray(),
                    additionalProperties = false
                }
            }
        };

        long contextMaxPixels = Math.Min(LlmImageProcessor.GetConfiguredVisionMaxPixels(), 1024L * 1024L);

        (string Json, LlmImageStats Stats) BuildContextImageRequest(long maxPixels, int jpegQuality)
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
                stream = false
            };

            return (JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true }), imageStats);
        }

        string requestJson;
        LlmImageStats imageStats;
        try
        {
            (requestJson, imageStats) = BuildContextImageRequest(contextMaxPixels, 75);
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"TranslateTextBlocksWithContextImage failed to prepare image: {ex.Message}");
            return await TranslateTextBlocksAsync(cleanedBlocks, targetLanguage, apiUrl, sourceLanguage, contextHint, modelOverride, cancellationToken, useReasoning);
        }

        LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, new[] { imageStats });

        try
        {
            LlmDebugLogger.LogExecution($"TranslateTextBlocksWithContextImage endpoint: {requestUrl} | model: {model} | vision: true | timeout: {translationTimeoutSeconds}s");
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(translationTimeoutSeconds));
            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content, cts.Token);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(responseText))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await _modelManager.TryRecoverVisionModelAsync(requestUrl, model, "TranslateTextBlocksWithContextImage primary model-unloaded", cancellationToken))
                {
                    using var recoveryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using var recoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    recoveryCts.CancelAfter(TimeSpan.FromSeconds(translationTimeoutSeconds));
                    response = await LlmModelManager.HttpClient.PostAsync(requestUrl, recoveryContent, recoveryCts.Token);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode &&
                LlmModelManager.IsFailedToProcessImageError(response.StatusCode, responseText))
            {
                LlmDebugLogger.LogExecution("TranslateTextBlocksWithContextImage retry with smaller context image", success: false);
                (requestJson, imageStats) = BuildContextImageRequest(Math.Min(contextMaxPixels, 768L * 768L), 60);
                LlmDebugLogger.LogRequest(Path.GetDirectoryName(imagePath) ?? "", userPrompt, systemPrompt, requestJson, new[] { imagePath }, new[] { imageStats });

                using var retryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                retryCts.CancelAfter(TimeSpan.FromSeconds(translationTimeoutSeconds));
                response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryContent, retryCts.Token);
                responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(responseText))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await _modelManager.TryRecoverVisionModelAsync(requestUrl, model, "TranslateTextBlocksWithContextImage retry model-unloaded", cancellationToken))
                    {
                        using var retryRecoveryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        using var retryRecoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        retryRecoveryCts.CancelAfter(TimeSpan.FromSeconds(translationTimeoutSeconds));
                        response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryRecoveryContent, retryRecoveryCts.Token);
                        responseText = await response.Content.ReadAsStringAsync();
                    }
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"TranslateTextBlocksWithContextImage API Error {response.StatusCode}: {responseText}");
                return await TranslateTextBlocksAsync(cleanedBlocks, targetLanguage, apiUrl, sourceLanguage, contextHint, modelOverride, cancellationToken, useReasoning);
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return await TranslateTextBlocksAsync(cleanedBlocks, targetLanguage, apiUrl, sourceLanguage, contextHint, modelOverride, cancellationToken, useReasoning);

            string messageContent = LlmParsers.ExtractAssistantMessageText(
                choices[0].GetProperty("message"),
                allowReasoningFallback: useReasoning);
            LlmDebugLogger.LogResponse(messageContent);

            var parsed = LlmParsers.ParseTranslationResult(messageContent, target);
            if (parsed == null)
                return await TranslateTextBlocksAsync(cleanedBlocks, targetLanguage, apiUrl, sourceLanguage, contextHint, modelOverride, cancellationToken, useReasoning);

            parsed.Translations = NormalizeTranslationLines(parsed.Translations, parsed.TranslatedFullText, cleanedBlocks.Count);
            parsed.TranslatedFullText = BuildNormalizedTranslationFullText(parsed.Translations, parsed.TranslatedFullText);
            return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"TranslateTextBlocksWithContextImage failed: {ex.Message}");
            return await TranslateTextBlocksAsync(cleanedBlocks, targetLanguage, apiUrl, sourceLanguage, contextHint, modelOverride, cancellationToken, useReasoning);
        }
    }

    public async Task<string?> TranslateSimpleTextAsync(
        string sourceText,
        string targetLanguage,
        string apiUrl,
        string? modelOverride = null,
        CancellationToken cancellationToken = default,
        bool useReasoning = true,
        string? contextHint = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sourceText))
            return "";

        string model = string.IsNullOrWhiteSpace(modelOverride) ? AppSettings.Current.LlmModelName : modelOverride;
        string requestUrl = LlmModelManager.GetCompletionsApiUrl(string.IsNullOrWhiteSpace(apiUrl) ? AppSettings.Current.LlmApiUrl : apiUrl, null);
        string target = string.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
        int timeoutSeconds = LlmModelManager.ComputeTranslationTimeoutSeconds(Math.Max(AppSettings.Current.LlmMaxTokens, 2048));

        async Task<bool> TryReloadModelAsync(string stage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _modelManager.TryRecoverVisionModelAsync(requestUrl, model, $"TranslateSimpleText {stage}", cancellationToken);
        }

        string systemPrompt = "You translate text accurately and naturally. Return only the translation.";
        string userPrompt = WithReasoningDirective(
            $"Translate this text to {target}:{Environment.NewLine}" +
            FormatOptionalPromptLine("General context hint", contextHint) +
            $"{Environment.NewLine}{sourceText}",
            useReasoning);

        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        var requestBody = new
        {
            model = model,
            messages = messages,
            stream = false
        };

        string requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
        LlmDebugLogger.LogRequest("", userPrompt, systemPrompt, requestJson);

        try
        {
            LlmDebugLogger.LogExecution($"TranslateSimpleText endpoint: {requestUrl} | model: {model} | vision: false | timeout: {timeoutSeconds}s");
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var response = await LlmModelManager.HttpClient.PostAsync(requestUrl, content, cts.Token);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode && LlmModelManager.IsModelUnloadedError(responseText))
            {
                if (await TryReloadModelAsync("primary"))
                {
                    using var retryContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    retryCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    response = await LlmModelManager.HttpClient.PostAsync(requestUrl, retryContent, retryCts.Token);
                    responseText = await response.Content.ReadAsStringAsync();
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                LlmDebugLogger.LogError($"TranslateSimpleText API Error {response.StatusCode}: {responseText}");
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return "";

            string messageContent = LlmParsers.ExtractAssistantMessageText(
                choices[0].GetProperty("message"),
                allowReasoningFallback: useReasoning);
            LlmDebugLogger.LogResponse(messageContent);
            return NormalizePlainTextResponse(messageContent);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"TranslateSimpleText failed: {ex.Message}");
            return null;
        }
    }

    private static List<string> NormalizeTranslationLines(IReadOnlyList<string>? rawLines, string? fullText, int expectedCount)
    {
        var directSegments = NormalizeDirectTranslationSegments(rawLines);
        var groupedFromArray = ExtractOrderedBlocks(rawLines);
        if (groupedFromArray.Count > 0)
            return FitTranslationBlockCount(groupedFromArray, expectedCount);

        var groupedFromFullText = ExtractOrderedBlocks(fullText);
        if (groupedFromFullText.Count > 0)
            return FitTranslationBlockCount(groupedFromFullText, expectedCount);

        if (directSegments.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
        {
            string single = StripOrderedPrefix(NormalizeTranslationSegment(fullText));
            if (!string.IsNullOrWhiteSpace(single))
                directSegments.Add(single);
        }

        return FitTranslationBlockCount(directSegments, expectedCount);
    }

    private static string BuildNormalizedTranslationFullText(IReadOnlyList<string> translations, string? fallbackFullText)
    {
        var lines = translations?
            .Select(NormalizeTranslationSegment)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList() ?? new List<string>();

        if (lines.Count > 0)
            return string.Join(Environment.NewLine, lines);

        return StripOrderedPrefix(NormalizeTranslationSegment(fallbackFullText ?? ""));
    }

    private static List<string> NormalizeDirectTranslationSegments(IReadOnlyList<string>? rawLines)
    {
        var normalized = new List<string>();
        if (rawLines == null)
            return normalized;

        for (int i = 0; i < rawLines.Count; i++)
        {
            string line = StripOrderedPrefix(NormalizeTranslationSegment(rawLines[i] ?? ""));
            if (!string.IsNullOrWhiteSpace(line))
                normalized.Add(line);
        }

        return normalized;
    }

    private static List<string> ExtractOrderedBlocks(IReadOnlyList<string>? rawLines)
    {
        if (rawLines == null || rawLines.Count == 0)
            return new List<string>();

        return ExtractOrderedBlocks(rawLines.Select(NormalizeTranslationSegment));
    }

    private static List<string> ExtractOrderedBlocks(string? text)
    {
        string normalized = NormalizeTranslationSegment(text ?? "");
        if (string.IsNullOrWhiteSpace(normalized))
            return new List<string>();

        return ExtractOrderedBlocks(new[] { normalized });
    }

    private static List<string> ExtractOrderedBlocks(IEnumerable<string> segments)
    {
        var blocks = new List<string>();
        StringBuilder? current = null;
        bool sawOrderedMarker = false;

        void FlushCurrent()
        {
            if (current == null)
                return;

            string value = NormalizeTranslationSegment(current.ToString());
            if (!string.IsNullOrWhiteSpace(value))
                blocks.Add(value);
            current = null;
        }

        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            string normalizedSegment = NormalizeTranslationSegment(segment);
            foreach (string rawLine in normalizedSegment.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (TryParseOrderedLine(line, out _, out string content))
                {
                    FlushCurrent();
                    current = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(content))
                        current.Append(content);
                    sawOrderedMarker = true;
                    continue;
                }

                if (current == null)
                {
                    if (!sawOrderedMarker)
                        continue;

                    current = new StringBuilder();
                }

                if (current.Length > 0)
                    current.AppendLine();
                current.Append(line);
            }
        }

        FlushCurrent();
        return sawOrderedMarker ? blocks : new List<string>();
    }

    private static List<string> FitTranslationBlockCount(List<string> blocks, int expectedCount)
    {
        if (expectedCount <= 0)
            return blocks;

        var normalized = blocks
            .Select(NormalizeTranslationSegment)
            .ToList();

        if (normalized.Count > expectedCount)
        {
            var merged = normalized.Take(expectedCount).ToList();
            for (int i = expectedCount; i < normalized.Count; i++)
            {
                string extra = normalized[i];
                if (string.IsNullOrWhiteSpace(extra))
                    continue;

                if (string.IsNullOrWhiteSpace(merged[expectedCount - 1]))
                    merged[expectedCount - 1] = extra;
                else
                    merged[expectedCount - 1] += Environment.NewLine + extra;
            }
            return merged;
        }

        while (normalized.Count < expectedCount)
            normalized.Add(string.Empty);

        return normalized;
    }

    private static bool TryParseOrderedLine(string text, out int order, out string content)
    {
        order = 0;
        content = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        int i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;

        if (i == 0 || i >= trimmed.Length)
            return false;

        char marker = trimmed[i];
        if (marker != '.' && marker != ')' && marker != ':' && marker != '-')
            return false;

        if (marker == ':' && i + 1 < trimmed.Length && !char.IsWhiteSpace(trimmed[i + 1]))
            return false;

        if (!int.TryParse(trimmed.Substring(0, i), out order))
            return false;

        i++;
        while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
            i++;

        content = i < trimmed.Length ? trimmed.Substring(i).Trim() : "";
        return true;
    }

    private static string NormalizeTranslationSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static string ExtractSnippetTextFromResponse(string messageContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(LlmParsers.ExtractJsonObject(messageContent));
            var root = doc.RootElement;
            if (root.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.String)
                LlmDebugLogger.LogResponse($"[OCR Snippet Thought]\n{thought.GetString()}\n");
            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return NormalizePlainTextResponse(text.GetString() ?? "");
        }
        catch
        {
            // Older/fallback servers may still return plain text despite the schema request.
        }

        return NormalizePlainTextResponse(messageContent);
    }

    private static string NormalizePlainTextResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = normalized.IndexOf('\n');
            int lastFence = normalized.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                normalized = normalized.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
        }

        return normalized;
    }

    private static string StripOrderedPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string trimmed = text.Trim();
        int i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;

        if (i > 0 && i < trimmed.Length)
        {
            char marker = trimmed[i];
            if (marker == '.' || marker == ')' || marker == ':' || marker == '-')
            {
                if (marker == ':' && i + 1 < trimmed.Length && !char.IsWhiteSpace(trimmed[i + 1]))
                    return trimmed;

                i++;
                while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                    i++;
                if (i < trimmed.Length)
                    return trimmed.Substring(i).Trim();
            }
        }

        return trimmed;
    }
}
