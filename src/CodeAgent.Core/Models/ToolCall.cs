using System.Text.Json;

namespace CodeAgent.Core.Models;

public class ToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Arguments { get; set; }
    public JsonElement? InputJson { get; set; }
}

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonElement InputSchema { get; set; }
}