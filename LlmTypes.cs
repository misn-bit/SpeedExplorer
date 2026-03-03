using System.Collections.Generic;
using System.Linq;

namespace SpeedExplorer;

public struct LlmImageStats
{
    public string Path;
    public int OrigW;
    public int OrigH;
    public int NewW;
    public int NewH;
    public long Bytes;
}

public enum LlmTaskKind
{
    Text,
    Vision
}

public enum LlmUsageKind
{
    Assistant,
    Batch
}

public sealed class LlmModelInfo
{
    public string Id { get; set; } = "";
    public bool IsLoaded { get; set; }
    public bool IsVision { get; set; }
    public List<string> LoadedInstanceIds { get; set; } = new();
}

public sealed class LlmModelCatalog
{
    public List<LlmModelInfo> AvailableModels { get; set; } = new();
    public List<LlmModelInfo> LoadedModels => AvailableModels.Where(m => m.IsLoaded).ToList();
}

public sealed class LlmImageTextBlock
{
    public string Text { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float FontSize { get; set; }
}

public sealed class LlmImageTextResult
{
    public string FullText { get; set; } = "";
    public string DetectedLanguage { get; set; } = "";
    public List<LlmImageTextBlock> Blocks { get; set; } = new();
}

public sealed class LlmTextTranslationResult
{
    public string TranslatedFullText { get; set; } = "";
    public List<string> Translations { get; set; } = new();
    public string TargetLanguage { get; set; } = "";
}

public sealed class LlmAgentChatDecision
{
    public string Thought { get; set; } = "";
    public string Action { get; set; } = "reply";
    public string Message { get; set; } = "";
    public string RunTask { get; set; } = "";
    public List<LlmCommand> Commands { get; set; } = new();
}

public sealed class LlmAgentRunReport
{
    public string Request { get; set; } = "";
    public string Model { get; set; } = "";
    public int LoopsUsed { get; set; }
    public int MaxLoops { get; set; }
    public bool Completed { get; set; }
    public bool ClosureVerificationRan { get; set; }
    public int CommandsExecuted { get; set; }
    public int ReadOnlyCommandsExecuted { get; set; }
    public int WriteCommandsExecuted { get; set; }
    public int UndoOperationRecords { get; set; }
    public string StopReason { get; set; } = "";
    public List<string> Events { get; set; } = new();
    public string ModelSummary { get; set; } = "";
    public string FinishedUtc { get; set; } = "";
}

public class LlmAgentResponse
{
    public string? Thought { get; set; }
    public string? Plan { get; set; }
    public bool IsDone { get; set; }
    public LlmAgentContextPolicy? ContextPolicy { get; set; }
    public List<LlmCommand> Commands { get; set; } = new();
}

public class LlmAgentContextPolicy
{
    public bool UseFileContext { get; set; }
    public string Level { get; set; } = "none";
    public string? Path { get; set; } = "./";
    public bool RefreshEachLoop { get; set; } = true;
}

public class LlmCommand
{
    public string Cmd { get; set; } = "";
    public string? Name { get; set; }       // For create_file name
    public string? Path { get; set; }       // For create_folder path (absolute or relative)
    public string? Root { get; set; }       // For search root director
    public string? Pattern { get; set; }    // For move by pattern
    public string? To { get; set; }         // Destination for move
    public string? File { get; set; }       // Single file for rename
    public string? NewName { get; set; }    // New name for rename
    public string? Content { get; set; }    // Content for create_file
    public bool IncludeMetadata { get; set; } // For list_dir
    public List<string>? Tags { get; set; } // Tags for tag command
    public List<string>? Files { get; set; } // Files to move or tag
}
