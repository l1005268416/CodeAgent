using System.Diagnostics;
using System.Text.Json;

namespace CodeAgent.Core.Tools.BuiltIn;

public class ShellExecTool : ITool
{
    public string Name => "shell_exec";
    public string Description => "Execute a shell command";
    public JsonElement InputSchema => JsonSerializer.Deserialize<JsonElement>(@"
{
    ""type"": ""object"",
    ""properties"": {
        ""command"": {
            ""type"": ""string"",
            ""description"": ""The shell command to execute""
        }
    },
    ""required"": [""command""]
}");

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

            var processStartInfo = new ProcessStartInfo
            {
                //FileName = OperatingSystem.IsWindows() ? "wsl" : "/bin/bash",
                //Arguments = OperatingSystem.IsWindows() ? $"--exec bash -c {command}" : $"-c \"{command}\"",
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
                Arguments = OperatingSystem.IsWindows() ? $"-NoProfile -Command {command}" : $"-c \"{command}\"",
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
                result = $"Command exited with code {process.ExitCode}";
            }
            return new ToolResult 
            { 
                Success = process.ExitCode == 0, 
                Content = result,
                Error = process.ExitCode != 0 ? $"Command failed with exit code {process.ExitCode}" : null
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}