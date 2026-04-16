using System.Text.Json;
using CodeAgent.MCP.Models;
using CodeAgent.MCP.Transports;
using Microsoft.Extensions.Logging;

namespace CodeAgent.MCP;

public class McpClient : IMcpClient, IDisposable
{
    private readonly IMcpTransport _transport;
    private readonly string _serverName;
    private readonly ILogger<McpClient> _logger;
    private List<McpTool>? _cachedTools;
    private List<McpResource>? _cachedResources;
    private List<McpPrompt>? _cachedPrompts;
    private bool _initialized;
    private bool _transportConnected;

    public string ServerName => _serverName;
    public bool IsConnected => _transportConnected && _transport.IsConnected;

    public McpClient(string serverName, IMcpTransport transport, ILogger<McpClient>? logger = null)
    {
        _serverName = serverName;
        _transport = transport;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<McpClient>.Instance;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _transport.ConnectAsync(ct);
        _transportConnected = true;
        _logger.LogDebug("Transport connected for {ServerName}", _serverName);
        
        var initRequest = JsonSerializer.SerializeToElement(new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "CodeAgent.CLI", version = "1.0.0" }
        });
        
        try
        {
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(10));
            
            _logger.LogDebug("Sending initialize request for {ServerName}", _serverName);
            var response = await _transport.SendRequestAsync<JsonElement>("initialize", initRequest, handshakeCts.Token);
            _logger.LogDebug("Initialize response received for {ServerName}: {Response}", _serverName, response.ToString());
            
            _initialized = true;
            _logger.LogInformation("MCP handshake completed for server: {ServerName}", _serverName);
            
            var notification = JsonSerializer.SerializeToElement(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });
            await _transport.SendNotificationAsync("notifications/initialized", notification, handshakeCts.Token);
            _logger.LogInformation("MCP initialized notification sent for server: {ServerName}", _serverName);
        }
        catch (Exception ex)
        {
            _initialized = false;
            _logger.LogWarning(ex, "MCP handshake failed for server: {ServerName}, will attempt to use anyway", _serverName);
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _transport.DisconnectAsync(ct);
        _logger.LogInformation("Disconnected from MCP server: {ServerName}", _serverName);
    }

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        if (_cachedTools != null) return _cachedTools;

        _logger.LogDebug("ListToolsAsync called for {ServerName}", _serverName);
        var emptyParams =  JsonDocument.Parse("{}").RootElement;
        var response = await _transport.SendRequestAsync<JsonElement>("tools/list", emptyParams, ct);
        _logger.LogDebug("ListToolsAsync response received for {ServerName}", _serverName);
        
        var tools = new List<McpTool>();
        if (response.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var tool in toolsArray.EnumerateArray())
            {
                tools.Add(new McpTool
                {
                    Name = tool.GetProperty("name").GetString() ?? "",
                    Description = tool.GetProperty("description").GetString() ?? "",
                    InputSchema = tool.TryGetProperty("inputSchema", out var schema) ? schema.Clone() : null
                });
            }
        }

        _cachedTools = tools;
        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        var requestParams = JsonSerializer.SerializeToElement(new
        {
            name = toolName,
            arguments = arguments
        });

        var response = await _transport.SendRequestAsync<JsonElement>("tools/call", requestParams, ct);

        var result = new McpToolResult();
        if (response.TryGetProperty("content", out var content))
        {
            result.Content = new List<McpContentItem>();
            foreach (var item in content.EnumerateArray())
            {
                result.Content.Add(new McpContentItem
                {
                    Type = item.GetProperty("type").GetString() ?? "text",
                    Text = item.TryGetProperty("text", out var text) ? text.GetString() : null
                });
            }
        }

        if (response.TryGetProperty("isError", out var isError))
        {
            result.IsError = isError.GetBoolean();
        }

        return result;
    }

    public async Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default)
    {
        if (_cachedResources != null) return _cachedResources;

        var response = await _transport.SendRequestAsync<JsonElement>("resources/list", null, ct);

        var resources = new List<McpResource>();
        if (response.TryGetProperty("resources", out var resourcesArray))
        {
            foreach (var resource in resourcesArray.EnumerateArray())
            {
                resources.Add(new McpResource
                {
                    Uri = resource.GetProperty("uri").GetString() ?? "",
                    Name = resource.GetProperty("name").GetString() ?? "",
                    Description = resource.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    MimeType = resource.TryGetProperty("mimeType", out var mime) ? mime.GetString() : null
                });
            }
        }

        _cachedResources = resources;
        return resources;
    }

    public async Task<IReadOnlyList<McpPrompt>> ListPromptsAsync(CancellationToken ct = default)
    {
        if (_cachedPrompts != null) return _cachedPrompts;

        var response = await _transport.SendRequestAsync<JsonElement>("prompts/list", null, ct);

        var prompts = new List<McpPrompt>();
        if (response.TryGetProperty("prompts", out var promptsArray))
        {
            foreach (var prompt in promptsArray.EnumerateArray())
            {
                var mcpPrompt = new McpPrompt
                {
                    Name = prompt.GetProperty("name").GetString() ?? "",
                    Description = prompt.TryGetProperty("description", out var desc) ? desc.GetString() : null
                };

                if (prompt.TryGetProperty("arguments", out var argsArray))
                {
                    mcpPrompt.Arguments = new List<McpPromptArgument>();
                    foreach (var arg in argsArray.EnumerateArray())
                    {
                        mcpPrompt.Arguments.Add(new McpPromptArgument
                        {
                            Name = arg.GetProperty("name").GetString() ?? "",
                            Description = arg.TryGetProperty("description", out var argDesc) ? argDesc.GetString() : null,
                            Required = arg.TryGetProperty("required", out var required) && required.GetBoolean()
                        });
                    }
                }

                prompts.Add(mcpPrompt);
            }
        }

        _cachedPrompts = prompts;
        return prompts;
    }

    public void Dispose()
    {
        if (_transport is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

public interface IMcpClient : IDisposable
{
    string ServerName { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);

    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default);
    Task<McpToolResult> CallToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default);

    Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<McpPrompt>> ListPromptsAsync(CancellationToken ct = default);
}