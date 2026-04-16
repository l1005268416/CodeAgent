using System.Text.Json;
using Microsoft.Extensions.Logging;
using CodeAgent.Core.Models;
using CodeAgent.Core.Tools;
using CodeAgent.Core.Context;
using CodeAgent.Core.Sessions;

namespace CodeAgent.Core.Agent;

public interface IAgentOrchestrator
{
    IAsyncEnumerable<string> ProcessStreamAsync(
        string userMessage,
        Session session,
        CancellationToken cancellationToken = default);

    Task<string> ProcessAsync(
        string userMessage,
        Session session,
        CancellationToken cancellationToken = default);
}

public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly CodeAgent.LLM.ILlmProvider _llmProvider;
    private readonly IToolRegistry _toolRegistry;
    private readonly IContextManager _contextManager;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly int _maxIterations;

    public AgentOrchestrator(
        CodeAgent.LLM.ILlmProvider llmProvider,
        IToolRegistry toolRegistry,
        IContextManager contextManager,
        ISessionManager sessionManager,
        ILogger<AgentOrchestrator> logger,
        int maxIterations = 30)
    {
        _llmProvider = llmProvider;
        _toolRegistry = toolRegistry;
        _contextManager = contextManager;
        _sessionManager = sessionManager;
        _logger = logger;
        _maxIterations = maxIterations;
    }

    private List<CodeAgent.LLM.ChatMessage> ConvertToChatMessages(List<Message> messages)
    {
        return messages.Select(m => new CodeAgent.LLM.ChatMessage
        {
            Role = m.Role switch
            {
                MessageRole.System => "system",
                MessageRole.User => "user",
                MessageRole.Assistant => "assistant",
                MessageRole.Tool => "tool",
                _ => "user"
            },
            Content = m.Content,
            ToolCalls = m.ToolCalls?.Select(tc => new CodeAgent.LLM.ToolCallItem
            {
                Id = tc.Id,
                Type = "function",
                Function = new CodeAgent.LLM.ToolCallFunction
                {
                    Name = tc.Name,
                    Arguments = tc.Arguments != null ? JsonSerializer.Serialize(tc.Arguments) : "{}"
                }
            }).ToList(),
            ToolCallId = m.ToolCallId
        }).ToList();
    }

    private List<CodeAgent.LLM.ToolDefinition> ConvertToToolDefinitions(IReadOnlyList<ToolDefinition> tools)
    {
        return tools.Select(t => new CodeAgent.LLM.ToolDefinition
        {
            Type = "function",
            Function = new CodeAgent.LLM.FunctionDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.InputSchema
            }
        }).ToList();
    }

    public async Task<string> ProcessAsync(
        string userMessage,
        Session session,
        CancellationToken cancellationToken = default)
    {
        session.Messages.Add(new Message
        {
            Role = MessageRole.User,
            Content = userMessage
        });

        var tools = ConvertToToolDefinitions(_toolRegistry.GetToolDefinitions());
        var response = await CallLlmAsync(session.Messages, tools, cancellationToken);

        var assistantMessage = new Message
        {
            Role = MessageRole.Assistant,
            Content = response.Content
        };

        if (response.ToolCalls != null && response.ToolCalls.Count > 0)
        {
            assistantMessage.ToolCalls = response.ToolCalls.Select(tc => new ToolCall
            {
                Id = tc.Id,
                Name = tc.Function.Name,
                Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Function.Arguments ?? "{}")
            }).ToList();
        }
        session.Messages.Add(assistantMessage);

        var iterations = 0;
        while (response.ToolCalls != null && response.ToolCalls.Count > 0 && iterations < _maxIterations)
        {
            iterations++;
            _logger.LogInformation("Tool call iteration {Iteration}/{Max}", iterations, _maxIterations);

            await ExecuteToolCallsAsync(session, response.ToolCalls, cancellationToken);

            var toolMessages = ConvertToChatMessages(session.Messages);
            response = await CallLlmAsync(session.Messages, tools, cancellationToken);

            var finalAssistant = new Message
            {
                Role = MessageRole.Assistant,
                Content = response.Content
            };

            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                finalAssistant.ToolCalls = response.ToolCalls.Select(tc => new ToolCall
                {
                    Id = tc.Id,
                    Name = tc.Function.Name,
                    Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Function.Arguments ?? "{}")
                }).ToList();
            }
            session.Messages.Add(finalAssistant);
        }

        await _sessionManager.SaveAsync(session);
        
        var lastAssistant = session.Messages.LastOrDefault(m => m.Role == MessageRole.Assistant);
        return lastAssistant?.Content ?? response.Content;
    }

    private async Task<CodeAgent.LLM.ChatResponse> CallLlmAsync(
        List<Message> messages,
        List<CodeAgent.LLM.ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var chatMessages = ConvertToChatMessages(messages);
        return await _llmProvider.CompleteAsync(chatMessages, tools, cancellationToken);
    }

    private async Task ExecuteToolCallsAsync(
        Session session,
        List<CodeAgent.LLM.ToolCallItem> toolCalls,
        CancellationToken cancellationToken)
    {
        foreach (var toolCall in toolCalls)
        {
            var toolName = toolCall.Function.Name;
            _logger.LogInformation("Executing tool: {ToolName}", toolName);

            var tool = _toolRegistry.Get(toolName);
            if (tool == null)
            {
                _logger.LogWarning("Tool not found: {ToolName}", toolName);
                session.Messages.Add(new Message
                {
                    Role = MessageRole.Tool,
                    ToolCallId = toolCall.Id,
                    Content = $"Error: Tool '{toolName}' not found"
                });
                continue;
            }

            try
            {
                JsonElement parameters;
                var args = toolCall.Function.Arguments ?? "{}";
                if (!string.IsNullOrEmpty(args) && args != "{}")
                {
                    parameters = JsonSerializer.Deserialize<JsonElement>(args);
                }
                else
                {
                    parameters = JsonSerializer.Deserialize<JsonElement>("{}");
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                var result = await tool.ExecuteAsync(parameters, cts.Token);

                var toolMessageContent = result.Success 
                    ? result.Content 
                    : (result.Error ?? "Unknown error");
                
                session.Messages.Add(new Message
                {
                    Role = MessageRole.Tool,
                    ToolCallId = toolCall.Id,
                    Content = toolMessageContent
                });

                _logger.LogInformation("Tool {ToolName} executed, success: {Success}", toolName, result.Success);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Tool {ToolName} execution timed out", toolName);
                session.Messages.Add(new Message
                {
                    Role = MessageRole.Tool,
                    ToolCallId = toolCall.Id,
                    Content = $"Error: Tool execution timed out"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool: {ToolName}", toolName);
                session.Messages.Add(new Message
                {
                    Role = MessageRole.Tool,
                    ToolCallId = toolCall.Id,
                    Content = $"Error: {ex.Message}"
                });
            }
        }
    }

    public async IAsyncEnumerable<string> ProcessStreamAsync(
        string userMessage,
        Session session,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        session.Messages.Add(new Message
        {
            Role = MessageRole.User,
            Content = userMessage
        });

        var messages = _contextManager.BuildMessages(session, userMessage);
        var chatMessages = ConvertToChatMessages(messages);
        var tools = ConvertToToolDefinitions(_toolRegistry.GetToolDefinitions());

        await foreach (var chunk in _llmProvider.CompleteStreamAsync(chatMessages, tools, cancellationToken))
        {
            yield return chunk.Content;
        }

        await _sessionManager.SaveAsync(session);
    }
}