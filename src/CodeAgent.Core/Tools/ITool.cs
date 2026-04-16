using System.Text.Json;

namespace CodeAgent.Core.Tools;

public class ToolResult
{
    public bool Success { get; set; }
    public string Content { get; set; } = string.Empty;
    public object? StructuredData { get; set; }
    public string? Error { get; set; }
}

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }

    Task<ToolResult> ExecuteAsync(
        JsonElement parameters,
        CancellationToken cancellationToken = default);
}