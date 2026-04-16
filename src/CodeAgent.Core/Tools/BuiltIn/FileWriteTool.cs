using System.Text.Json;

namespace CodeAgent.Core.Tools.BuiltIn;

public class FileWriteTool : ITool
{
    public string Name => "file_write";
    public string Description => "Write content to a file";
    public JsonElement InputSchema => JsonSerializer.Deserialize<JsonElement>(@"
{
    ""type"": ""object"",
    ""properties"": {
        ""path"": {
            ""type"": ""string"",
            ""description"": ""The file path to write""
        },
        ""content"": {
            ""type"": ""string"",
            ""description"": ""The content to write""
        }
    },
    ""required"": [""path"", ""content""]
}");

    public async Task<ToolResult> ExecuteAsync(JsonElement parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            string? path = null;
            string? content = null;

            if (parameters.TryGetProperty("path", out var pathElement))
                path = pathElement.GetString();
            if (parameters.TryGetProperty("content", out var contentElement))
                content = contentElement.GetString();

            if (string.IsNullOrEmpty(path))
                return new ToolResult { Success = false, Error = "Missing required parameter: path" };
            if (content == null)
                return new ToolResult { Success = false, Error = "Missing required parameter: content" };

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, content, cancellationToken);
            return new ToolResult { Success = true, Content = $"Successfully wrote to {path}" };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}