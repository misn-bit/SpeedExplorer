using System.Collections.Generic;

namespace SpeedExplorer;

public class FolderSettings
{
    public Dictionary<string, FolderSortState> Settings { get; set; } = new();
}

public class FolderSortState
{
    public SortColumn Column { get; set; }
    public SortDirection Direction { get; set; }
}
