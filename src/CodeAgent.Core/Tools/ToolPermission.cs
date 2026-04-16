namespace CodeAgent.Core.Tools;

public enum ToolPermission
{
    Allow,
    Deny,
    Confirm
}

public class ToolPermissionPolicy
{
    public ToolPermission Default { get; set; } = ToolPermission.Confirm;
    public Dictionary<string, ToolPermission> Specific { get; set; } = new();

    public ToolPermission GetPermission(string toolName)
    {
        return Specific.TryGetValue(toolName, out var permission) ? permission : Default;
    }
}