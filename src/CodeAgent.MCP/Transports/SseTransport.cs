using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using YamlDotNet.Core.Tokens;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CodeAgent.MCP.Transports;

public class SseTransport : IMcpTransport, IDisposable
{
    private string relativePath;
    private readonly string _baseUrl;
    private readonly Dictionary<string, string> _headers;
    private readonly ILogger<SseTransport> _logger;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _requestId = 0;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private string messageurl= string.Empty;

    public bool IsConnected => _httpClient != null;

    public event Action<JsonElement>? OnNotification;

    public SseTransport(string baseUrl, Dictionary<string, string>? headers = null, ILogger<SseTransport>? logger = null)
    {
     
        Uri uri = new Uri(baseUrl);

        baseUrl = uri.GetLeftPart(UriPartial.Authority);
        relativePath = uri.AbsolutePath;

        _baseUrl = baseUrl.TrimEnd('/');
        _headers = headers ?? new Dictionary<string, string>();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SseTransport>.Instance;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _httpClient = new HttpClient { BaseAddress = new Uri(_baseUrl) };

        foreach (var (key, value) in _headers)
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }

        _readCts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadSseAsync(_readCts.Token));

        await Task.Delay(1000);

        _logger.LogInformation("MCP SSE transport connected to {BaseUrl}", _baseUrl);
    }

    private async Task ReadSseAsync(CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var currentEvent = new StringBuilder();
            var eventType = "";

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                if (line.StartsWith("event:"))
                {
                    eventType = line[6..].Trim();
                }
                else if (line.StartsWith("data:"))
                {
                    currentEvent.Append(line[5..].Trim());
                }
                else if (line == "" && currentEvent.Length > 0)
                {
                    if (eventType == "endpoint")
                    {
                        var data = currentEvent.ToString().Trim();
                        messageurl = data;
                    }
                    else if (eventType == "message")
                    {
                        ProcessMessage(currentEvent.ToString(), eventType);
                    }
                    
                    currentEvent.Clear();
                    eventType = "";

                    //if (!string.IsNullOrEmpty(data))
                    //{
                    //    messageurl = data;
                    //    //ProcessMessage(data, eventType);
                    //}
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error reading SSE stream");
        }
    }

    private void ProcessMessage(string data, string eventType)
    {
        try
        {
            _logger.LogDebug("Processing SSE data: {Data}, type: {Type}", data, eventType);

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElement))
            {
                var requestId = idElement.ValueKind == JsonValueKind.Number 
                    ? idElement.GetInt32() 
                    : int.Parse(idElement.GetString()!);

                if (_pendingRequests.TryGetValue(requestId, out var tcs))
                {
                    _pendingRequests.Remove(requestId);
                    if (root.TryGetProperty("result", out var result))
                    {
                        tcs.SetResult(result.Clone());
                    }
                    else if (root.TryGetProperty("error", out var error))
                    {
                        tcs.SetException(new Exception(error.ToString()));
                    }
                }
            }
            else if (root.TryGetProperty("method", out var methodElement))
            {
                var method = methodElement.GetString();
                _logger.LogDebug("Received notification: {Method}", method);

                if (root.TryGetProperty("params", out var @params))
                {
                    OnNotification?.Invoke(@params.Clone());
                }
            }
            else if (root.TryGetProperty("jsonrpc", out _))
            {
                if (root.TryGetProperty("result", out var resultData))
                {
                    var id = root.TryGetProperty("id", out var idEl) 
                        ? (idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : int.Parse(idEl.GetString()!))
                        : 0;

                    if (_pendingRequests.TryGetValue(id, out var tcs))
                    {
                        _pendingRequests.Remove(id);
                        tcs.SetResult(resultData.Clone());
                    }
                }
                else if (root.TryGetProperty("error", out var errorData))
                {
                    var id = root.TryGetProperty("id", out var idEl)
                        ? (idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : int.Parse(idEl.GetString()!))
                        : 0;

                    if (_pendingRequests.TryGetValue(id, out var tcs))
                    {
                        _pendingRequests.Remove(id);
                        tcs.SetException(new Exception(errorData.ToString()));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process SSE message: {Data}", data);
        }
    }

    public async Task SendRequestAsync(string method, JsonElement? parameters, CancellationToken ct = default)
    {
        await SendRequestAsync<JsonElement>(method, parameters, ct);
    }

    public async Task<T> SendRequestAsync<T>(string method, JsonElement? parameters, CancellationToken ct = default)
    {
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
            _logger.LogDebug("Sending MCP request via SSE: {Json}", json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, messageurl)
            {
                Content = content
            };

            var response = await _httpClient!.SendAsync(httpRequest, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Received SSE response: {Json}", responseJson);

            //if (_pendingRequests.TryGetValue(id, out var tcs))
            //{
            //    _pendingRequests.Remove(requestId);
            //    if (root.TryGetProperty("result", out var result))
            //    {
            //        tcs.SetResult(result.Clone());
            //    }
            //    else if (root.TryGetProperty("error", out var error))
            //    {
            //        tcs.SetException(new Exception(error.ToString()));
            //    }
            //}
            if (responseJson != "Accepted")
            {
                throw new Exception(responseJson);
            }

            //using var doc = JsonDocument.Parse(responseJson);
            //var root = doc.RootElement;

            //if (root.TryGetProperty("result", out var result))
            //{
            //    return JsonSerializer.Deserialize<T>(result.GetRawText()) ?? throw new Exception("Failed to deserialize response");
            //}
            //else if (root.TryGetProperty("error", out var error))
            //{
            //    throw new Exception(error.ToString());
            //}

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                var responseResult = await tcs.Task.WaitAsync(cts.Token);
                return JsonSerializer.Deserialize<T>(responseResult.GetRawText()) ?? throw new Exception("Failed to deserialize response");
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
            _httpClient?.Dispose();
        }
        catch { }
        finally
        {
            _httpClient = null;
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
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, messageurl)
            {
                Content = content
            };

            await _httpClient!.SendAsync(request, ct);
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
        _httpClient?.Dispose();
        _semaphore.Dispose();
    }
}