using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using CodeAgent.Core.Agent;
using CodeAgent.Core.Context;
using CodeAgent.Core.Models;
using CodeAgent.Core.Sessions;
using CodeAgent.Core.Skills;
using CodeAgent.Core.Tools;
using CodeAgent.Core.Tools.BuiltIn;
using CodeAgent.Infrastructure.Storage;
using CodeAgent.Infrastructure.Config;
using CodeAgent.LLM;
using CodeAgent.MCP;
using CodeAgent.CLI;

var builder = new ServiceCollection();
var configManager = new ConfigManager();
var config = await configManager.LoadAsync();

ConfigureServices(builder, config);
var services = builder.BuildServiceProvider();

var cliApp = services.GetRequiredService<ICliApp>();
await cliApp.RunAsync(args);

void ConfigureServices(IServiceCollection services, AppConfig config)
{
    services.AddLogging(config =>
    {
        config.AddConsole();
        config.SetMinimumLevel(LogLevel.Warning);
    });

    services.AddSingleton(configManager);
    services.AddSingleton<ISessionStore, FileSessionStore>();
    services.AddSingleton<ISessionManager, SessionManager>();
    services.AddSingleton<IToolRegistry, ToolRegistry>();
    services.AddSingleton<IContextManager, ContextManager>();
    services.AddSingleton<SkillLoader>();
    services.AddSingleton<ISkillEngine>(sp =>
    {
        var loader = sp.GetRequiredService<SkillLoader>();
        var registry = sp.GetRequiredService<IToolRegistry>();
        var logger = sp.GetRequiredService<ILogger<SkillEngine>>();
        return new SkillEngine(loader, registry, logger);
    });
    services.AddSingleton<IMcpClientManager, McpClientManager>();

    var providerConfig = config.Llm.Providers.GetValueOrDefault(config.Llm.DefaultProvider) 
        ?? new LlmProviderConfig 
        { 
            BaseUrl = "http://localhost:11434/v1", 
            Model = "llama3:8b" 
        };

    services.AddSingleton<ILlmProvider>(sp =>
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (!string.IsNullOrEmpty(providerConfig.ApiKey) && providerConfig.ApiKey != "ollama")
        {
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", providerConfig.ApiKey);
        }
        return new OpenAICompatibleProvider(httpClient, providerConfig.BaseUrl, providerConfig.Model);
    });

    services.AddSingleton<IAgentOrchestrator>(sp =>
    {
        var llmProvider = sp.GetRequiredService<ILlmProvider>();
        var toolRegistry = sp.GetRequiredService<IToolRegistry>();
        var contextManager = sp.GetRequiredService<IContextManager>();
        var sessionManager = sp.GetRequiredService<ISessionManager>();
        var logger = sp.GetRequiredService<ILogger<AgentOrchestrator>>();
        return new AgentOrchestrator(llmProvider, toolRegistry, contextManager, sessionManager, logger);
    });

    services.AddSingleton<ICliApp, CliApp>();
}

public interface ICliApp
{
    Task RunAsync(string[] args);
}

public class CliApp : ICliApp
{
    private readonly ISessionManager _sessionManager;
    private readonly IToolRegistry _toolRegistry;
    private readonly IAgentOrchestrator _agentOrchestrator;
    private readonly IContextManager _contextManager;
    private readonly ISkillEngine _skillEngine;
    private readonly IMcpClientManager _mcpClientManager;
    private readonly ILogger<CliApp> _logger;
    private Session? _currentSession;

    public CliApp(
        ISessionManager sessionManager,
        IToolRegistry toolRegistry,
        IAgentOrchestrator agentOrchestrator,
        IContextManager contextManager,
        ISkillEngine skillEngine,
        IMcpClientManager mcpClientManager,
        ILogger<CliApp> logger)
    {
        _sessionManager = sessionManager;
        _toolRegistry = toolRegistry;
        _agentOrchestrator = agentOrchestrator;
        _contextManager = contextManager;
        _skillEngine = skillEngine;
        _mcpClientManager = mcpClientManager;
        _logger = logger;
        _agentOrchestrator.OnLogMessage += _agentOrchestrator_OnLogMessage;
        RegisterBuiltInTools();
    }

   

    private async Task InitializeAsync()
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var mcpConfigPath = Path.Combine(homeDir, ".codeagent", "mcp.json");
            
            if (File.Exists(mcpConfigPath))
            {
                await _mcpClientManager.LoadConfigAsync(mcpConfigPath);
                
                var mcpTools = await _mcpClientManager.GetAllToolDefinitionsAsync();
                foreach (var tool in mcpTools)
                {
_toolRegistry.Register(new McpToolAdapter(tool, _mcpClientManager));
                }
                _logger.LogInformation("Loaded {Count} MCP tools", mcpTools.Count);
            }
            
            await _skillEngine.LoadSkillsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize MCP or Skills");
        }
    }

    private void RegisterBuiltInTools()
    {
        _toolRegistry.Register(new FileReadTool());
        _toolRegistry.Register(new FileWriteTool());
        _toolRegistry.Register(new ShellExecTool());
    }

    public async Task RunAsync(string[] args)
    {
        await InitializeAsync();
        
        if (args.Length > 0)
        {
            await RunSingleCommand(args);
        }
        else
        {
            await RunRepl();
        }
    }

    private async Task RunSingleCommand(string[] args)
    {
        var input = string.Join(" ", args);
        
        if (input.StartsWith("/"))
        {
            await HandleSlashCommand(input);
            return;
        }

        _currentSession = await _sessionManager.CreateAsync("Command Session");
        
        AnsiConsole.MarkupLine("[bold cyan]CodeAgent CLI[/] - Single Command Mode");
        AnsiConsole.MarkupLine($"[dim]Prompt: {input}[/]\n");

        try
        {
            var response = await _agentOrchestrator.ProcessAsync(input, _currentSession);
            Console.WriteLine(response);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }
    private void _agentOrchestrator_OnLogMessage(int flg, string e)
    {
        if(flg==0)
            AnsiConsole.Write(new Panel(e).Header("[yellow]工具调用[/]"));
        else
            AnsiConsole.MarkupLine(e);
    }
    private async Task RunRepl()
    {
        _currentSession = await _sessionManager.CreateAsync("Interactive Session");

        AnsiConsole.MarkupLine("[bold cyan]CodeAgent CLI[/] - Interactive Mode");
        AnsiConsole.MarkupLine("[dim]Type /help for available commands[/]\n");

        while (true)
        {
            AnsiConsole.Markup("[green]>[/] ");
            var input = Console.ReadLine().Trim();
            //var input = AnsiConsole.Ask<string>("[green]>[/] ").Trim();

            if (string.IsNullOrEmpty(input))
                continue;
            if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[yellow]Goodbye![/]");
                break;
            }
            else if(input.StartsWith("/"))
            {
                await HandleSlashCommand(input);
            }
            else
            {
                await HandleUserMessage(input);
            }
        }
    }

    private async Task HandleSlashCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        switch (command)
        {
            case "/help":
                ShowHelp();
                break;
            case "/clear":
                _currentSession?.Messages.Clear();
                await _sessionManager.SaveAsync(_currentSession!);
                AnsiConsole.MarkupLine("[green]Context cleared[/]");
                break;
            case "/session":
                await HandleSessionCommand(parts);
                break;
            case "/tools":
                ListTools();
                break;
            case "/model":
                AnsiConsole.MarkupLine("[yellow]Model switching not implemented in MVP[/]");
                break;
            case "/context":
                ShowContextStats();
                break;
            case "/skill":
                await HandleSkillCommand(parts);
                break;
            case "/mcp":
                await HandleMcpCommand(parts);
                break;
            default:
                AnsiConsole.MarkupLine($"[red]Unknown command: {command}[/]");
                break;
        }
    }

    private async Task HandleSessionCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            AnsiConsole.MarkupLine("[yellow]Usage: /session [list|new|resume|delete][/]");
            return;
        }

        var subCommand = parts[1].ToLowerInvariant();

        switch (subCommand)
        {
            case "list":
                var sessions = await _sessionManager.ListAsync();
                var table = new Table().AddColumn("ID").AddColumn("Name").AddColumn("Created").AddColumn("Updated");
                foreach (var s in sessions)
                {
                    table.AddRow(s.Id[..8], s.Name, s.CreatedAt.ToString("yyyy-MM-dd HH:mm"), s.UpdatedAt.ToString("yyyy-MM-dd HH:mm"));
                }
                AnsiConsole.Write(table);
                break;
            case "new":
                _currentSession = await _sessionManager.CreateAsync();
                AnsiConsole.MarkupLine($"[green]Created new session: {_currentSession.Id[..8]}[/]");
                break;
            case "resume":
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /session resume <id>[/]");
                    return;
                }
                try
                {
                    _currentSession = await _sessionManager.ResumeAsync(parts[2]);
                    AnsiConsole.MarkupLine($"[green]Resumed session: {_currentSession.Id[..8]}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
                }
                break;
            case "delete":
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /session delete <id>[/]");
                    return;
                }
                await _sessionManager.DeleteAsync(parts[2]);
                AnsiConsole.MarkupLine($"[green]Deleted session: {parts[2][..Math.Min(8, parts[2].Length)]}[/]");
                break;
        }
    }

    private void ListTools()
    {
        var tools = _toolRegistry.GetAll();
        var table = new Table().AddColumn("Name").AddColumn("Description");
        foreach (var tool in tools)
        {
            table.AddRow(tool.Name, tool.Description);
        }
        AnsiConsole.Write(table);
    }

    private void ShowContextStats()
    {
        if (_currentSession == null) return;
        
        var totalTokens = _currentSession.Messages.Sum(m => _contextManager.EstimateTokenCount(m.Content));
        AnsiConsole.MarkupLine($"[cyan]Messages: {_currentSession.Messages.Count}[/]");
        AnsiConsole.MarkupLine($"[cyan]Estimated Tokens: {totalTokens}[/]");
    }

    private async Task HandleSkillCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            await ListSkills();
            return;
        }

        var subCommand = parts[1].ToLowerInvariant();

        switch (subCommand)
        {
            case "list":
                await ListSkills();
                break;
            case "use":
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine("[yellow]Usage: /skill use <name>[/]");
                    return;
                }
                await UseSkill(parts[2], parts.Length > 3 ? parts[3] : null);
                break;
            default:
                AnsiConsole.MarkupLine($"[yellow]Unknown skill command: {subCommand}[/]");
                break;
        }
    }

    private async Task ListSkills()
    {
        await _skillEngine.LoadSkillsAsync();
        var skills = _skillEngine.GetAllSkills();
        
        if (skills.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No skills found. Skills should be placed in ~/.codeagent/skills/[/]");
            return;
        }

        var table = new Table().AddColumn("Name").AddColumn("Description").AddColumn("Tags");
        foreach (var skill in skills)
        {
            table.AddRow(skill.Name, skill.Description, string.Join(", ", skill.Tags));
        }
        AnsiConsole.Write(table);
    }

    private async Task UseSkill(string skillName, string? filePath)
    {
        try
        {
            var context = await _skillEngine.PrepareExecutionAsync(skillName);
            
            if (!string.IsNullOrEmpty(filePath))
            {
                context.Parameters["file_path"] = filePath;
                if (File.Exists(filePath))
                {
                    context.Variables["code_content"] = await File.ReadAllTextAsync(filePath);
                }
            }

            var prompt = _skillEngine.RenderPrompt(context);
            var systemPrompt = _skillEngine.GetSystemPrompt(context);

            _currentSession!.SystemPrompt = systemPrompt;
            _currentSession.Messages.Add(new Message { Role = CodeAgent.Core.Models.MessageRole.User, Content = prompt });

            AnsiConsole.MarkupLine($"[cyan]Executing skill: {skillName}[/]");
            var response = await _agentOrchestrator.ProcessAsync(prompt, _currentSession);
            Console.WriteLine(response);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    private async Task HandleMcpCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            await ListMcpServers();
            return;
        }

        var subCommand = parts[1].ToLowerInvariant();

        switch (subCommand)
        {
            case "list":
                await ListMcpServers();
                break;
            case "reload":
                await ReloadMcpServers();
                break;
            default:
                AnsiConsole.MarkupLine($"[yellow]Unknown MCP command: {subCommand}[/]");
                break;
        }
    }

    private async Task ListMcpServers()
    {
        var clients = _mcpClientManager.GetAllClients();
        
        if (clients.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No MCP servers configured. Add them to ~/.codeagent/mcp.json[/]");
            return;
        }

        var table = new Table().AddColumn("Server").AddColumn("Status").AddColumn("Tools");
        foreach (var client in clients)
        {
            var status = client.IsConnected ? "[green]Connected[/]" : "[red]Disconnected[/]";
            var tools = "N/A";
            
            if (client.IsConnected)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var toolList = await client.ListToolsAsync(cts.Token);
                    tools = toolList.Count.ToString();
                }
                catch (OperationCanceledException)
                {
                    tools = "[yellow]Timeout[/]";
                }
                catch
                {
                    tools = "[red]Error[/]";
                }
            }
            
            table.AddRow(client.ServerName, status, tools);
        }
        AnsiConsole.Write(table);
    }

    private async Task ReloadMcpServers()
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var mcpConfigPath = Path.Combine(homeDir, ".codeagent", "mcp.json");
            
            await _mcpClientManager.LoadConfigAsync(mcpConfigPath);
            AnsiConsole.MarkupLine("[green]MCP servers reloaded[/]");
            
            var mcpTools = await _mcpClientManager.GetAllToolDefinitionsAsync();
            foreach (var tool in mcpTools)
            {
                _toolRegistry.Register(new McpToolAdapter(tool, _mcpClientManager));
            }
            AnsiConsole.MarkupLine($"[green]Registered {mcpTools.Count} MCP tools[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error reloading MCP servers: {ex.Message}[/]");
        }
    }

    private void ShowHelp()
    {
        var panel = new Panel(@"
[bold]Available Commands:[/]

[cyan]/help[/]           - Show this help
[cyan]/clear[/]          - Clear current context
[cyan]/session list[/]   - List all sessions
[cyan]/session new[/]    - Create new session
[cyan]/session resume[/] - Resume a session
[cyan]/session delete[/] - Delete a session
[cyan]/tools[/]          - List available tools
[cyan]/context stats[/]  - Show context statistics
[cyan]/exit[/]           - Exit the program

[dim]Just type your message to chat with the agent[/]")
            .Header("CodeAgent CLI Help");
        AnsiConsole.Write(panel);
    }

    private async Task HandleUserMessage(string message)
    {
        if (_currentSession == null)
        {
            _currentSession = await _sessionManager.CreateAsync();
        }

        try
        {
            AnsiConsole.MarkupLine("[dim]Thinking...[/]");
            var response = await _agentOrchestrator.ProcessAsync(message, _currentSession);
            Console.WriteLine(response);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }
}