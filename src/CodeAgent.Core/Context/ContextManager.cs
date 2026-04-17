using Microsoft.Extensions.Logging;
using CodeAgent.Core.Models;
using CodeAgent.LLM;
using System.Text.Json;
using System.Text;

namespace CodeAgent.Core.Context;

public interface IContextManager
{
    List<Message> BuildMessages(Session session, string userInput);
    int EstimateTokenCount(string text);
    void TrimContext(List<Message> messages, int maxTokens);
    Task<List<Message>> SummarizeAndCompressAsync(
        List<Message> messages,
        int maxTokens,
        int keepLatest,
        CancellationToken cancellationToken = default);
}

public class ContextManager : IContextManager
{
    private readonly ILogger<ContextManager> _logger;
    private readonly int _maxTokens;
    private readonly int _reserveTokens;
    private readonly ILlmProvider? _llmProvider;

    public ContextManager(
        ILogger<ContextManager> logger,
        ILlmProvider? llmProvider = null,
        int maxTokens = 128000,
        int reserveTokens = 4096)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _maxTokens = maxTokens;
        _reserveTokens = reserveTokens;
    }

    public List<Message> BuildMessages(Session session, string userInput)
    {
        var messages = new List<Message>();

        if (!string.IsNullOrEmpty(session.SystemPrompt))
        {
            messages.Add(new Message
            {
                Role = Core.Models.MessageRole.System,
                Content = session.SystemPrompt
            });
        }

        foreach (var msg in session.Messages)
        {
            messages.Add(msg);
        }

        if (!string.IsNullOrEmpty(userInput))
        {
            messages.Add(new Message
            {
                Role = Core.Models.MessageRole.User,
                Content = userInput
            });
        }

        return messages;
    }

    public int EstimateTokenCount(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    public void TrimContext(List<Message> messages, int maxTokens)
    {
        var availableTokens = maxTokens - _reserveTokens;
        int currentTokens = messages.Sum(m => EstimateTokenCount(m.Content));

        while (currentTokens > availableTokens && messages.Count > 2)
        {
            int removeIndex = 1;
            for (int i = 1; i < messages.Count - 1; i++)
            {
                if (messages[i].Role == Core.Models.MessageRole.User)
                {
                    removeIndex = i;
                    break;
                }
            }

            var removed = messages[removeIndex];
            currentTokens -= EstimateTokenCount(removed.Content);
            messages.RemoveAt(removeIndex);
            _logger.LogDebug("Trimmed message: {MessageId}", removed.Id);
        }
    }

    public async Task<List<Message>> SummarizeAndCompressAsync(
        List<Message> messages,
        int maxTokens,
        int keepLatest,
        CancellationToken cancellationToken = default)
    {
        var availableTokens = maxTokens - _reserveTokens;
        int currentTokens = messages.Sum(m => EstimateTokenCount(m.Content));

        if (currentTokens <= availableTokens || messages.Count <= 2 || _llmProvider == null)
        {
            return messages;
        }

        var systemMessage = messages.FirstOrDefault(m => m.Role == Core.Models.MessageRole.System);
        var latestMessages = messages.TakeLast(keepLatest).ToList();
        var historyMessages = messages
            .Where(m => m.Role != Core.Models.MessageRole.System)
            .Except(latestMessages)
            .ToList();

        if (!historyMessages.Any())
        {
            return messages;
        }

        var summaryPrompt = BuildSummaryPrompt(historyMessages);
        _logger.LogInformation("Summarizing {Count} messages", historyMessages.Count);

        var summaryMessages = new List<ChatMessage>
        {
            new ChatMessage
            {
                Role = "system",
                Content = "You are a context summarizer. Summarize the conversation history into a concise summary. " +
                         "Focus on key facts, decisions, and important context. Keep it under 500 tokens."
            },
            new ChatMessage
            {
                Role = "user",
                Content = summaryPrompt
            }
        };

        var summaryResponse = await _llmProvider.CompleteAsync(summaryMessages, null, cancellationToken);
        var summary = summaryResponse.Content.Trim();

        var newMessages = new List<Message>();
        if (systemMessage != null)
        {
            newMessages.Add(systemMessage);
        }

        newMessages.Add(new Message
        {
            Role = Core.Models.MessageRole.System,
            Content = $"[Previous conversation summary ({historyMessages.Count} messages condensed)]: {summary}",
            Metadata = new Dictionary<string, object> { { "IsSummary", true } }
        });

        newMessages.AddRange(latestMessages);

        var compressedTokens = newMessages.Sum(m => EstimateTokenCount(m.Content));
        if (compressedTokens > availableTokens)
        {
            _logger.LogWarning("Summary still too large, falling back to trim");
            TrimContext(newMessages, maxTokens);
        }
        else
        {
            _logger.LogInformation("Compressed from ~{Old} to ~{New} tokens",
                currentTokens, compressedTokens);
        }

        return newMessages;
    }

    private string BuildSummaryPrompt(List<Message> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Please summarize the following conversation history:");
        sb.AppendLine();

        foreach (var msg in messages)
        {
            var role = msg.Role.ToString().ToLower();
            var content = msg.Content.Length > 1000
                ? msg.Content[..1000] + "..."
                : msg.Content;

            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine($"[{role}]: {content}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}