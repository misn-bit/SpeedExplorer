using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>
    /// Specialized method for getting tags from an image based on user criteria.
    /// Returns a list of tags.
    /// </summary>
    public async Task<List<string>> GetImageTagsAsync(string userPrompt, string imagePath, string apiUrl, string? modelOverride = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = AppSettings.Current;
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
    public async Task<LlmImageTextResult?> ExtractImageTextAsync(string imagePath, string apiUrl, string? modelOverride = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = AppSettings.Current;
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
                        allowReasoningFallback: true);
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
                var earlyFallback = await RunFallbackWithoutSchemaAsync(1536L * 1536L, 85, "primary failed_to_process_image");
                if (earlyFallback != null)
                    return earlyFallback;

                LlmDebugLogger.LogExecution("ExtractImageText retry with aggressive resize/compression", success: false);
                (requestJson, stats) = BuildRequest(1024L * 1024L, 70);
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
                    (requestJson, stats) = BuildRequest(768L * 768L, 60);
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
                return await RunFallbackWithoutSchemaAsync(896L * 896L, 65, $"{(int)response.StatusCode} {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            string messageContent = LlmParsers.ExtractAssistantMessageText(
                choices[0].GetProperty("message"),
                allowReasoningFallback: true);
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

    /// <summary>
    /// Translates OCR block text preserving input order.
    /// </summary>
    public async Task<LlmTextTranslationResult?> TranslateTextBlocksAsync(
        IReadOnlyList<string> sourceBlocks,
        string targetLanguage,
        string apiUrl,
        string? sourceLanguage = null,
        string? modelOverride = null,
        CancellationToken cancellationToken = default)
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
                allowReasoningFallback: true);
            LlmDebugLogger.LogResponse(messageContent);

            var parsed = LlmParsers.ParseTranslationResult(messageContent, target);
            if (parsed == null)
                return null;

            parsed.Translations = NormalizeTranslationLines(parsed.Translations, parsed.TranslatedFullText, cleanedBlocks.Count);
            if (string.IsNullOrWhiteSpace(parsed.TranslatedFullText) && parsed.Translations.Count > 0)
                parsed.TranslatedFullText = string.Join(Environment.NewLine, parsed.Translations);

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

    private static List<string> NormalizeTranslationLines(IReadOnlyList<string>? rawLines, string? fullText, int expectedCount)
    {
        var normalized = new List<string>();
        if (rawLines != null)
        {
            for (int i = 0; i < rawLines.Count; i++)
            {
                string line = StripOrderedPrefix(rawLines[i] ?? "");
                if (!string.IsNullOrWhiteSpace(line))
                    normalized.Add(line);
            }
        }

        var extractedFromFull = ExtractOrderedLines(fullText ?? "");
        if (normalized.Count == 0 && extractedFromFull.Count > 0)
            normalized.AddRange(extractedFromFull);

        if (normalized.Count == 1 && expectedCount > 1 && extractedFromFull.Count > 1)
        {
            normalized.Clear();
            normalized.AddRange(extractedFromFull);
        }

        if (normalized.Count > expectedCount)
            return normalized.Take(expectedCount).ToList();

        if (normalized.Count == expectedCount)
            return normalized;

        if (normalized.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
            normalized.Add(StripOrderedPrefix(fullText));

        while (normalized.Count < expectedCount)
            normalized.Add(string.Empty);

        return normalized;
    }

    private static List<string> ExtractOrderedLines(string text)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string raw in normalized.Split('\n'))
        {
            string line = StripOrderedPrefix(raw);
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return lines;
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
