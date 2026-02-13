using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SpeedExplorer;

public class PromptManager
{
    private static readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prompts.json");
    private static PromptManager? _instance;
    public static PromptManager Instance => _instance ??= new PromptManager();

    public Dictionary<string, string> BatchPrompts { get; set; } = new();

    private PromptManager()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var data = JsonSerializer.Deserialize<PromptData>(json);
                if (data?.BatchPrompts != null)
                {
                    BatchPrompts = data.BatchPrompts;
                }
            }
            else
            {
                ResetToDefaults();
            }
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to load prompts: {ex.Message}");
            ResetToDefaults();
        }
    }

    public void Save()
    {
        try
        {
            var data = new PromptData { BatchPrompts = BatchPrompts };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to save prompts: {ex.Message}");
        }
    }

    public void ResetToDefaults()
    {
        BatchPrompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Image to Text", "Write down the text from this image and save it to a text file." },
            { "Describe Image", "Describe this image in detail and save the description to a text file." },
            { "Tag Image", "Analyze this image and apply tags with most defining features of the image using the 'tag_selected' command. 20 tags or less." },
            { "Renamer", "Rename this file based on its visual content. Keep the extension." },
            { "Analyze Tech Stack", "Analyze this code file and explain the technology stack used." }
        };
        Save();
    }
}

public class PromptData
{
    public Dictionary<string, string> BatchPrompts { get; set; } = new();
}
