namespace Facet.Core;

public sealed class AgentChatReference
{
    public string ThreadId { get; set; } = "";
    public string HostId { get; set; } = "local";
    public string? ProjectId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

// One-time move into the chat plugin. Delete once no such records remain (ADR 0084).
public static class LegacyChat
{
    public const string Plugin = "facet.chat", Widget = "plugin." + Plugin + ".chat";

    // Writes the plugin's store format; what the store already holds wins per chat.
    public static string Merge(string? stored, IEnumerable<AgentChatReference> chats)
    {
        var root = string.IsNullOrWhiteSpace(stored) ? new System.Text.Json.Nodes.JsonObject { ["SchemaVersion"] = 1 }
            : System.Text.Json.Nodes.JsonNode.Parse(stored) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidDataException("채팅 저장소 형식이 올바르지 않습니다.");
        var existing = System.Text.Json.JsonSerializer.Deserialize<List<AgentChatReference>>(root["Chats"]?.ToJsonString() ?? "[]") ?? throw new InvalidDataException("채팅 저장소 형식이 올바르지 않습니다.");
        var merged = existing.Concat(chats.Where(chat => existing.All(item => item.ThreadId != chat.ThreadId))).Take(40).ToList();
        root["Chats"] = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(merged));
        return root.ToJsonString();
    }
}
