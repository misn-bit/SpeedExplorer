using System;
using System.Collections.Generic;

namespace SpeedExplorer;

internal sealed class TabState
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Tab";
    public string CurrentPath { get; set; } = "";
    public string CurrentDisplayPath { get; set; } = "";
    public bool IsSearchMode { get; set; }
    public string SearchText { get; set; } = "";
    public SortColumn SortColumn { get; set; } = SortColumn.Name;
    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    public Dictionary<string, string> LastSelection { get; set; } = new();
    public Dictionary<string, (SortColumn Column, SortDirection Direction)> FolderSortSettings { get; set; } = new();
    public Stack<string> BackHistory { get; set; } = new();
    public Stack<string> ForwardHistory { get; set; } = new();
    public bool IsShellMode { get; set; }
    public string CurrentShellId { get; set; } = "";
}

