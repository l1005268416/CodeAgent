using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeAgent.Core.Tools.BuiltIn;

public class GitExecTool : ITool
{
    public string Name => "git_exec";
    public string Description => "Execute a git command in a repository";
    public JsonElement InputSchema => JsonSerializer.Deserialize<JsonElement>(@"
{
    ""type"": ""object"",
    ""properties"": {
        ""command"": {
            ""type"": ""string"",
            ""description"": ""The git command to execute (without 'git' prefix, e.g., 'status', 'log -5', 'branch -a')""
        },
        ""repo_path"": {
            ""type"": ""string"",
            ""description"": ""Optional: repository path. Defaults to current directory""
        }
    },
    ""required"": [""command""]
}");

    private static readonly HashSet<string> SafeCommands = new(StringComparer.Ordinal)
    {
        "status", "log", "branch", "diff", "show", "rev-parse", "describe", "fetch", "pull",
        " remote", "-v", "--version", " ls-files", " ls-tree", " name-rev", " symbolic-ref"
    };

    private static readonly HashSet<string> UnsafeCommands = new(StringComparer.Ordinal)
    {
        "push", "rebase", "merge", "reset", "checkout", "stash", "clean", "rm", "mv"
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!parameters.TryGetProperty("command", out var commandElement))
            {
                return new ToolResult { Success = false, Error = "Missing required parameter: command" };
            }

            var command = commandElement.GetString();
            if (string.IsNullOrEmpty(command))
            {
                return new ToolResult { Success = false, Error = "Command cannot be empty" };
            }

            command = command.Trim();
            var normalizedCmd = command.ToLowerInvariant();

            var isSafe = SafeCommands.Any(s => normalizedCmd.StartsWith(s) || normalizedCmd == s);
            var isUnsafe = UnsafeCommands.Any(s => normalizedCmd.StartsWith(s) || normalizedCmd == s);

            if (!isSafe && isUnsafe)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = $"Command '{command}' is potentially dangerous. Use git_exec for read-only commands like status, log, branch, diff, show, etc."
                };
            }

            string repoPath = ".";
            if (parameters.TryGetProperty("repo_path", out var pathElement))
            {
                var path = pathElement.GetString();
                if (!string.IsNullOrEmpty(path))
                {
                    repoPath = path;
                }
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var result = string.IsNullOrEmpty(error) ? output : $"{output}\nSTDERR: {error}";
            if (process.ExitCode != 0 && string.IsNullOrEmpty(result))
            {
                result = $"Git command exited with code {process.ExitCode}";
            }

            return new ToolResult
            {
                Success = process.ExitCode == 0,
                Content = result,
                Error = process.ExitCode != 0 ? $"Git command failed with exit code {process.ExitCode}" : null
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}