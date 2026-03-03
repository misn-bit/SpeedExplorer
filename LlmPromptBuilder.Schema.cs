using System.Collections.Generic;
using System.Linq;

namespace SpeedExplorer;

public static partial class LlmPromptBuilder
{
    public static object GetJsonSchema(bool taggingEnabled, bool searchEnabled, bool hasFullContext, bool thinkingEnabled)
    {
        var commandSchemas = BuildWriteCommandSchemas(taggingEnabled);

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
                    required = new[] { "commands" },
                    additionalProperties = false
                }
            }
        };
    }

    public static object GetChatResponseJsonSchema(bool taggingEnabled)
    {
        var commandSchemas = BuildWriteCommandSchemas(taggingEnabled);
        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "chat_message_with_commands",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        thought = new { type = "string" },
                        message = new { type = "string" },
                        commands = new
                        {
                            type = "array",
                            items = new
                            {
                                oneOf = commandSchemas
                            }
                        }
                    },
                    required = new[] { "message", "commands" },
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

    private static List<object> BuildWriteCommandSchemas(bool taggingEnabled)
    {
        var createFolderSchema = new
        {
            type = "object",
            properties = new
            {
                cmd = new { @const = "create_folder" },
                path = new { type = "string" }
            },
            required = new[] { "cmd", "path" },
            additionalProperties = false
        };

        var moveSchema = new
        {
            type = "object",
            properties = new
            {
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
            properties = new
            {
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
            properties = new
            {
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
                properties = new
                {
                    cmd = new { @const = "tag" },
                    files = new { type = "array", items = new { type = "string" } },
                    tags = new { type = "array", items = new { type = "string" } }
                },
                required = new[] { "cmd", "files", "tags" },
                additionalProperties = false
            });
        }

        return commandSchemas.ToList();
    }
}

