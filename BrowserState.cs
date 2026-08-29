using System.Collections.Generic;

namespace SpeedExplorer;

/// <summary>
/// Mutable application state for the main file-browser surface.
///
/// This type intentionally contains no controls, services, or persistence logic.
/// It is the state that navigation, tabs, search, and list rendering coordinate
/// around; the MainForm remains responsible for presenting it for now.
/// </summary>
internal sealed class BrowserState
{
    public string CurrentPath { get; set; } = "";
    public string CurrentDisplayPath { get; set; } = "";
    public bool IsShellMode { get; set; }

    public List<FileItem> Items { get; set; } = new();
    public List<FileItem> AllItems { get; set; } = new();

    public SortColumn SortColumn { get; set; } = SortColumn.Name;
    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    public bool TaggedFilesOnTop { get; set; }

    public HashSet<string> CutPaths { get; } = new();
}
