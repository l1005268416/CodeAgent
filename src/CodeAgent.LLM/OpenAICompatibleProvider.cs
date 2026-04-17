using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CodeAgent.LLM;

public class OpenAICompatibleProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;

    public string ProviderName => "OpenAI Compatible";
    public string ModelName => _model;

    public OpenAICompatibleProvider(HttpClient httpClient, string baseUrl, string model)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public async Task<ChatResponse> CompleteAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken cancellationToken = default)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages
        };

        if (tools != null && tools.Count > 0)
        {
            request["tools"] = tools;
            request["tool_choice"] = "auto";
        }

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/chat/completions",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        var message = result.GetProperty("choices")[0].GetProperty("message");
        var content = message.TryGetProperty("content", out var contentProp) 
            ? contentProp.GetString() ?? "" 
            : "";
        var finishReason = result.GetProperty("choices")[0].GetProperty("finish_reason").GetString();

        List<ToolCallItem>? toolCalls = null;
        if (message.TryGetProperty("tool_calls", out var tc))
        {
            toolCalls = tc.Deserialize<List<ToolCallItem>>();
        }

        return new ChatResponse
        {
            Content = content,
            FinishReason = finishReason,
            ToolCalls = toolCalls
        };
    }

    public async IAsyncEnumerable<ChatChunk> CompleteStreamAsync(
        List<ChatMessage> messages,
        List<ToolDefinition>? tools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["stream"] = true
        };

        if (tools != null && tools.Count > 0)
        {
            request["tools"] = tools;
        }

        var content = new StringBuilder();
        var toolCallId = "";
        var toolCallName = "";
        var toolCallArguments = new StringBuilder();
        var toolCallType = "function";
        List<ToolCallItem>? toolCalls = null;

        var requestBody = JsonSerializer.Serialize(request);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var data = line[6..];
            if (data == "[DONE]")
                break;

            JsonElement json;
            try
            {
                json = JsonSerializer.Deserialize<JsonElement>(data);
            }
            catch
            {
                continue;
            }

            var choice = json.GetProperty("choices")[0];

            string? chunkContent = null;
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var contentProp))
                {
                    chunkContent = contentProp.GetString();
                    if (chunkContent != null)
                    {
                        content.Append(chunkContent);
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var tc))
                {
                    foreach (var tcItem in tc.EnumerateArray())
                    {
                        if (tcItem.TryGetProperty("id", out var idProp))
                        {
                            var newId = idProp.GetString() ?? "";
                            if (!string.IsNullOrEmpty(newId) && newId != toolCallId)
                            {
                                toolCallId = newId;
                                toolCallArguments.Clear();
                                toolCalls ??= new List<ToolCallItem>();
                            }
                        }
                        if (tcItem.TryGetProperty("type", out var typeProp))
                        {
                            toolCallType = typeProp.GetString() ?? "function";
                        }
                        if (tcItem.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var nameProp))
                            {
                                var newName = nameProp.GetString() ?? "";
                                if (!string.IsNullOrEmpty(newName) && newName != toolCallName)
                                {
                                    toolCallName = newName;
                                }
                            }
                            if (fn.TryGetProperty("arguments", out var argsProp))
                            {
                                var args = argsProp.GetString();
                                if (args != null)
                                {
                                    toolCallArguments.Append(args);
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(toolCallId) && string.IsNullOrEmpty(toolCallName))
                            continue;

                        var existingIdx = toolCalls.FindIndex(t => t.Id == toolCallId);
                        if (existingIdx >= 0)
                        {
                            toolCalls[existingIdx].Function.Arguments = toolCallArguments.ToString();
                        }
                        else
                        {
                            toolCalls.Add(new ToolCallItem
                            {
                                Id = toolCallId,
                                Type = toolCallType,
                                Function = new ToolCallFunction
                                {
                                    Name = toolCallName,
                                    Arguments = toolCallArguments.ToString()
                                }
                            });
                        }
                    }
                }
            }

            string? finishReason = null;
            if (choice.TryGetProperty("finish_reason", out var fr))
            {
                finishReason = fr.GetString();
            }

            yield return new ChatChunk
            {
                Content = chunkContent ?? "",
                FinishReason = finishReason,
                ToolCalls = toolCalls
            };

            if (finishReason == "stop")
                break;
        }
    }
}