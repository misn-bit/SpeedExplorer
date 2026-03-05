namespace SpeedExplorer;

public class ManualContextAction
{
    public string Name { get; set; } = "New Action";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
    public string AppliesTo { get; set; } = "Both"; // Files, Folders, Both
    public string Extensions { get; set; } = ""; // comma-separated, like ".zip,.rar"
    public bool AllowMultiple { get; set; } = true;
    public string WorkingDir { get; set; } = "";
    public bool VisibleInShell { get; set; } = true;
}
