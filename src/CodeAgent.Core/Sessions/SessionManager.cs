using Microsoft.Extensions.Logging;
using CodeAgent.Core.Models;

namespace CodeAgent.Core.Sessions;

public interface ISessionManager
{
    Task<Session> CreateAsync(string? name = null);
    Task<Session?> GetAsync(string sessionId);
    Task<IReadOnlyList<Session>> ListAsync();
    Task SaveAsync(Session session);
    Task DeleteAsync(string sessionId);
    Task<Session> ResumeAsync(string sessionId);
}

public class SessionManager : ISessionManager
{
    private readonly ISessionStore _store;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ISessionStore store, ILogger<SessionManager> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<Session> CreateAsync(string? name = null)
    {
        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            Name = name ?? $"Session {DateTime.Now:yyyy-MM-dd HH:mm}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _store.SaveAsync(session);
        _logger.LogInformation("Created new session: {SessionId} - {SessionName}", session.Id, session.Name);
        return session;
    }

    public async Task<Session?> GetAsync(string sessionId)
    {
        return await _store.LoadAsync(sessionId);
    }

    public async Task<IReadOnlyList<Session>> ListAsync()
    {
        return await _store.ListAllAsync();
    }

    public async Task SaveAsync(Session session)
    {
        session.UpdatedAt = DateTime.UtcNow;
        await _store.SaveAsync(session);
    }

    public async Task DeleteAsync(string sessionId)
    {
        await _store.DeleteAsync(sessionId);
        _logger.LogInformation("Deleted session: {SessionId}", sessionId);
    }

    public async Task<Session> ResumeAsync(string sessionId)
    {
        var session = await _store.LoadAsync(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session not found: {sessionId}");
        }
        return session;
    }
}

public interface ISessionStore
{
    Task SaveAsync(Session session);
    Task<Session?> LoadAsync(string sessionId);
    Task<IReadOnlyList<Session>> ListAllAsync();
    Task DeleteAsync(string sessionId);
}