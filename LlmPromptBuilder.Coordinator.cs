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

