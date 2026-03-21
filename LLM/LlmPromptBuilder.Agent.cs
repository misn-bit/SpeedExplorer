using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpeedExplorer;

public static partial class LlmPromptBuilder
{
    public static string GetAgenticSystemPrompt(bool taggingEnabled, bool searchEnabled)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an autonomous file system agent that works one loop at a time.");
        sb.AppendLine("You inspect the current folder, choose safe file commands, then the app executes them and returns feedback in the next loop.");
        sb.AppendLine();
        sb.AppendLine("Return strict JSON only.");
        sb.AppendLine("Keep 'thought' and 'plan' short and concrete.");
        sb.AppendLine("Do not guess filenames. Use tools or injected context.");
        sb.AppendLine();
        sb.AppendLine("Available commands:");
        sb.AppendLine("- {\"cmd\":\"list_dir\",\"path\":\"./Folder\",\"include_metadata\":true} : list a directory when you need names or metadata.");
        sb.AppendLine("- list_dir path may also be an absolute path like \"C:\\\\Users\" or \"D:\\\\\" if you need to inspect outside the current folder.");
        if (searchEnabled)
            sb.AppendLine("- {\"cmd\":\"search\",\"root\":\"./\",\"pattern\":\"*.jpg\"} : recursive search when scope is unclear.");
        if (searchEnabled)
            sb.AppendLine("- search root may be \"./\" or an absolute path like \"C:\\\\\" when the user asks about files elsewhere on the machine.");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"search_tags\",\"tags\":[\"important\"]} : find files by tags.");
        sb.AppendLine("- {\"cmd\":\"create_folder\",\"path\":\"FolderName\"} : create a folder.");
        sb.AppendLine("- {\"cmd\":\"move\",\"files\":[\"file1.txt\"],\"to\":\"./Destination\"} : move exact files.");
        sb.AppendLine("- {\"cmd\":\"move\",\"pattern\":\"*.txt\",\"to\":\"./Destination\"} : move files by glob pattern.");
        sb.AppendLine("- {\"cmd\":\"rename\",\"file\":\"oldname.txt\",\"newName\":\"newname.md\"} : rename one file.");
        sb.AppendLine("- {\"cmd\":\"create_file\",\"name\":\"script.py\",\"content\":\"text\"} : create a new file.");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"tag\",\"files\":[\"photo.jpg\"],\"tags\":[\"nature\"]} : add tags.");
        sb.AppendLine("- Optional context policy: {\"context_policy\":{\"use_file_context\":true,\"level\":\"names|metadata|none\",\"path\":\"./\",\"refresh_each_loop\":true}}.");
        sb.AppendLine("  If file context is injected, prefer using it instead of repeating the same list_dir.");

        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. The JSON must contain 'thought', 'plan', 'is_done', and 'commands'.");
        sb.AppendLine("2. Use './' for the current directory.");
        sb.AppendLine("3. Never use placeholders like '?', '...', or empty paths.");
        sb.AppendLine("4. If the task is simple and based only on extension, prefer one compact step: create_folder + move pattern.");
        sb.AppendLine("5. If the task has conditions based on filename content, language, or exceptions, inspect first and then move exact file lists.");
        sb.AppendLine("6. Prefer 1 to 3 commands per loop. Keep the workflow incremental.");
        sb.AppendLine("7. Set is_done=true only when the whole request is satisfied or verified by current feedback/context.");
        sb.AppendLine("8. If more work is needed, set is_done=false and provide the next commands.");
        sb.AppendLine("9. When filenames are already visible in injected context, use them directly instead of listing again.");
        sb.AppendLine("10. After successful writes, do at most one focused verification pass unless feedback shows a concrete mismatch to fix.");
        sb.AppendLine("11. If the latest verification shows the target state already matches the request, set is_done=true instead of verifying again.");
        sb.AppendLine("12. If nothing should be executed in this loop, commands may be empty only when is_done=true.");
        sb.AppendLine();
        sb.AppendLine("Preferred workflows:");
        sb.AppendLine("- Simple extension task: create destination folder, move by pattern, then verify next loop.");
        sb.AppendLine("- Conditional routing task: inspect names first, then move explicit files, then verify.");

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
