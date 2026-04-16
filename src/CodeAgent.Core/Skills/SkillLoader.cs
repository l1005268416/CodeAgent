using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using Microsoft.Extensions.Logging;

namespace CodeAgent.Core.Skills;

public class SkillLoader
{
    private readonly ILogger<SkillLoader> _logger;
    private readonly string _skillsDirectory;

    public SkillLoader(ILogger<SkillLoader> logger, string? skillsDirectory = null)
    {
        _logger = logger;
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _skillsDirectory = skillsDirectory ?? Path.Combine(homeDir, ".codeagent", "skills");
    }

    public async Task<IReadOnlyList<SkillDefinition>> LoadAllAsync(CancellationToken ct = default)
    {
        var skills = new List<SkillDefinition>();

        if (!Directory.Exists(_skillsDirectory))
        {
            Directory.CreateDirectory(_skillsDirectory);
            _logger.LogInformation("Created skills directory: {Directory}", _skillsDirectory);
            return skills;
        }

        var yamlFiles = Directory.GetFiles(_skillsDirectory, "*.yaml");
        _logger.LogInformation("Found {Count} skill files", yamlFiles.Length);

        foreach (var file in yamlFiles)
        {
            try
            {
                var skill = await LoadAsync(file, ct);
                if (skill != null)
                {
                    skills.Add(skill);
                    _logger.LogInformation("Loaded skill: {SkillName} from {File}", skill.Name, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load skill from {File}", file);
            }
        }

        return skills;
    }

    public async Task<SkillDefinition?> LoadAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Skill file not found: {FilePath}", filePath);
            return null;
        }

        var yaml = await File.ReadAllTextAsync(filePath, ct);
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        var skill = deserializer.Deserialize<SkillDefinition>(yaml);
        
        if (string.IsNullOrEmpty(skill.Name))
        {
            skill.Name = Path.GetFileNameWithoutExtension(filePath);
        }

        return skill;
    }

    public async Task SaveAsync(SkillDefinition skill, CancellationToken ct = default)
    {
        var fileName = $"{skill.Name.ToLowerInvariant().Replace(" ", "-")}.yaml";
        var filePath = Path.Combine(_skillsDirectory, fileName);

        var serializer = new SerializerBuilder()
            .Build();

        var yaml = serializer.Serialize(skill);
        await File.WriteAllTextAsync(filePath, yaml, ct);

        _logger.LogInformation("Saved skill to {FilePath}", filePath);
    }

    public async Task DeleteAsync(string skillName, CancellationToken ct = default)
    {
        var fileName = $"{skillName.ToLowerInvariant().Replace(" ", "-")}.yaml";
        var filePath = Path.Combine(_skillsDirectory, fileName);

        if (File.Exists(filePath))
        {
            await Task.Run(() => File.Delete(filePath), ct);
            _logger.LogInformation("Deleted skill: {SkillName}", skillName);
        }
    }
}