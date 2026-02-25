using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace SpeedExplorer;

/// <summary>
/// Builds system prompts, JSON schemas, and directory context for LLM requests.
/// Extracted from LlmService for separation of concerns.
/// </summary>
public static class LlmPromptBuilder
{
    public static string GetSystemPrompt(bool taggingEnabled, bool searchEnabled, bool hasFullContext, bool thinkingEnabled)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a file organizer assistant. Output only a valid JSON object.");
        
        if (thinkingEnabled)
            sb.AppendLine("You must include a \"thought\" field first to explain your reasoning, followed by a \"commands\" array.");
        else
            sb.AppendLine("Output a JSON object with a \"commands\" array.");

        sb.AppendLine("Available commands:");
        sb.AppendLine("- {\"cmd\":\"create_folder\",\"path\":\"FolderName\"} : Creates a folder. Use relative path (./Folder) or absolute (D:/Folder).");
        sb.AppendLine("- {\"cmd\":\"move\",\"files\":[\"file1.txt\",\"file2.jpg\"],\"to\":\"./Destination\"} : Moves specific files to a folder.");
        
        if (hasFullContext)
            sb.AppendLine("  You have the full file list - use exact filenames in the 'files' array.");
        else
            sb.AppendLine("- {\"cmd\":\"move\",\"pattern\":\"*.jpg\",\"to\":\"./Images\"} : Moves files matching a glob pattern.");
            
        sb.AppendLine("- {\"cmd\":\"rename\",\"file\":\"oldname.txt\",\"newName\":\"newname.md\"} : Renames a single file (extension changes allowed).");
        sb.AppendLine("- {\"cmd\":\"create_file\",\"name\":\"script.py\",\"content\":\"print('hello')\"} : Creates a new file with any extension.");
        
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"tag\",\"files\":[\"photo.jpg\"],\"tags\":[\"nature\",\"sunset\"]} : Adds tags to specified files.");

        sb.AppendLine("\nRules:");
        sb.AppendLine("1. Destination paths can be relative (./Folder, Folder) or absolute (D:/Archive). Create folders first if they don't exist.");
        sb.AppendLine("2. Files can only be moved from the current directory, but to anywhere.");
        sb.AppendLine("3. If the same file appears in multiple commands, only the last action will be executed.");
        
        if (thinkingEnabled)
             sb.AppendLine("4. Output only valid JSON. Start with a 'thought' field to explain your plan, then the 'commands' array.");
        else
             sb.AppendLine("4. Output only valid JSON. No markdown, no explanations outside the JSON structure.");
             
        sb.AppendLine("5. No file will be overwritten - if a name conflict occurs, the system auto-renames.");
        sb.AppendLine("6. You must write the whole sequence of commands in one go.");

        return sb.ToString();
    }

    public static string GetChatSystemPrompt(bool taggingEnabled, bool searchEnabled, bool hasFullContext, bool thinkingEnabled, string? currentContext = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful, conversational file system assistant.");
        sb.AppendLine("You can both chat with the user and perform file operations.");
        
        if (!string.IsNullOrEmpty(currentContext))
        {
            sb.AppendLine("\nCURRENT DIRECTORY CONTENT:");
            sb.AppendLine(currentContext);
            sb.AppendLine("\n(Use the list above for exact filenames in your commands.)");
        }

        sb.AppendLine("\nWhen you need to perform actions, you must include a JSON object in your response.");
        sb.AppendLine("The JSON should have a \"thought\" field and a \"commands\" array.");
        
        sb.AppendLine("\nAvailable commands:");
        sb.AppendLine("- {\"cmd\":\"create_folder\",\"path\":\"FolderName\"}");
        sb.AppendLine("- {\"cmd\":\"move\",\"files\":[\"file1.txt\"],\"to\":\"./Target\"}");
        if (!hasFullContext)
            sb.AppendLine("- {\"cmd\":\"move\",\"pattern\":\"*.jpg\",\"to\":\"./Target\"}");
        sb.AppendLine("- {\"cmd\":\"rename\",\"file\":\"old.txt\",\"newName\":\"new.txt\"}");
        sb.AppendLine("- {\"cmd\":\"create_file\",\"name\":\"file.txt\",\"content\":\"text\"}");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"tag\",\"files\":[\"f.jpg\"],\"tags\":[\"t1\"]}");

        sb.AppendLine("\nRules for Operations:");
        sb.AppendLine("1. Paths can be relative (./Folder) or absolute (C:/Files).");
        sb.AppendLine("2. Files are moved from the current directory.");
        sb.AppendLine("3. No overwriting - conflicts are auto-renamed.");
        
        sb.AppendLine("\nYou are encouraged to be helpful and explain what you are doing. If you are just answering a question without actions, you can skip the JSON or provide an empty 'commands' list.");
        
        return sb.ToString();
    }

    /// <summary>
    /// JSON Schema for structured output enforcement.
    /// </summary>
    public static object GetJsonSchema(bool taggingEnabled, bool searchEnabled, bool hasFullContext, bool thinkingEnabled)
    {
        var createFolderSchema = new
        {
            type = "object",
            properties = new { 
                cmd = new { @const = "create_folder" },
                path = new { type = "string" }
            },
            required = new[] { "cmd", "path" },
            additionalProperties = false
        };

        var moveSchema = new
        {
            type = "object",
            properties = new { 
                cmd = new { @const = "move" },
                to = new { type = "string" },
                files = new { type = "array", items = new { type = "string" } },
                pattern = new { type = "string" }
            },
            required = new[] { "cmd", "to" },
            additionalProperties = false
        };

        var renameSchema = new
        {
            type = "object",
            properties = new { 
                cmd = new { @const = "rename" },
                file = new { type = "string" },
                newName = new { type = "string" }
            },
            required = new[] { "cmd", "file", "newName" },
            additionalProperties = false
        };

        var createFileSchema = new
        {
            type = "object",
            properties = new { 
                cmd = new { @const = "create_file" },
                name = new { type = "string" },
                content = new { type = "string" }
            },
            required = new[] { "cmd", "name" },
            additionalProperties = false
        };

        var commandSchemas = new List<object> { createFolderSchema, moveSchema, renameSchema, createFileSchema };

        if (taggingEnabled)
        {
            commandSchemas.Add(new
            {
                type = "object",
                properties = new { 
                    cmd = new { @const = "tag" },
                    files = new { type = "array", items = new { type = "string" } },
                    tags = new { type = "array", items = new { type = "string" } }
                },
                required = new[] { "cmd", "files", "tags" },
                additionalProperties = false
            });
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "file_commands",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        { "thought", new { type = "string" } },
                    }.Concat(new Dictionary<string, object>
                    {
                        { "commands", new
                            {
                                type = "array",
                                items = new
                                {
                                    oneOf = commandSchemas
                                }
                            }
                        }
                    }).ToDictionary(k => k.Key, v => v.Value),
                    
                    required = thinkingEnabled ? new[] { "thought", "commands" } : new[] { "commands" },
                    additionalProperties = false
                }
            }
        };
    }

    public static object GetTaggingJsonSchema()
    {
        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "image_tags",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        tags = new { type = "array", items = new { type = "string" } }
                    },
                    required = new[] { "tags" },
                    additionalProperties = false
                }
            }
        };
    }

    public static string GetAgenticSystemPrompt(bool taggingEnabled, bool searchEnabled)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an autonomous file system agent that operates in a loop.");
        sb.AppendLine("You can inspect the file system and execute operations. After you output commands, the system will execute them and return the results to you in the next message.");
        sb.AppendLine("\nAvailable COMMANDS:");
        sb.AppendLine("- {\"cmd\":\"list_dir\",\"path\":\"./Folder\",\"include_metadata\":true} : Lists files in a directory. Use this to gather context instead of guessing filenames.");
        if (searchEnabled)
        {
            sb.AppendLine("- {\"cmd\":\"search\",\"root\":\"./\",\"pattern\":\"*.jpg\"} : Recursively searches for matching files.");
        }
        if (taggingEnabled)
        {
            sb.AppendLine("- {\"cmd\":\"search_tags\",\"tags\":[\"important\"]} : Finds files with specific tags.");
        }
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

        sb.AppendLine("\nRULES:");
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
                        { "is_done", new { type = "boolean" } },
                    }.Concat(new Dictionary<string, object>
                    {
                        { "commands", new
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

    public static string GetAgentCoordinatorSystemPrompt(
        bool taggingEnabled,
        bool searchEnabled,
        string? currentDirectory,
        string? currentContext,
        bool forceReplyOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the orchestration brain for a chat-first file assistant.");
        sb.AppendLine("Decide whether to reply directly, run quick read-only tools, or start the autonomous agent loop.");
        sb.AppendLine();
        sb.AppendLine("You MUST return strict JSON matching the schema.");
        sb.AppendLine();
        sb.AppendLine("Actions:");
        sb.AppendLine("- reply: answer normally with no commands.");
        sb.AppendLine("- quick_commands: issue short read-only tool calls, then the app will run them and ask you for a final reply.");
        sb.AppendLine("- start_agent_run: start the autonomous loop for multi-step or write-heavy tasks.");
        sb.AppendLine();
        sb.AppendLine("Quick command toolset (read-only):");
        sb.AppendLine("- {\"cmd\":\"list_dir\",\"path\":\"./\",\"include_metadata\":true}");
        if (searchEnabled)
            sb.AppendLine("- {\"cmd\":\"search\",\"root\":\"./\",\"pattern\":\"*.jpg\"}");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"search_tags\",\"tags\":[\"important\"]}");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Never output write commands in quick_commands.");
        sb.AppendLine("2. Use start_agent_run for tasks that move/rename/create files or need multiple corrective passes.");
        sb.AppendLine("3. Set run_task to a concise executable task objective when action=start_agent_run.");
        sb.AppendLine("4. Keep message user-facing and concise.");
        if (forceReplyOnly)
        {
            sb.AppendLine("5. FORCE_REPLY_ONLY is active: action MUST be \"reply\" and commands MUST be empty.");
            sb.AppendLine("6. If a [AGENT_RUN_REPORT] message exists in history, base your reply on the latest report only.");
            sb.AppendLine("7. In FORCE_REPLY_ONLY mode after an agent run, do not state future intent (no \"I'll ...\"). State completed/failed status directly.");
        }

        sb.AppendLine();
        sb.AppendLine($"Current directory: {currentDirectory ?? "(unknown)"}");
        if (!string.IsNullOrWhiteSpace(currentContext))
        {
            sb.AppendLine("Current directory context:");
            sb.AppendLine(currentContext);
        }

        return sb.ToString().TrimEnd();
    }

    public static object GetAgentCoordinatorJsonSchema(bool taggingEnabled, bool searchEnabled)
    {
        var listDirSchema = new
        {
            type = "object",
            properties = new
            {
                cmd = new { @const = "list_dir" },
                path = new { type = "string" },
                include_metadata = new { type = "boolean" }
            },
            required = new[] { "cmd" },
            additionalProperties = false
        };

        var commandSchemas = new List<object> { listDirSchema };

        if (searchEnabled)
        {
            commandSchemas.Add(new
            {
                type = "object",
                properties = new
                {
                    cmd = new { @const = "search" },
                    root = new { type = "string" },
                    pattern = new { type = "string" }
                },
                required = new[] { "cmd", "pattern" },
                additionalProperties = false
            });
        }

        if (taggingEnabled)
        {
            commandSchemas.Add(new
            {
                type = "object",
                properties = new
                {
                    cmd = new { @const = "search_tags" },
                    tags = new { type = "array", items = new { type = "string" } }
                },
                required = new[] { "cmd", "tags" },
                additionalProperties = false
            });
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "agent_chat_decision",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        { "thought", new { type = "string" } },
                        { "action", new { type = "string", @enum = new[] { "reply", "quick_commands", "start_agent_run" } } },
                        { "message", new { type = "string" } },
                        { "run_task", new { type = "string" } },
                        {
                            "commands", new
                            {
                                type = "array",
                                items = new
                                {
                                    oneOf = commandSchemas
                                }
                            }
                        }
                    },
                    required = new[] { "thought", "action", "message", "commands" },
                    additionalProperties = false
                }
            }
        };
    }

    /// <summary>
    /// Builds a compact summary of file extensions and counts in the directory.
    /// </summary>
    public static string BuildExtensionContext(string directory)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
            var extGroups = files
                .Select(f => Path.GetExtension(f).ToLowerInvariant())
                .Where(e => !string.IsNullOrEmpty(e))
                .GroupBy(e => e)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            if (extGroups.Count == 0)
                return "(no files with extensions)";

            return string.Join(", ", extGroups);
        }
        catch
        {
            return "(unable to scan directory)";
        }
    }

    /// <summary>
    /// Builds a detailed list of all files in the directory.
    /// </summary>
    public static string BuildFullDirectoryContext(string directory)
    {
        try
        {
            var items = new DirectoryInfo(directory).GetFileSystemInfos("*", SearchOption.TopDirectoryOnly);
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                var type = (item.Attributes & FileAttributes.Directory) != 0 ? "[DIR]" : "[FILE]";
                sb.AppendLine($"{type} {item.Name}");
            }
            return sb.Length > 0 ? sb.ToString() : "(empty directory)";
        }
        catch (Exception ex)
        {
            return $"(error reading directory: {ex.Message})";
        }
    }
}
