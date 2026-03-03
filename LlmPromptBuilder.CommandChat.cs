using System.Text;

namespace SpeedExplorer;

public static partial class LlmPromptBuilder
{
    public static string GetSystemPrompt(bool taggingEnabled, bool searchEnabled, bool hasFullContext, bool thinkingEnabled)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a file organizer assistant.");
        sb.AppendLine("Return STRICT JSON only with a 'commands' array.");
        if (thinkingEnabled)
            sb.AppendLine("You may include an optional 'thought' field for short reasoning.");

        sb.AppendLine();
        sb.AppendLine("Available commands:");
        sb.AppendLine("- {\"cmd\":\"create_folder\",\"path\":\"FolderName\"} : Creates a folder.");
        sb.AppendLine("- {\"cmd\":\"move\",\"files\":[\"file1.txt\",\"file2.jpg\"],\"to\":\"./Destination\"} : Moves specific files.");
        if (!hasFullContext)
            sb.AppendLine("- {\"cmd\":\"move\",\"pattern\":\"*.jpg\",\"to\":\"./Images\"} : Moves files matching a glob pattern.");
        sb.AppendLine("- {\"cmd\":\"rename\",\"file\":\"oldname.txt\",\"newName\":\"newname.md\"} : Renames a single file.");
        sb.AppendLine("- {\"cmd\":\"create_file\",\"name\":\"script.py\",\"content\":\"print('hello')\"} : Creates a new file.");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"tag\",\"files\":[\"photo.jpg\"],\"tags\":[\"nature\",\"sunset\"]} : Adds tags.");

        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Destination paths can be relative or absolute.");
        sb.AppendLine("2. Files can only be moved from the current directory context.");
        sb.AppendLine("3. If the same file appears in multiple commands, only the last action is applied.");
        sb.AppendLine("4. No markdown and no prose outside JSON.");
        sb.AppendLine("5. No file overwrite; conflicts are auto-renamed.");
        sb.AppendLine("6. Return the complete command plan in one response.");

        return sb.ToString();
    }

    public static string GetChatSystemPrompt(
        bool taggingEnabled,
        bool searchEnabled,
        bool hasFullContext,
        bool thinkingEnabled,
        string? currentContext = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful conversational file assistant.");
        sb.AppendLine("Return JSON with shape: {\"message\":\"...\",\"commands\":[...]}.");
        sb.AppendLine("'commands' can be empty when no file actions are needed.");
        if (thinkingEnabled)
            sb.AppendLine("You may include an optional 'thought' field.");

        if (!string.IsNullOrEmpty(currentContext))
        {
            sb.AppendLine();
            sb.AppendLine("CURRENT DIRECTORY CONTEXT:");
            sb.AppendLine(currentContext);
        }

        sb.AppendLine();
        sb.AppendLine("Command set:");
        sb.AppendLine("- create_folder(path)");
        sb.AppendLine("- move(files,to) and move(pattern,to)");
        sb.AppendLine("- rename(file,newName)");
        sb.AppendLine("- create_file(name,content)");
        if (taggingEnabled)
            sb.AppendLine("- tag(files,tags)");

        sb.AppendLine();
        sb.AppendLine("Guidance:");
        sb.AppendLine("- For pure Q&A, keep commands empty and answer in message.");
        sb.AppendLine("- For actions, include only valid commands and then explain briefly in message.");
        sb.AppendLine("- Keep message concise and user-facing.");

        return sb.ToString();
    }
}

