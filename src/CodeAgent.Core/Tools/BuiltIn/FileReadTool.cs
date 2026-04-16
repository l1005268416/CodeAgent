using System.Text.Json;

namespace CodeAgent.Core.Tools.BuiltIn;

public class FileReadTool : ITool
{
    public string Name => "file_read";
    public string Description => "Read content from a file";
    public JsonElement InputSchema => JsonSerializer.Deserialize<JsonElement>(@"
{
    ""type"": ""object"",
    ""properties"": {
        ""path"": {
            ""type"": ""string"",
            ""description"": ""The file path to read""
        }
    },
    ""required"": [""path""]
}");

    public async Task<ToolResult> ExecuteAsync(JsonElement parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!parameters.TryGetProperty("path", out var pathElement))
            {
                return new ToolResult { Success = false, Error = "Missing required parameter: path" };
            }

            var path = pathElement.GetString();
            if (string.IsNullOrEmpty(path))
            {
                return new ToolResult { Success = false, Error = "Path cannot be empty" };
            }

            if (!File.Exists(path))
            {
                return new ToolResult { Success = false, Error = $"File not found: {path}" };
            }

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            return new ToolResult { Success = true, Content = content };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}