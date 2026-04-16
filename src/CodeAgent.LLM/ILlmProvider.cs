using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAgent.LLM;

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
    
    [JsonPropertyName("tool_calls")]
    public List<ToolCallItem>? ToolCalls { get; set; }
    
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }
}

public class ToolCallItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";
    
    [JsonPropertyName("function")]
    public ToolCallFunction Function { get; set; } = new();
}

public class ToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;
}

public class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";
    
    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; set; } = new();
}

public class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

public interface ILlmProvider
{
    string ProviderName { get; }
    string ModelName { get; }

    Task<ChatResponse> CompleteAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatChunk> CompleteStreamAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);
}

public class ChatResponse
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
    
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
    
    [JsonPropertyName("tool_calls")]
    public List<ToolCallItem>? ToolCalls { get; set; }
}

public class ChatChunk
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
    
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}