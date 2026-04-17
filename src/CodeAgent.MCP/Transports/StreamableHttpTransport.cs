using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodeAgent.MCP.Transports;

public class StreamableHttpTransport : IMcpTransport, IDisposable
{
    private readonly string _baseUrl;
    private readonly Dictionary<string, string> _headers;
    private readonly ILogger<StreamableHttpTransport> _logger;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _requestId = 0;
    //private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private string? _sessionId;
    private readonly HttpMessageHandler? _customHandler;

    public bool IsConnected => _httpClient != null;

    public event Action<JsonElement>? OnNotification;

    public StreamableHttpTransport(
        string baseUrl, 
        Dictionary<string, string>? headers = null, 
        ILogger<StreamableHttpTransport>? logger = null,
        HttpMessageHandler? customHandler = null)
    {
        Uri uri = new Uri(baseUrl);
        _baseUrl = uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath;
        _baseUrl = _baseUrl.TrimEnd('/');
        _headers = headers ?? new Dictionary<string, string>();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StreamableHttpTransport>.Instance;
        _customHandler = customHandler;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        if (_customHandler != null)
        {
            _httpClient = new HttpClient(_customHandler);
        }
        else
        {
            _httpClient = new HttpClient();
        }
        
        _httpClient.BaseAddress = new Uri(_baseUrl);

        foreach (var (key, value) in _headers)
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }

        //_readCts = new CancellationTokenSource();
        //_readTask = Task.Run(() => ReadStreamAsync(_readCts.Token));

        //await Task.Delay(500);

        _logger.LogInformation("MCP Streamable HTTP transport connected to {BaseUrl}", _baseUrl);
    }

    private async Task ReadStreamAsync(CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/mcp");
            
            if (!string.IsNullOrEmpty(_sessionId))
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            }

            var requestBody = new
            {
                jsonrpc = "2.0",
                method = "initialize",
                @params = new { },
                id = 0
            };
            
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
            {
                _sessionId = sessionIds.FirstOrDefault();
            }

            if (response.StatusCode == (HttpStatusCode)419)
            {
                _logger.LogInformation("Session already exists, reusing session: {SessionId}", _sessionId);
            }

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var buffer = new char[8192];
            var messageBuffer = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == '\x1e')
                    {
                        var jsonStr = messageBuffer.ToString().Trim();
                        messageBuffer.Clear();

                        if (!string.IsNullOrEmpty(jsonStr))
                        {
                            //ProcessMessage(jsonStr);
                        }
                    }
                    else if (buffer[i] == '\n' && messageBuffer.Length == 0)
                    {
                        continue;
                    }
                    else
                    {
                        messageBuffer.Append(buffer[i]);
                    }
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error reading Streamable HTTP stream");
        }
    }
    private JsonElement ProcessMessage(string data, string eventType)
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
                root.TryGetProperty("result", out var result);
                return result.Clone();
            }
            //else if (root.TryGetProperty("method", out var methodElement))
            //{
            //    var method = methodElement.GetString();
            //    _logger.LogDebug("Received notification: {Method}", method);

            //    if (root.TryGetProperty("params", out var @params))
            //    {
            //        OnNotification?.Invoke(@params.Clone());
            //    }
            //}
            else if (root.TryGetProperty("jsonrpc", out _))
            {
                if (root.TryGetProperty("result", out var resultData))
                {
                    var id = root.TryGetProperty("id", out var idEl)
                        ? (idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : int.Parse(idEl.GetString()!))
                        : 0;

                    return resultData.Clone();
                }
                else if (root.TryGetProperty("error", out var errorData))
                {
                    var id = root.TryGetProperty("id", out var idEl)
                        ? (idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : int.Parse(idEl.GetString()!))
                        : 0;
                    throw new Exception(errorData.ToString());

                }
                else
                {
                    throw new Exception("weizhi");
                }

            }
            else
            {
                throw new Exception("weizhi");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process SSE message: {Data}", data);

            throw new Exception(ex.ToString());
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
            //var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            //_pendingRequests[id] = tcs;

            var request = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = parameters?.Clone()
            };

            var json = JsonSerializer.Serialize(request);
            _logger.LogDebug("Sending MCP request via Streamable HTTP: {Json}", json);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            if (!string.IsNullOrEmpty(_sessionId))
            {
                httpRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            }

            var response = await _httpClient!.SendAsync(httpRequest, ct);
            
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
            {
                _sessionId = sessionIds.FirstOrDefault();
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Received Streamable HTTP response: {Json}", responseJson);
            using (var reader = new StringReader(responseJson))
            {
                string line;
                string eventType = "";
                StringBuilder currentEvent=new StringBuilder();
                while ((line = reader.ReadLine()) != null)
                {
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
                        var responseResult=ProcessMessage(currentEvent.ToString(), eventType);
                        return JsonSerializer.Deserialize<T>(responseResult.GetRawText()) ?? throw new Exception("Failed to deserialize response");
                    }

                }
            }
            //    if (responseJson.StartsWith("event:"))
            //{
            //    eventType = line[6..].Trim();
            //}
            //else if (line.StartsWith("data:"))
            //{
            //    currentEvent.Append(line[5..].Trim());
            //}
            //else if (line == "" && currentEvent.Length > 0)
            //{
            //    if (eventType == "endpoint")
            //    {
            //        var data = currentEvent.ToString().Trim();
            //        messageurl = data;
            //    }
            //    else if (eventType == "message")
            //    {
            //        ProcessMessage(currentEvent.ToString(), eventType);
            //    }

            //    currentEvent.Clear();
            //    eventType = "";

            //    //if (!string.IsNullOrEmpty(data))
            //    //{
            //    //    messageurl = data;
            //    //    //ProcessMessage(data, eventType);
            //    //}
            //}
            //if (!string.IsNullOrEmpty(responseJson) && responseJson.TrimStart().StartsWith('{'))
            //{
            //    using var doc = JsonDocument.Parse(responseJson);
            //    var root = doc.RootElement;

            //    if (root.TryGetProperty("result", out var result))
            //    {
            //        return JsonSerializer.Deserialize<T>(result.GetRawText()) ?? throw new Exception("Failed to deserialize response");
            //    }
            //    else if (root.TryGetProperty("error", out var error))
            //    {
            //        throw new Exception(error.ToString());
            //    }
            //}

            throw new Exception("error.ToString()");

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
            if (_httpClient != null && !string.IsNullOrEmpty(_sessionId))
            {
                var closeRequest = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/mcp");
                closeRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
                await _httpClient.SendAsync(closeRequest, ct);
            }
            
            _httpClient?.Dispose();
        }
        catch { }
        finally
        {
            _httpClient = null;
            _sessionId = null;
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
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/mcp")
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(_sessionId))
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            }

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