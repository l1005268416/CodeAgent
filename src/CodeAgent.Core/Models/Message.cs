namespace CodeAgent.Core.Models;

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int TokenCount { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}