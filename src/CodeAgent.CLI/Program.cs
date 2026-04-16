using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using CodeAgent.Core.Agent;
using CodeAgent.Core.Context;
using CodeAgent.Core.Models;
using CodeAgent.Core.Sessions;
using CodeAgent.Core.Tools;
using CodeAgent.Core.Tools.BuiltIn;
using CodeAgent.Infrastructure.Storage;
using CodeAgent.Infrastructure.Config;
using CodeAgent.LLM;

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
        config.SetMinimumLevel(LogLevel.Information);
    });

    services.AddSingleton(configManager);
    services.AddSingleton<ISessionStore, FileSessionStore>();
    services.AddSingleton<ISessionManager, SessionManager>();
    services.AddSingleton<IToolRegistry, ToolRegistry>();
    services.AddSingleton<IContextManager, ContextManager>();

    var providerConfig = config.Llm.Providers.GetValueOrDefault(config.Llm.DefaultProvider) 
        ?? new LlmProviderConfig 
        { 
            BaseUrl = "http://localhost:11434/v1", 
            Model = "llama3:8b" 
        };

    services.AddSingleton<ILlmProvider>(sp =>
    {
        var httpClient = new HttpClient();
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
    private readonly ILogger<CliApp> _logger;
    private Session? _currentSession;

    public CliApp(
        ISessionManager sessionManager,
        IToolRegistry toolRegistry,
        IAgentOrchestrator agentOrchestrator,
        IContextManager contextManager,
        ILogger<CliApp> logger)
    {
        _sessionManager = sessionManager;
        _toolRegistry = toolRegistry;
        _agentOrchestrator = agentOrchestrator;
        _contextManager = contextManager;
        _logger = logger;

        RegisterBuiltInTools();
    }

    private void RegisterBuiltInTools()
    {
        _toolRegistry.Register(new FileReadTool());
        _toolRegistry.Register(new FileWriteTool());
        _toolRegistry.Register(new ShellExecTool());
    }

    public async Task RunAsync(string[] args)
    {
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
        var prompt = string.Join(" ", args);
        _currentSession = await _sessionManager.CreateAsync("Command Session");
        
        AnsiConsole.MarkupLine("[bold cyan]CodeAgent CLI[/] - Single Command Mode");
        AnsiConsole.MarkupLine($"[dim]Prompt: {prompt}[/]\n");

        try
        {
            var response = await _agentOrchestrator.ProcessAsync(prompt, _currentSession);
            Console.WriteLine(response);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    private async Task RunRepl()
    {
        _currentSession = await _sessionManager.CreateAsync("Interactive Session");

        AnsiConsole.MarkupLine("[bold cyan]CodeAgent CLI[/] - Interactive Mode");
        AnsiConsole.MarkupLine("[dim]Type /help for available commands[/]\n");

        while (true)
        {
            var input = AnsiConsole.Ask<string>("[green]>[/] ").Trim();

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