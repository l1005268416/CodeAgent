namespace CodeAgent.Core.Models;

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string SystemPrompt { get; set; } = "You are a helpful AI assistant.";
    public List<Message> Messages { get; set; } = new();
    public Dictionary<string, object>? Metadata { get; set; }
}