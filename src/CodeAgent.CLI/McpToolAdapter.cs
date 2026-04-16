using System.Text.Json;
using CodeAgent.Core.Tools;
using CodeAgent.MCP;
using CodeAgent.MCP.Models;

namespace CodeAgent.CLI;

public class McpToolAdapter : ITool
{
    private readonly string _serverName;
    private readonly string _toolName;
    private readonly string _description;
    private readonly JsonElement _inputSchema;
    private readonly IMcpClientManager _mcpClientManager;

    public string Name => $"{_serverName}/{_toolName}";
    public string Description => _description;
    public JsonElement InputSchema => _inputSchema;

    public McpToolAdapter(CodeAgent.Core.Models.ToolDefinition toolDef, IMcpClientManager mcpClientManager)
    {
        var parts = toolDef.Name.Split('/');
        _serverName = parts.Length > 0 ? parts[0] : "";
        _toolName = parts.Length > 1 ? parts[1] : toolDef.Name;
        _description = toolDef.Description;
        _inputSchema = toolDef.InputSchema;
        _mcpClientManager = mcpClientManager;
    }

    public McpToolAdapter(string serverName, string toolName, string description, JsonElement inputSchema, IMcpClientManager mcpClientManager)
    {
        _serverName = serverName;
        _toolName = toolName;
        _description = description;
        _inputSchema = inputSchema;
        _mcpClientManager = mcpClientManager;
    }

    public async Task<ToolResult> ExecuteAsync(JsonElement parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _mcpClientManager.CallToolAsync(_serverName, _toolName, parameters, cancellationToken);
            
            if (result.IsError)
            {
                return new ToolResult 
                { 
                    Success = false, 
                    Content = result.Content?.FirstOrDefault()?.Text ?? "Unknown error" 
                };
            }

            var content = result.Content?.Select(c => c.Text).FirstOrDefault() ?? "Tool executed successfully";
            return new ToolResult { Success = true, Content = content };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}