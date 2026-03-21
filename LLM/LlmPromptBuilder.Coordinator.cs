using System.Collections.Generic;
using System.Text;

namespace SpeedExplorer;

public static partial class LlmPromptBuilder
{
    public static string GetAgentCoordinatorSystemPrompt(
        bool taggingEnabled,
        bool searchEnabled,
        string? currentDirectory,
        string? currentContext,
        bool forceReplyOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You route requests for a chat-first file assistant.");
        sb.AppendLine("Choose exactly one action: reply, quick_commands, or start_agent_run.");
        sb.AppendLine();
        sb.AppendLine("Return strict JSON matching the schema.");
        sb.AppendLine();
        sb.AppendLine("Actions:");
        sb.AppendLine("- reply: normal answer, no commands.");
        sb.AppendLine("- quick_commands: short read-only inspection only.");
        sb.AppendLine("- start_agent_run: any request that changes files, creates folders/files, renames, moves, tags, or needs multiple steps.");
        sb.AppendLine();
        sb.AppendLine("Quick command toolset (read-only):");
        sb.AppendLine("- {\"cmd\":\"list_dir\",\"path\":\"./\",\"include_metadata\":true}");
        if (searchEnabled)
            sb.AppendLine("- {\"cmd\":\"search\",\"root\":\"./\",\"pattern\":\"*.jpg\"}");
        if (taggingEnabled)
            sb.AppendLine("- {\"cmd\":\"search_tags\",\"tags\":[\"important\"]}");
        sb.AppendLine("- list_dir/search may also use absolute paths when the user asks about locations outside the current folder.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Never output write commands in quick_commands.");
        sb.AppendLine("2. If the user wants file changes, prefer start_agent_run.");
        sb.AppendLine("3. Use quick_commands only for inspection questions like listing, searching, or checking what exists.");
        sb.AppendLine("4. Set run_task to a short executable objective when action=start_agent_run.");
        sb.AppendLine("5. Keep message short and user-facing.");
        if (forceReplyOnly)
        {
            sb.AppendLine("6. FORCE_REPLY_ONLY is active: action MUST be \"reply\" and commands MUST be empty.");
            sb.AppendLine("7. If a [AGENT_RUN_REPORT] message exists in history, base your reply on the latest report only.");
            sb.AppendLine("8. In FORCE_REPLY_ONLY mode after an agent run, do not state future intent. State the result directly.");
        }
        else
        {
            sb.AppendLine("6. If uncertain between quick_commands and start_agent_run, choose start_agent_run for file-changing requests and quick_commands for inspection-only requests.");
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
                    properties = new
                    {
                        thought = new { type = "string" },
                        action = new { type = "string", @enum = new[] { "reply", "quick_commands", "start_agent_run" } },
                        message = new { type = "string" },
                        run_task = new { type = "string" },
                        commands = new
                        {
                            type = "array",
                            items = new
                            {
                                oneOf = commandSchemas
                            }
                        }
                    },
                    required = new[] { "thought", "action", "message", "commands" },
                    additionalProperties = false
                }
            }
        };
    }
}
