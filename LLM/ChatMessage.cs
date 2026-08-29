using System;
using System.Text.Json.Serialization;

namespace SpeedExplorer;

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
