using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAgent.Infrastructure.Config;

public class AppConfig
{
    public LlmConfig Llm { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
    public ToolsConfig Tools { get; set; } = new();
    public SessionConfig Session { get; set; } = new();
}

public class LlmConfig
{
    public string DefaultProvider { get; set; } = "ollama";
    public Dictionary<string, LlmProviderConfig> Providers { get; set; } = new();
}

public class LlmProviderConfig
{
    public string Type { get; set; } = "openai_compatible";
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "llama3:8b";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
}

public class ContextConfig
{
    public int MaxTokens { get; set; } = 128000;
    public string TruncationStrategy { get; set; } = "truncate_oldest";
    public int ReserveTokens { get; set; } = 4096;
}

public class ToolsConfig
{
    public int MaxIterations { get; set; } = 10;
    public int DefaultTimeout { get; set; } = 30;
    public Dictionary<string, string> Permissions { get; set; } = new();
}

public class SessionConfig
{
    public int AutoSaveInterval { get; set; } = 60;
    public int IdleTimeout { get; set; } = 1800;
    public int MaxSessions { get; set; } = 100;
}

public class ConfigManager
{
    private readonly string _configPath;
    private AppConfig? _config;

    public ConfigManager(string? configPath = null)
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configPath = configPath ?? Path.Combine(homeDir, ".codeagent", "config.yaml");
    }

    public async Task<AppConfig> LoadAsync()
    {
        if (_config != null) return _config;

        if (File.Exists(_configPath))
        {
            try
            {
                var yaml = await File.ReadAllTextAsync(_configPath);
                var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                    .Build();
                _config = deserializer.Deserialize<AppConfig>(yaml);
            }
            catch
            {
                _config = CreateDefaultConfig();
            }
        }
        else
        {
            _config = CreateDefaultConfig();
            await SaveAsync(_config);
        }

        return _config;
    }

    public async Task SaveAsync(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var serializer = new YamlDotNet.Serialization.SerializerBuilder()
            .Build();
        var yaml = serializer.Serialize(config);
        await File.WriteAllTextAsync(_configPath, yaml);
    }

    private AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            Llm = new LlmConfig
            {
                DefaultProvider = "ollama",
                Providers = new Dictionary<string, LlmProviderConfig>
                {
                    ["ollama"] = new LlmProviderConfig
                    {
                        Type = "openai_compatible",
                        BaseUrl = "http://localhost:11434/v1",
                        ApiKey = "ollama",
                        Model = "llama3:8b"
                    }
                }
            }
        };
    }
}