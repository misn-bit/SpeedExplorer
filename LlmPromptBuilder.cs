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
