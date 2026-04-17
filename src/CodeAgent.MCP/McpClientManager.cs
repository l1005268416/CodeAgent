using System.Text.Json;
using CodeAgent.MCP.Models;
using CodeAgent.MCP.Transports;
using Microsoft.Extensions.Logging;
using CoreTools = CodeAgent.Core.Tools;

namespace CodeAgent.MCP;

public class McpClientManager : IMcpClientManager, IDisposable
{
    private readonly Dictionary<string, IMcpClient> _clients = new();
    private readonly ILogger<McpClientManager> _logger;

    public McpClientManager(ILogger<McpClientManager> logger)
    {
        _logger = logger;
    }

    public async Task LoadConfigAsync(string configPath, CancellationToken ct = default)
    {
        if (!File.Exists(configPath))
        {
            _logger.LogWarning("MCP config file not found: {ConfigPath}", configPath);
            return;
        }

        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = JsonSerializer.Deserialize<McpConfig>(json);

        if (config?.McpServers == null) return;

        foreach (var (name, serverConfig) in config.McpServers)
        {
            if (!serverConfig.Enabled)
            {
                _logger.LogInformation("MCP server {ServerName} is disabled, skipping", name);
                continue;
            }

            try
            {
                await AddClientAsync(name, serverConfig, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add MCP server {ServerName}", name);
            }
        }
    }

    public async Task AddClientAsync(string name, McpServerConfig config, CancellationToken ct = default)
    {
        IMcpTransport transport;

        if (config.Transport == "stdio")
        {
            if (string.IsNullOrEmpty(config.Command))
            {
                throw new ArgumentException($"MCP server {name} stdio transport requires a command");
            }

            var envVars = new Dictionary<string, string>();
            foreach (var (key, value) in config.Headers)
            {
                if (value.StartsWith("${") && value.EndsWith("}"))
                {
                    var envVar = value[2..^1];
                    envVars[key] = Environment.GetEnvironmentVariable(envVar) ?? "";
                }
                else
                {
                    envVars[key] = value;
                }
            }

            transport = new StdioTransport(config.Command, config.Args, envVars);
        }
        else if (config.Transport == "sse")
        {
            if (string.IsNullOrEmpty(config.Url))
            {
                throw new ArgumentException($"MCP server {name} sse transport requires a URL");
            }

            var headers = new Dictionary<string, string>();
            foreach (var (key, value) in config.Headers)
            {
                if (value.StartsWith("${") && value.EndsWith("}"))
                {
                    var envVar = value[2..^1];
                    headers[key] = Environment.GetEnvironmentVariable(envVar) ?? "";
                }
                else
                {
                    headers[key] = value;
                }
            }

            transport = new SseTransport(config.Url, headers);
        }
        else
        {
            throw new NotSupportedException($"MCP transport {config.Transport} not supported yet");
        }

        var client = new McpClient(name, transport);
        _clients[name] = client;

        try
        {
            _logger.LogDebug("Calling ConnectAsync for server: {ServerName}", name);
            await client.ConnectAsync(ct);
            _logger.LogDebug("ConnectAsync completed for server: {ServerName}", name);
            _logger.LogInformation("MCP server {ServerName} connected", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect MCP server {ServerName}", name);
            _clients.Remove(name);
            throw;
        }
    }

    public void RemoveClient(string name)
    {
        if (_clients.TryGetValue(name, out var client))
        {
            client.Dispose();
            _clients.Remove(name);
            _logger.LogInformation("MCP server {ServerName} removed", name);
        }
    }

    public IMcpClient? GetClient(string name)
    {
        return _clients.TryGetValue(name, out var client) ? client : null;
    }

    public IReadOnlyList<IMcpClient> GetAllClients()
    {
        return _clients.Values.ToList();
    }

    public async Task<IReadOnlyList<CodeAgent.Core.Models.ToolDefinition>> GetAllToolDefinitionsAsync(CancellationToken ct = default)
    {
        var tools = new List<CodeAgent.Core.Models.ToolDefinition>();

        foreach (var client in _clients.Values)
        {
            _logger.LogDebug("Checking server {ServerName}, IsConnected: {IsConnected}", client.ServerName, client.IsConnected);
            
            if (!client.IsConnected) 
            {
                _logger.LogDebug("Skipping disconnected MCP server: {ServerName}", client.ServerName);
                continue;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(120));
                
                _logger.LogDebug("Calling ListToolsAsync for server: {ServerName}", client.ServerName);
                var mcpTools = await client.ListToolsAsync(cts.Token);
                _logger.LogInformation("Got {Count} tools from MCP server: {ServerName}", mcpTools.Count, client.ServerName);
                
                foreach (var tool in mcpTools)
                {
                    tools.Add(new CodeAgent.Core.Models.ToolDefinition
                    {
                        Name = $"{client.ServerName}/{tool.Name}",
                        Description = tool.Description,
                        InputSchema = tool.InputSchema ?? JsonSerializer.Deserialize<JsonElement>("{}")
                    });
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout getting tools from MCP server: {ServerName}", client.ServerName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from MCP server: {ServerName}", client.ServerName);
            }
        }

        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(string serverName, string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(serverName, out var client))
        {
            throw new KeyNotFoundException($"MCP server {serverName} not found");
        }

        return await client.CallToolAsync(toolName, arguments, ct);
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }
}

public interface IMcpClientManager : IDisposable
{
    Task LoadConfigAsync(string configPath, CancellationToken ct = default);
    Task AddClientAsync(string name, McpServerConfig config, CancellationToken ct = default);
    void RemoveClient(string name);
    IMcpClient? GetClient(string name);
    IReadOnlyList<IMcpClient> GetAllClients();
    Task<IReadOnlyList<CodeAgent.Core.Models.ToolDefinition>> GetAllToolDefinitionsAsync(CancellationToken ct = default);
    Task<McpToolResult> CallToolAsync(string serverName, string toolName, JsonElement arguments, CancellationToken ct = default);
}