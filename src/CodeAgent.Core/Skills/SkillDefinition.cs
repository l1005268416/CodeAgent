namespace CodeAgent.Core.Skills;

public class SkillDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = "community";
    public string SystemPrompt { get; set; } = string.Empty;
    public string PromptTemplate { get; set; } = string.Empty;
    public List<string> RequiredTools { get; set; } = new();
    public List<SkillParameter> Parameters { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public class SkillParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
}

public class SkillExecutionContext
{
    public SkillDefinition Skill { get; set; } = null!;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = new();
}