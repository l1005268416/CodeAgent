using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CodeAgent.Core.Tools;

namespace CodeAgent.Core.Skills;

public class SkillEngine : ISkillEngine
{
    private readonly SkillLoader _loader;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<SkillEngine> _logger;
    private List<SkillDefinition> _loadedSkills = new();

    public SkillEngine(SkillLoader loader, IToolRegistry toolRegistry, ILogger<SkillEngine> logger)
    {
        _loader = loader;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task LoadSkillsAsync(CancellationToken ct = default)
    {
        _loadedSkills = (await _loader.LoadAllAsync(ct)).ToList();
        _logger.LogInformation("Loaded {Count} skills", _loadedSkills.Count);
    }

    public IReadOnlyList<SkillDefinition> GetAllSkills()
    {
        return _loadedSkills;
    }

    public SkillDefinition? GetSkill(string name)
    {
        return _loadedSkills.FirstOrDefault(s => 
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            s.Tags.Any(t => t.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<SkillExecutionContext> PrepareExecutionAsync(string skillName, Dictionary<string, string>? parameters = null, CancellationToken ct = default)
    {
        var skill = GetSkill(skillName) 
            ?? throw new KeyNotFoundException($"Skill not found: {skillName}");

        var context = new SkillExecutionContext
        {
            Skill = skill,
            Parameters = parameters ?? new Dictionary<string, string>()
        };

        ValidateRequiredTools(skill);
        
        foreach (var param in skill.Parameters.Where(p => p.Required && !context.Parameters.ContainsKey(p.Name)))
        {
            if (!string.IsNullOrEmpty(param.DefaultValue))
            {
                context.Parameters[param.Name] = param.DefaultValue;
            }
            else
            {
                throw new InvalidOperationException($"Missing required parameter: {param.Name}");
            }
        }

        return await Task.FromResult(context);
    }

    public string RenderPrompt(SkillExecutionContext context)
    {
        var prompt = context.Skill.PromptTemplate;

        var variablePattern = new Regex(@"\{\{(\w+)\}\}");
        prompt = variablePattern.Replace(prompt, match =>
        {
            var variableName = match.Groups[1].Value;
            
            if (context.Parameters.TryGetValue(variableName, out var paramValue))
            {
                return paramValue;
            }
            
            if (context.Variables.TryGetValue(variableName, out var varValue))
            {
                return varValue;
            }

            return match.Value;
        });

        return prompt;
    }

    public string GetSystemPrompt(SkillExecutionContext context)
    {
        var systemPrompt = context.Skill.SystemPrompt;

        if (string.IsNullOrEmpty(systemPrompt))
        {
            systemPrompt = "You are a helpful AI assistant.";
        }

        var variablePattern = new Regex(@"\{\{(\w+)\}\}");
        systemPrompt = variablePattern.Replace(systemPrompt, match =>
        {
            var variableName = match.Groups[1].Value;
            
            if (context.Variables.TryGetValue(variableName, out var value))
            {
                return value;
            }

            return match.Value;
        });

        return systemPrompt;
    }

    public void ValidateRequiredTools(SkillDefinition skill)
    {
        if (skill.RequiredTools.Count == 0) return;

        var availableTools = _toolRegistry.GetAll().Select(t => t.Name).ToHashSet();
        var missingTools = skill.RequiredTools.Where(t => !availableTools.Contains(t)).ToList();

        if (missingTools.Count > 0)
        {
            throw new InvalidOperationException(
                $"Skill '{skill.Name}' requires tools that are not available: {string.Join(", ", missingTools)}. " +
                $"Available tools: {string.Join(", ", availableTools)}");
        }
    }

    public async Task AddOrUpdateSkillAsync(SkillDefinition skill, CancellationToken ct = default)
    {
        await _loader.SaveAsync(skill, ct);
        
        var existing = _loadedSkills.FindIndex(s => s.Name == skill.Name);
        if (existing >= 0)
        {
            _loadedSkills[existing] = skill;
        }
        else
        {
            _loadedSkills.Add(skill);
        }

        _logger.LogInformation("Added/Updated skill: {SkillName}", skill.Name);
    }

    public async Task DeleteSkillAsync(string skillName, CancellationToken ct = default)
    {
        await _loader.DeleteAsync(skillName, ct);
        
        _loadedSkills.RemoveAll(s => s.Name == skillName);
        
        _logger.LogInformation("Deleted skill: {SkillName}", skillName);
    }

    public SkillDefinition? FindSimilarSkill(string query, double threshold = 0.5)
    {
        var queryLower = query.ToLowerInvariant();
        
        var scoredSkills = _loadedSkills.Select(s => new
        {
            Skill = s,
            Score = CalculateSimilarity(queryLower, s)
        }).OrderByDescending(x => x.Score).ToList();

        return scoredSkills.FirstOrDefault(x => x.Score >= threshold)?.Skill;
    }

    private double CalculateSimilarity(string query, SkillDefinition skill)
    {
        var maxScore = 0.0;
        
        if (skill.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            maxScore = Math.Max(maxScore, 0.8);
        
        if (skill.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            maxScore = Math.Max(maxScore, 0.6);
        
        foreach (var tag in skill.Tags)
        {
            if (tag.Contains(query, StringComparison.OrdinalIgnoreCase))
                maxScore = Math.Max(maxScore, 0.7);
        }

        return maxScore;
    }
}

public interface ISkillEngine
{
    Task LoadSkillsAsync(CancellationToken ct = default);
    IReadOnlyList<SkillDefinition> GetAllSkills();
    SkillDefinition? GetSkill(string name);
    Task<SkillExecutionContext> PrepareExecutionAsync(string skillName, Dictionary<string, string>? parameters = null, CancellationToken ct = default);
    string RenderPrompt(SkillExecutionContext context);
    string GetSystemPrompt(SkillExecutionContext context);
    void ValidateRequiredTools(SkillDefinition skill);
    Task AddOrUpdateSkillAsync(SkillDefinition skill, CancellationToken ct = default);
    Task DeleteSkillAsync(string skillName, CancellationToken ct = default);
    SkillDefinition? FindSimilarSkill(string query, double threshold = 0.5);
}