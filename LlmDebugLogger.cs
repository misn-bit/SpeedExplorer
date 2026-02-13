using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SpeedExplorer;

/// <summary>
/// Debug logger for LLM interactions. Logs to llm_debug.log in app directory.
/// </summary>
public static class LlmDebugLogger
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "llm_debug.log");
    private static readonly object _lock = new object();

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
        sb.AppendLine(sanitizedJson);
        Write(sb.ToString());
    }

    public static void LogResponse(string rawResponse, string? parsedCommands = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"=== LLM RESPONSE [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===");
        sb.AppendLine("Raw Response:");
        sb.AppendLine(rawResponse);
        if (!string.IsNullOrEmpty(parsedCommands))
        {
            sb.AppendLine("Parsed Commands:");
            sb.AppendLine(parsedCommands);
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
            catch { }
        }
    }
}
