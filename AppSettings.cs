using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedExplorer;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    private static AppSettings? _instance;

    public static AppSettings Current => _instance ??= Load();

    // Default Settings
    public int FontSize { get; set; } = 10;
    public int IconSize { get; set; } = 16;
    public bool ShowIcons { get; set; } = true;
    public bool UseEmojiIcons { get; set; } = false; // Use emoji text instead of image icons
    public bool UseSystemIcons { get; set; } = true; // Colored system icons (vs grayscale)
    public bool ResolveUniqueIcons { get; set; } = false; // Specific icons for exe/lnk
    public bool ShowThumbnails { get; set; } = true;    // Image previews
    public bool UseBuiltInImageViewer { get; set; } = true;
    public System.Collections.Generic.Dictionary<string, int> FileColumnWidths { get; set; } = new()
    {
        ["col_name"] = 350,
        ["col_location"] = 200,
        ["col_size"] = 80,
        ["col_date_modified"] = 140,
        ["col_date_created"] = 140,
        ["col_type"] = 80,
        ["col_tags"] = 150
    };
    public System.Collections.Generic.Dictionary<string, int> DriveColumnWidths { get; set; } = new()
    {
        ["col_number"] = 48,
        ["col_name"] = 250,
        ["col_type"] = 100,
        ["col_format"] = 80,
        ["col_size"] = 100,
        ["col_capacity"] = 200,
        ["col_free_space"] = 120
    };
    public bool TileViewFolders { get; set; } = false;
    public bool TileViewThisPc { get; set; } = false;
    public bool ShowSidebarCommon { get; set; } = true; // Master toggle for system folders
    public bool ShowSidebarDesktop { get; set; } = true;
    public bool ShowSidebarDocuments { get; set; } = true;
    public bool ShowSidebarDownloads { get; set; } = true;
    public bool ShowSidebarPictures { get; set; } = true;
    public bool ShowSidebarRecent { get; set; } = true;
    public bool ShowSidebarVerticalScrollbar { get; set; } = true;
    public bool ShowSidebar { get; set; } = true;
    public bool RunAtStartup { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool ShowTrayIcon { get; set; } = true;
    public bool EnableShellContextMenu { get; set; } = false;
    public bool UseWindowsContextMenu { get; set; } = false;
    public string UiLanguage { get; set; } = "en";
    public System.Collections.Generic.List<ManualContextAction> ManualContextActions { get; set; } = new();
    public bool MiddleClickOpensNewTab { get; set; } = true;
    public bool DebugNavigationLogging { get; set; } = false;
    public bool DebugNavigationGcStats { get; set; } = false;
    public bool DebugNavigationUiQueue { get; set; } = false;
    public bool DebugNavigationPostBind { get; set; } = false;
    public bool DefaultFileManagerEnabled { get; set; } = false;
    public string DefaultFileManagerScope { get; set; } = "None";
    public string DefaultFileManagerBackupJson { get; set; } = "";
    public bool PermanentDeleteByDefault { get; set; } = false;

    // Window Sizes
    public int MainWindowWidth { get; set; } = 1200;
    public int MainWindowHeight { get; set; } = 800;
    public bool MainWindowMaximized { get; set; } = false;
    public bool MainWindowFullscreen { get; set; } = false;
    public int SettingsWidth { get; set; } = 600;
    public int SettingsHeight { get; set; } = 850;
    public int SidebarSplitDistance { get; set; } = 180;
    public double SidebarSplitRatio { get; set; } = 0.0;
    public bool SidebarSplitAtMinimum { get; set; } = false;
    public int EditTagsWidth { get; set; } = 400;
    public int EditTagsHeight { get; set; } = 310;
    public bool EditTagsMaximized { get; set; } = false;
    public int ImageViewerWidth { get; set; } = 1200;
    public int ImageViewerHeight { get; set; } = 900;
    public bool ImageViewerMaximized { get; set; } = false;
    public bool ImageViewerShowSavedOcr { get; set; } = true;

    public System.Collections.Generic.List<string> PinnedPaths { get; set; } = new();
    public System.Collections.Generic.List<string> SidebarBlockOrder { get; set; } = new()
    {
        "portable",
        "common",
        "pinned",
        "recent"
    };

    // LLM Settings
    public bool LlmEnabled { get; set; } = true;
    public string LlmApiUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
    public string LlmApiBaseUrl { get; set; } = "http://localhost:1234/api/v1/models"; // For /models
    public string LlmModelName { get; set; } = "qwen/qwen3-4b-thinking-2507";
    public string LlmBatchVisionModelName { get; set; } = "";
    public int LlmMaxTokens { get; set; } = 4096;
    public double LlmTemperature { get; set; } = 0.3;
    
    // Chat Mode Settings
    public string LlmChatApiUrl { get; set; } = "http://localhost:1234/api/v1/chat";
    public bool ChatModeEnabled { get; set; } = false;
    public double LlmChatPanelHeightRatio { get; set; } = 0.5; // Stored ratio for history panel height
    // LLM UI Toggles
    public bool LlmSearchEnabled { get; set; } = true;
    public bool LlmTaggingEnabled { get; set; } = true;
    public bool LlmFullContextEnabled { get; set; } = false;
    public bool LlmThinkingEnabled { get; set; } = true;
    
    public System.Collections.Generic.Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        { "NavBack", "Alt, Left" },
        { "NavForward", "Alt, Right" },
        { "FocusAddress", "Control, L" },
        { "FocusSearch", "Control, F" },
        { "FocusSidebar", "Control, D" },
        { "Refresh", "F5" },
        { "ShowProperties", "Alt, Return" },
        { "OpenSettings", "Control, Oemcomma" }, 
        { "TogglePin", "Control, P" },
        { "ToggleFullscreen", "F11" },
        { "CloseApp", "Alt, F4" },
        { "Copy", "Control, C" },
        { "Cut", "Control, X" },
        { "Paste", "Control, V" },
        { "Delete", "Delete" },
        { "Rename", "F2" },
        { "QuickLook", "Space" },
        { "Undo", "Control, Z" },
        { "Redo", "Control, Y" },
        { "SelectAll", "Control, A" },
        { "FocusFilePanel", "Control, Shift, D1" },
        { "FocusAI", "Control, Shift, D2" },
        { "ToggleSidebar", "Control, B" },
        { "DeletePermanent", "Shift, Delete" },
        { "EditTags", "Control, Alt, T" },
        { "NewTab", "Control, T" },
        { "NextTab", "Control, PageDown" },
        { "PrevTab", "Control, PageUp" }
    };
    
    // Future extensible
    [JsonExtensionData]
    public System.Collections.Generic.Dictionary<string, object>? ExtensionData { get; set; }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                
                // Migrate: Ensure all default hotkeys exist
                var defaults = new AppSettings().Hotkeys;
                foreach (var kvp in defaults)
                {
                    if (!settings.Hotkeys.ContainsKey(kvp.Key))
                        settings.Hotkeys[kvp.Key] = kvp.Value;
                }

                // Migrate: removed hotkeys
                settings.Hotkeys.Remove("ToggleDragBox");

                // Migrate: update old defaults if user hasn't customized
                if (settings.Hotkeys.TryGetValue("FocusFilePanel", out var focusFile) && focusFile == "Control, D1")
                    settings.Hotkeys["FocusFilePanel"] = "Control, Shift, D1";
                if (settings.Hotkeys.TryGetValue("FocusAI", out var focusAi) && focusAi == "Control, D2")
                    settings.Hotkeys["FocusAI"] = "Control, Shift, D2";
                if (settings.Hotkeys.TryGetValue("EditTags", out var editTags) && editTags == "Control, T")
                    settings.Hotkeys["EditTags"] = "Control, Alt, T";

                // Migrate: sidebar block order
                var defaultSidebarOrder = new AppSettings().SidebarBlockOrder;
                if (settings.SidebarBlockOrder == null || settings.SidebarBlockOrder.Count == 0)
                {
                    settings.SidebarBlockOrder = new System.Collections.Generic.List<string>(defaultSidebarOrder);
                }
                else
                {
                    var normalized = new System.Collections.Generic.List<string>();
                    var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var id in settings.SidebarBlockOrder)
                    {
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        if (!defaultSidebarOrder.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                        if (!seen.Add(id)) continue;
                        normalized.Add(id.ToLowerInvariant());
                    }
                    foreach (var id in defaultSidebarOrder)
                    {
                        if (seen.Add(id))
                            normalized.Add(id);
                    }
                    settings.SidebarBlockOrder = normalized;
                }

                // Migrate: ensure column width keys exist.
                var defaultFileCols = new AppSettings().FileColumnWidths;
                settings.FileColumnWidths ??= new System.Collections.Generic.Dictionary<string, int>();
                foreach (var kvp in defaultFileCols)
                {
                    if (!settings.FileColumnWidths.ContainsKey(kvp.Key) || settings.FileColumnWidths[kvp.Key] < 50)
                        settings.FileColumnWidths[kvp.Key] = kvp.Value;
                }

                var defaultDriveCols = new AppSettings().DriveColumnWidths;
                settings.DriveColumnWidths ??= new System.Collections.Generic.Dictionary<string, int>();
                foreach (var kvp in defaultDriveCols)
                {
                    if (!settings.DriveColumnWidths.ContainsKey(kvp.Key) || settings.DriveColumnWidths[kvp.Key] < 50)
                        settings.DriveColumnWidths[kvp.Key] = kvp.Value;
                }

                // Migrate: new separate default model for batch vision tasks.
                if (string.IsNullOrWhiteSpace(settings.LlmBatchVisionModelName))
                    settings.LlmBatchVisionModelName = settings.LlmModelName;
                
                return settings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
        return new AppSettings();
    }

    public static void ReloadCurrent()
    {
        _instance = Load();
    }
}
