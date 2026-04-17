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

        _logger.LogInformation("MCP Streamable HTTP transport connected to {BaseUrl}", _baseUrl);
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

            var responseData = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Received Streamable HTTP response: {Json}", responseData);
            if (string.IsNullOrEmpty(responseData))
            {
                throw new Exception("response is null.");
            }
            string responsejson = "";
            using (var reader = new StringReader(responseData))
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
                        responsejson = currentEvent.ToString();
                    }

                }
            }
            if (responsejson == "responsejson is nothing.")
            {
                throw new Exception("");
            }
            return JsonSerializer.Deserialize<T>(responsejson) ?? throw new Exception("Failed to deserialize response");
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