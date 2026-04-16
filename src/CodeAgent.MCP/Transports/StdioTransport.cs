using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CodeAgent.MCP.Transports;

public interface IMcpTransport
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct = default);
    Task<T> SendRequestAsync<T>(string method, JsonElement? parameters, CancellationToken ct = default);
    Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken ct = default);
    event Action<JsonElement>? OnNotification;
}

public class StdioTransport : IMcpTransport, IDisposable
{
    private readonly string _command;
    private readonly List<string> _args;
    private readonly Dictionary<string, string> _env;
    private readonly ILogger<StdioTransport> _logger;
    private Process? _process;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _requestId = 0;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();

    public bool IsConnected => _process != null && !_process.HasExited;

    public event Action<JsonElement>? OnNotification;

    public StdioTransport(string command, List<string> args, Dictionary<string, string>? env = null, ILogger<StdioTransport>? logger = null)
    {
        _command = command;
        _args = args;
        _env = env ?? new Dictionary<string, string>();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StdioTransport>.Instance;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _command,
                Arguments = string.Join(" ", _args),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var (key, value) in _env)
            {
                startInfo.Environment[key] = value;
            }

            _process = new Process { StartInfo = startInfo };
            _process.Start();

            _writer = _process.StandardInput;
            _writer.AutoFlush = true;

            await Task.Delay(500);

            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadOutputAsync(_readCts.Token, _process.StandardOutput));
            _ = Task.Run(() => ReadErrorAsync(_readCts.Token, _process.StandardError));

            _logger.LogInformation("MCP stdio transport connected to {Command}", _command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect MCP stdio transport");
            throw;
        }
    }

    private async Task ReadOutputAsync(CancellationToken ct, StreamReader reader)
    {
        var buffer = new char[4096];
        var messageBuffer = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        var line = messageBuffer.ToString().Trim();
                        messageBuffer.Clear();

                        if (!string.IsNullOrEmpty(line))
                        {
                            _logger.LogDebug("Received from stdout: {Line}", line);
                            ProcessMessage(line);
                        }
                    }
                    else
                    {
                        messageBuffer.Append(buffer[i]);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error reading MCP output");
            }
        }
    }

    private async Task ReadErrorAsync(CancellationToken ct, StreamReader reader)
    {
        var buffer = new char[4096];
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
                
                var error = new string(buffer, 0, bytesRead);
                _logger.LogWarning("MCP stderr: {Error}", error.Trim());
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error reading MCP stderr");
            }
        }
    }

private void ProcessMessage(string line)
    {
        try
        {
            _logger.LogDebug("Processing raw input line: {Line}", line);
            
            var messages = SplitJsonMessages(line);
            _logger.LogDebug("Split into {Count} message(s)", messages.Count);
            
            foreach (var msg in messages)
            {
                if (string.IsNullOrWhiteSpace(msg)) continue;
                
                _logger.LogDebug("Processing JSON: {Msg}", msg);
                
                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
                {
                    var requestId = idElement.GetInt32();
                    _logger.LogDebug("Found response with id={RequestId}, pending requests: {Pending}", 
                        requestId, string.Join(", ", _pendingRequests.Keys));
                    
                    if (_pendingRequests.TryGetValue(requestId, out var tcs))
                    {
                        _pendingRequests.Remove(requestId);
                        if (root.TryGetProperty("result", out var result))
                        {
                            _logger.LogDebug("Setting result for request {RequestId}", requestId);
                            tcs.SetResult(result.Clone());
                        }
                        else if (root.TryGetProperty("error", out var error))
                        {
                            _logger.LogDebug("Setting error for request {RequestId}: {Error}", requestId, error.ToString());
                            tcs.SetException(new Exception(error.ToString()));
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Received response for unknown request id: {RequestId}", requestId);
                    }
                }
                else if (root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String)
                {
                    var method = methodElement.GetString();
                    _logger.LogDebug("Received notification: {Method}", method);
                    
                    if (root.TryGetProperty("params", out var @params))
                    {
                        OnNotification?.Invoke(@params.Clone());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process MCP message: {Line}", line);
        }
    }

    private List<string> SplitJsonMessages(string input)
    {
        var messages = new List<string>();
        
        if (input.TrimStart().StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(input);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    messages.Add(item.GetRawText());
                }
                return messages;
            }
            catch { }
        }
        
        var depth = 0;
        var current = new StringBuilder();
        var inString = false;
        var escaped = false;
        
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            
            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }
            
            if (c == '\\' && inString)
            {
                current.Append(c);
                escaped = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
            }
            
            if (!inString)
            {
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                }
            }
            
            current.Append(c);
            
            if (depth == 0 && current.Length > 0)
            {
                var msg = current.ToString().Trim();
                if (!string.IsNullOrEmpty(msg))
                {
                    messages.Add(msg);
                }
                current.Clear();
            }
        }
        
        if (current.Length > 0)
        {
            var msg = current.ToString().Trim();
            if (!string.IsNullOrEmpty(msg))
            {
                messages.Add(msg);
            }
        }
        
        return messages;
    }

    public async Task SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct = default)
    {
        await SendRequestAsync<JsonElement>(method, parameters, ct);
    }

    public async Task<T> SendRequestAsync<T>(string method, JsonElement? parameters, CancellationToken ct = default)
    {
        _logger.LogDebug("SendRequestAsync called for method: {Method}", method);
        
        await _semaphore.WaitAsync(ct);
        try
        {
            var id = ++_requestId;
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id] = tcs;

            var request = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters?.Clone()
            };

            var json = JsonSerializer.Serialize(request);
            _logger.LogDebug("Sending MCP request: {Json}", json);
            
            if (_writer == null || _writer.BaseStream == null)
            {
                throw new Exception("Writer is not available");
            }
            
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
            _logger.LogDebug("Request sent, waiting for response for request ID: {RequestId}", id);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                var result = await tcs.Task.WaitAsync(cts.Token);
                return JsonSerializer.Deserialize<T>(result.GetRawText()) ?? throw new Exception("Failed to deserialize response");
            }
            catch (OperationCanceledException)
            {
                _pendingRequests.Remove(id);
                throw new TimeoutException($"MCP request {method} timed out");
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _readCts?.Cancel();

        try
        {
            _writer?.Close();
            _process?.Kill(true);
            await (_process?.WaitForExitAsync(ct) ?? Task.CompletedTask);
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
            _writer = null;
        }
    }

    public async Task SendNotificationAsync(string method, JsonElement? parameters, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var notification = new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters?.Clone()
            };

            var json = JsonSerializer.Serialize(notification);
            await _writer!.WriteLineAsync(json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();
        _semaphore.Dispose();
    }
}