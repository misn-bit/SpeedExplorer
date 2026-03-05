using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpeedExplorer;

/// <summary>
/// Debug logger for LLM interactions. Logs to llm_debug.log in app directory.
/// </summary>
public static class LlmDebugLogger
{
    private static readonly string LogPath = Path.Combine(GetAppDirectory(), "llm_debug.log");
    private static readonly object _lock = new object();
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void LogRequest(string currentDir, string userPrompt, string systemPrompt, string fullRequestJson, IEnumerable<string>? imagePaths = null, IEnumerable<LlmImageStats>? imageStats = null)
    {
        string sanitizedJson = fullRequestJson;
        
        if (imagePaths != null && imagePaths.Any())
        {
            int idx = 0;
            var pathList = imagePaths.ToList();
            // Match the data URI in the JSON and replace it with name/location
            sanitizedJson = Regex.Replace(fullRequestJson, @"""url"":\s*""data:image\/[^;]+;base64,[^""]+""", m => 
            {
                if (idx < pathList.Count)
                {
                    string path = pathList[idx++];
                    return @"""url"": ""[IMAGE: " + Path.GetFileName(path) + " at " + path + @"]""";
                }
                return m.Value;
            });
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"=== LLM REQUEST [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===");
        sb.AppendLine($"Current Directory: {currentDir}");
        sb.AppendLine($"User Prompt: {userPrompt}");
        
        if (imageStats != null && imageStats.Any())
        {
            sb.AppendLine("Vision Metadata:");
            foreach (var stat in imageStats)
            {
                sb.AppendLine($"  - {Path.GetFileName(stat.Path)}: {stat.OrigW}x{stat.OrigH} -> {stat.NewW}x{stat.NewH} ({stat.Bytes / 1024.0:F1} KB)");
            }
        }

        sb.AppendLine($"System Prompt: {systemPrompt}");
        sb.AppendLine("Full Request JSON (Images Redacted):");
        sb.AppendLine(NormalizeJsonForLog(sanitizedJson));
        Write(sb.ToString());
    }

    public static void LogResponse(string rawResponse, string? parsedCommands = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"=== LLM RESPONSE [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===");
        sb.AppendLine("Raw Response:");
        sb.AppendLine(NormalizeJsonForLog(rawResponse));
        if (!string.IsNullOrEmpty(parsedCommands))
        {
            sb.AppendLine("Parsed Commands:");
            sb.AppendLine(NormalizeJsonForLog(parsedCommands));
        }
        Write(sb.ToString());
    }

    public static void LogExecution(string message, bool success = true)
    {
        var prefix = success ? "[OK]" : "[FAIL]";
        Write($"{prefix} {message}");
    }

    public static void LogError(string error)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"=== LLM ERROR [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===");
        sb.AppendLine(error);
        Write(sb.ToString());
    }

    private static void Write(string text)
    {
        lock (_lock)
        {
            try
            {
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
            catch { /* Silently fail if can't write log */ }
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(LogPath))
                    File.Delete(LogPath);
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }
        }
    }

    private static string NormalizeJsonForLog(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        try
        {
            using var doc = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(doc.RootElement, LogJsonOptions);
        }
        catch
        {
            // Fallback: decode basic Unicode escapes in plain text payloads.
            return Regex.Replace(input, @"\\u([0-9a-fA-F]{4})", m =>
            {
                int code = Convert.ToInt32(m.Groups[1].Value, 16);
                return char.ConvertFromUtf32(code);
            });
        }
    }

    private static string GetAppDirectory()
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                string? dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir;
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

        try
        {
            if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
                return AppContext.BaseDirectory;
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine(__ex); }

        return Environment.CurrentDirectory;
    }
}
