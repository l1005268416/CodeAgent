using Microsoft.Extensions.Logging;
using CodeAgent.Core.Models;
using System.Text.Json;

namespace CodeAgent.Core.Context;

public interface IContextManager
{
    List<Message> BuildMessages(Session session, string userInput);
    int EstimateTokenCount(string text);
    void TrimContext(List<Message> messages, int maxTokens);
}

public class ContextManager : IContextManager
{
    private readonly ILogger<ContextManager> _logger;
    private readonly int _maxTokens;
    private readonly int _reserveTokens;

    public ContextManager(ILogger<ContextManager> logger, int maxTokens = 128000, int reserveTokens = 4096)
    {
        _logger = logger;
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
                Role = MessageRole.System,
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
                Role = MessageRole.User,
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
                if (messages[i].Role == MessageRole.User)
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
}