using System.Text.Json;
using Microsoft.Extensions.Logging;
using CodeAgent.Core.Models;
using CodeAgent.Core.Sessions;

namespace CodeAgent.Infrastructure.Storage;

public class FileSessionStore : ISessionStore
{
    private readonly string _sessionsPath;
    private readonly ILogger<FileSessionStore> _logger;

    public FileSessionStore(ILogger<FileSessionStore> logger)
    {
        _logger = logger;
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _sessionsPath = Path.Combine(homeDir, ".codeagent", "sessions");
        Directory.CreateDirectory(_sessionsPath);
    }

    public async Task SaveAsync(Session session)
    {
        var filePath = Path.Combine(_sessionsPath, $"{session.Id}.json");
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
        _logger.LogDebug("Saved session to {FilePath}", filePath);
    }

    public async Task<Session?> LoadAsync(string sessionId)
    {
        var filePath = Path.Combine(_sessionsPath, $"{sessionId}.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<Session>(json);
    }

    public async Task<IReadOnlyList<Session>> ListAllAsync()
    {
        var sessions = new List<Session>();
        var files = Directory.GetFiles(_sessionsPath, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var session = JsonSerializer.Deserialize<Session>(json);
                if (session != null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load session from {File}", file);
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public Task DeleteAsync(string sessionId)
    {
        var filePath = Path.Combine(_sessionsPath, $"{sessionId}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }
}