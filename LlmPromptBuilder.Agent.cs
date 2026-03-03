using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpeedExplorer;

public static partial class LlmPromptBuilder
{
    public static string GetAgenticSystemPrompt(bool taggingEnabled, bool searchEnabled)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an autonomous file system agent that operates in a loop.");
        sb.AppendLine("You can inspect the file system and execute operations. After you output commands, the system will execute them and return the results to you in the next message.");
        sb.AppendLine();
        sb.AppendLine("Available COMMANDS:");
        sb.AppendLine("- {\"cmd\":\"list_dir\",\"path\":\"./Folder\",\"include_metadata\":true} : Lists files in a directory. Use this to gather context instead of guessing filenames.");
        if (searchEnabled)
            sb.AppendLine("- {\"cmd\":\"search\",\"root\":\"./\",\"pattern\":\"*.jpg\"} : Recursively searches for matching files.");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"search_tags\",\"tags\":[\"important\"]} : Finds files with specific tags.");
        sb.AppendLine("- {\"cmd\":\"create_folder\",\"path\":\"FolderName\"} : Creates a folder.");
        sb.AppendLine("- {\"cmd\":\"move\",\"files\":[\"file1.txt\"],\"to\":\"./Destination\"} : Moves specific files (use exact names found via list_dir). File refs can be relative paths like \"images/file1.txt\".");
        sb.AppendLine("- {\"cmd\":\"move\",\"pattern\":\"*.txt\",\"to\":\"./Destination\"} : Moves files matching a glob pattern.");
        sb.AppendLine("- For extension-based tasks (e.g. \"move all .jpeg files\"), prefer move with pattern directly (\"*.jpeg\" and/or \"*.jpg\").");
        sb.AppendLine("- For conditional split tasks (e.g. based on filename content such as Cyrillic), use list_dir then move with explicit files; avoid broad pattern moves first.");
        sb.AppendLine("- {\"cmd\":\"rename\",\"file\":\"oldname.txt\",\"newName\":\"newname.md\"} : Renames a file.");
        sb.AppendLine("- {\"cmd\":\"create_file\",\"name\":\"script.py\",\"content\":\"text\"} : Creates a new file.");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"tag\",\"files\":[\"photo.jpg\"],\"tags\":[\"nature\"]} : Adds tags to files.");
        sb.AppendLine("- Optional first-loop context policy: {\"context_policy\":{\"use_file_context\":true,\"level\":\"names|metadata|none\",\"path\":\"./\",\"refresh_each_loop\":true}}.");
        sb.AppendLine("  If use_file_context=true, the system can inject an updated file snapshot each loop so you should avoid repeating list_dir for the same path.");

        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("1. You must output STRICT JSON ONLY matching the provided schema.");
        sb.AppendLine("2. The JSON must contain 'thought', 'plan', 'is_done', and 'commands'.");
        sb.AppendLine("2a. In loop 1, decide and set 'context_policy' for this task.");
        sb.AppendLine("2b. For tasks that depend on filename content (for example Cyrillic/non-Cyrillic or other name-based routing), default to context_policy use_file_context=true with level='names' in loop 1.");
        sb.AppendLine("3. For extension-only requests (no additional conditions), do NOT search first. Create destination folder then use move with pattern in the same response.");
        sb.AppendLine("4. If the request includes additional conditions (for example filename-language/content rules), do NOT do blanket extension pattern moves first.");
        sb.AppendLine("5. In conditional tasks, first inspect with list_dir/search and then move explicit file lists to each destination.");
        sb.AppendLine("6. Use 'list_dir' or 'search' only when names or scope are ambiguous.");
        sb.AppendLine("7. Never use placeholder values like '?', '...', or empty patterns/paths.");
        sb.AppendLine("8. Use './' for current directory paths.");
        sb.AppendLine("9. Set \"is_done\": true ONLY when the user's entire request is completely satisfied.");
        sb.AppendLine("10. You can issue multiple commands in one response, but keep them safe.");
        sb.AppendLine("11. Before setting is_done=true, verify completion using current/injected file context; if verification fails, return corrective commands.");

        return sb.ToString();
    }

    public static object GetAgenticJsonSchema(bool taggingEnabled, bool searchEnabled)
    {
        var createFolderSchema = new { type = "object", properties = new { cmd = new { @const = "create_folder" }, path = new { type = "string" } }, required = new[] { "cmd", "path" }, additionalProperties = false };
        var moveSchema = new { type = "object", properties = new { cmd = new { @const = "move" }, to = new { type = "string" }, files = new { type = "array", items = new { type = "string" } }, pattern = new { type = "string" } }, required = new[] { "cmd", "to" }, additionalProperties = false };
        var renameSchema = new { type = "object", properties = new { cmd = new { @const = "rename" }, file = new { type = "string" }, newName = new { type = "string" } }, required = new[] { "cmd", "file", "newName" }, additionalProperties = false };
        var createFileSchema = new { type = "object", properties = new { cmd = new { @const = "create_file" }, name = new { type = "string" }, content = new { type = "string" } }, required = new[] { "cmd", "name" }, additionalProperties = false };
        var listDirSchema = new { type = "object", properties = new { cmd = new { @const = "list_dir" }, path = new { type = "string" }, include_metadata = new { type = "boolean" } }, required = new[] { "cmd" }, additionalProperties = false };
        var searchSchema = new { type = "object", properties = new { cmd = new { @const = "search" }, root = new { type = "string" }, pattern = new { type = "string" } }, required = new[] { "cmd", "pattern" }, additionalProperties = false };
        var searchTagsSchema = new { type = "object", properties = new { cmd = new { @const = "search_tags" }, tags = new { type = "array", items = new { type = "string" } } }, required = new[] { "cmd", "tags" }, additionalProperties = false };
        var tagSchema = new { type = "object", properties = new { cmd = new { @const = "tag" }, files = new { type = "array", items = new { type = "string" } }, tags = new { type = "array", items = new { type = "string" } } }, required = new[] { "cmd", "files", "tags" }, additionalProperties = false };

        var contextPolicySchema = new
        {
            type = "object",
            properties = new
            {
                use_file_context = new { type = "boolean" },
                level = new { type = "string", @enum = new[] { "none", "names", "metadata" } },
                path = new { type = "string" },
                refresh_each_loop = new { type = "boolean" }
            },
            required = new[] { "use_file_context", "level" },
            additionalProperties = false
        };

        var commandSchemas = new List<object> { createFolderSchema, moveSchema, renameSchema, createFileSchema, listDirSchema };
        if (searchEnabled) commandSchemas.Add(searchSchema);
        if (taggingEnabled)
        {
            commandSchemas.Add(searchTagsSchema);
            commandSchemas.Add(tagSchema);
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "agent_loop_commands",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        { "thought", new { type = "string" } },
                        { "plan", new { type = "string" } },
                        { "is_done", new { type = "boolean" } }
                    }.Concat(new Dictionary<string, object>
                    {
                        {
                            "commands", new
                            {
                                type = "array",
                                items = new
                                {
                                    oneOf = commandSchemas
                                }
                            }
                        },
                        { "context_policy", contextPolicySchema }
                    }).ToDictionary(k => k.Key, v => v.Value),
                    required = new[] { "thought", "plan", "is_done", "commands" },
                    additionalProperties = false
                }
            }
        };
    }
}

