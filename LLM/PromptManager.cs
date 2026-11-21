using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpeedExplorer;

public class PromptManager
{
    private static string _settingsPath = ResolveSettingsPath();
    private static PromptManager? _instance;
    public static PromptManager Instance => _instance ??= new PromptManager();

    public Dictionary<string, string> BatchPrompts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BatchPromptModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string ResolveSettingsPath()
    {
        return AppStorage.GetPath("prompts.json");
    }

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
                var defaults = CreateDefaultBatchPrompts();
                var defaultModes = CreateDefaultBatchPromptModes();
                var savedPrompts = data?.BatchPrompts;
                var savedModes = data?.BatchPromptModes;
                var mergedPrompts = defaults;
                var mergedModes = defaultModes;
                bool addedMissingDefaults = savedPrompts == null;

                if (savedPrompts != null)
                {
                    foreach (var (name, prompt) in savedPrompts)
                    {
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt))
                        {
                            addedMissingDefaults = true;
                            continue;
                        }

                        mergedPrompts[name.Trim()] = prompt;
                    }

                    addedMissingDefaults |= defaults.Keys.Any(name => !savedPrompts.ContainsKey(name));
                }

                if (savedModes != null)
                {
                    foreach (var (name, mode) in savedModes)
                    {
                        if (mergedPrompts.ContainsKey(name))
                            mergedModes[name] = NormalizeMode(mode);
                    }
                }

                foreach (string name in mergedPrompts.Keys)
                {
                    if (!mergedModes.ContainsKey(name))
                    {
                        mergedModes[name] = InferMode(name);
                        addedMissingDefaults = true;
                    }
                }

                addedMissingDefaults |= savedModes == null ||
                    mergedPrompts.Keys.Any(name => !savedModes.ContainsKey(name));

                BatchPrompts = mergedPrompts;
                BatchPromptModes = mergedModes;
                if (addedMissingDefaults)
                    Save();
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

    public bool Save()
    {
        try
        {
            var data = new PromptData { BatchPrompts = BatchPrompts, BatchPromptModes = BatchPromptModes };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            _settingsPath = AppStorage.WriteText("prompts.json", json);
            return true;
        }
        catch (Exception ex)
        {
            LlmDebugLogger.LogError($"Failed to save prompts: {ex.Message}");
            return false;
        }
    }

    public void ResetToDefaults()
    {
        BatchPrompts = CreateDefaultBatchPrompts();
        BatchPromptModes = CreateDefaultBatchPromptModes();
        Save();
    }

    public void SaveBatchPrompt(string name, string prompt, bool isTaggingMode)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A prompt name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt text is required.", nameof(prompt));

        BatchPrompts ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        BatchPromptModes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        BatchPrompts = new Dictionary<string, string>(BatchPrompts, StringComparer.OrdinalIgnoreCase);
        BatchPromptModes = new Dictionary<string, string>(BatchPromptModes, StringComparer.OrdinalIgnoreCase);

        BatchPrompts[name.Trim()] = prompt;
        BatchPromptModes[name.Trim()] = isTaggingMode ? "Tagging" : "General";
        if (!Save())
            throw new IOException("Could not save prompts to prompts.json.");
    }

    private static Dictionary<string, string> CreateDefaultBatchPrompts() => new(StringComparer.OrdinalIgnoreCase)
    {
        { "Image to Text", "Write down the text from this image and save it to a text file." },
        { "Describe Image", "Describe this image in detail and save the description to a text file." },
        { "Tag Image", "Analyze this image and apply tags with most defining features of the image using the 'tag_selected' command. 20 tags or less." },
        { "Renamer", "Rename this file based on its visual or text content. Keep the original extension." },
        { "Analyze Tech Stack", "Analyze this code file and explain the technology stack used." }
    };

    private static Dictionary<string, string> CreateDefaultBatchPromptModes() => new(StringComparer.OrdinalIgnoreCase)
    {
        { "Image to Text", "General" },
        { "Describe Image", "General" },
        { "Tag Image", "Tagging" },
        { "Renamer", "General" },
        { "Analyze Tech Stack", "General" }
    };

    private static string NormalizeMode(string? mode) =>
        string.Equals(mode, "Tagging", StringComparison.OrdinalIgnoreCase) ? "Tagging" : "General";

    private static string InferMode(string name) =>
        name.Contains("Tag", StringComparison.OrdinalIgnoreCase) ? "Tagging" : "General";
}

public class PromptData
{
    public Dictionary<string, string> BatchPrompts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BatchPromptModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
