using Microsoft.Extensions.Logging;
using CodeAgent.Core.Models;

namespace CodeAgent.Core.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void Register(ITool tool)
    {
        if (_tools.ContainsKey(tool.Name))
        {
            _logger.LogWarning("Tool {ToolName} already registered, overwriting", tool.Name);
        }
        _tools[tool.Name] = tool;
        _logger.LogInformation("Registered tool: {ToolName}", tool.Name);
    }

    public void Unregister(string toolName)
    {
        if (_tools.Remove(toolName))
        {
            _logger.LogInformation("Unregistered tool: {ToolName}", toolName);
        }
    }

    public ITool? Get(string toolName)
    {
        return _tools.TryGetValue(toolName, out var tool) ? tool : null;
    }

    public IReadOnlyList<ITool> GetAll()
    {
        return _tools.Values.ToList();
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinition
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();
    }
}

public interface IToolRegistry
{
    void Register(ITool tool);
    void Unregister(string toolName);
    ITool? Get(string toolName);
    IReadOnlyList<ITool> GetAll();
    IReadOnlyList<ToolDefinition> GetToolDefinitions();
}