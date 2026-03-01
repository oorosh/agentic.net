using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agentic.Abstractions;
using Agentic.Core;

namespace Agentic.Providers.OpenAi;

public sealed class OpenAiChatModelProvider : IModelProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly IReadOnlyList<OpenAiFunctionToolDefinition> _tools;

    public OpenAiChatModelProvider(
        string apiKey,
        string model = OpenAiModels.Gpt4oMini,
        IReadOnlyList<OpenAiFunctionToolDefinition>? tools = null)
    {
        _apiKey = apiKey;
        _model = model;
        _tools = tools ?? [];
    }

    public IAgentModel CreateModel() => new OpenAiChatModel(_apiKey, _model, _tools);
}

public sealed record OpenAiFunctionToolDefinition(
    string Name,
    string Description,
    IReadOnlyList<OpenAiFunctionToolParameter> Parameters);

public sealed record OpenAiFunctionToolParameter(
    string Name,
    string Type,
    string Description,
    bool Required = true);

internal sealed class OpenAiChatModel : IAgentModel
{
    private static readonly HttpClient SharedHttpClient = new();
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new() { WriteIndented = false };
    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private readonly string _model;
    private readonly string _apiKey;
    private readonly IReadOnlyList<OpenAiFunctionToolDefinition> _tools;

    public OpenAiChatModel(
        string apiKey,
        string model,
        IReadOnlyList<OpenAiFunctionToolDefinition> tools)
    {
        _apiKey = apiKey;
        _model = model;
        _tools = tools;
    }

    public async Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        using var activity = AgenticTelemetry.ActivitySource.StartActivity(AgenticTelemetry.Spans.LlmComplete);
        activity?.SetTag(AgenticTelemetry.Tags.GenAiSystem, "openai");
        activity?.SetTag(AgenticTelemetry.Tags.GenAiOperation, "chat");
        activity?.SetTag(AgenticTelemetry.Tags.GenAiModel, _model);

        AgenticTelemetry.LlmCallCounter.Add(1, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));

        try
        {
            var payload = BuildPayload(messages, stream: false);

            using var content = new StringContent(JsonSerializer.Serialize(payload, PayloadSerializerOptions));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl) { Content = content };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            // Parse token usage
            UsageInfo? usage = null;
            if (json.RootElement.TryGetProperty("usage", out var usageEl))
            {
                usageEl.TryGetProperty("prompt_tokens", out var promptEl);
                usageEl.TryGetProperty("completion_tokens", out var completionEl);
                usageEl.TryGetProperty("total_tokens", out var totalEl);
                var prompt = promptEl.ValueKind == JsonValueKind.Number ? promptEl.GetInt32() : 0;
                var completion = completionEl.ValueKind == JsonValueKind.Number ? completionEl.GetInt32() : 0;
                var total = totalEl.ValueKind == JsonValueKind.Number ? totalEl.GetInt32() : prompt + completion;
                usage = new UsageInfo(prompt, completion, total);

                activity?.SetTag(AgenticTelemetry.Tags.PromptTokens, prompt);
                activity?.SetTag(AgenticTelemetry.Tags.CompletionTokens, completion);
                AgenticTelemetry.PromptTokenCounter.Add(prompt, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));
                AgenticTelemetry.CompletionTokenCounter.Add(completion, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));
            }

            // Model id echoed back by OpenAI
            var modelId = json.RootElement.TryGetProperty("model", out var modelEl)
                ? modelEl.GetString()
                : null;

            var choice = json.RootElement.GetProperty("choices")[0];

            // Finish reason
            var finishReason = choice.TryGetProperty("finish_reason", out var frEl) && frEl.ValueKind != JsonValueKind.Null
                ? frEl.GetString()
                : null;

            var message = choice.GetProperty("message");
            var toolCalls = ParseToolCalls(message);
            var text = message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;

            return new AgentResponse(text, toolCalls, usage, finishReason, modelId);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async IAsyncEnumerable<StreamingToken> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = AgenticTelemetry.ActivitySource.StartActivity(AgenticTelemetry.Spans.LlmComplete);
        activity?.SetTag(AgenticTelemetry.Tags.GenAiSystem, "openai");
        activity?.SetTag(AgenticTelemetry.Tags.GenAiOperation, "chat");
        activity?.SetTag(AgenticTelemetry.Tags.GenAiModel, _model);

        AgenticTelemetry.LlmCallCounter.Add(1, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));

        HttpResponseMessage httpResponse;
        try
        {
            var payload = BuildPayload(messages, stream: true);
            using var reqContent = new StringContent(JsonSerializer.Serialize(payload, PayloadSerializerOptions));
            reqContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl) { Content = reqContent };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            httpResponse = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }

        string? modelId = null;
        string? finishReason = null;
        UsageInfo? usage = null;
        // Tool call accumulators: index → (id, name, arguments builder)
        Dictionary<int, (string? Id, string Name, StringBuilder Arguments)>? toolCallBuilders = null;

        await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using (httpResponse)
        using (var reader = new System.IO.StreamReader(responseStream, Encoding.UTF8))
        {
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]") break;

                JsonDocument? chunkDoc = null;
                try { chunkDoc = JsonDocument.Parse(data); }
                catch (JsonException) { continue; }

                string? deltaText = null;
                bool hasToolCallDelta = false;

                using (chunkDoc)
                {
                    var root = chunkDoc.RootElement;

                    if (modelId is null && root.TryGetProperty("model", out var modelEl))
                        modelId = modelEl.GetString();

                    // Usage chunk (stream_options: include_usage)
                    if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
                    {
                        usageEl.TryGetProperty("prompt_tokens", out var promptEl);
                        usageEl.TryGetProperty("completion_tokens", out var completionEl);
                        usageEl.TryGetProperty("total_tokens", out var totalEl);
                        var prompt = promptEl.ValueKind == JsonValueKind.Number ? promptEl.GetInt32() : 0;
                        var completion = completionEl.ValueKind == JsonValueKind.Number ? completionEl.GetInt32() : 0;
                        var total = totalEl.ValueKind == JsonValueKind.Number ? totalEl.GetInt32() : prompt + completion;
                        usage = new UsageInfo(prompt, completion, total);
                    }

                    if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                        continue;

                    var choice = choices[0];

                    if (choice.TryGetProperty("finish_reason", out var frEl) && frEl.ValueKind != JsonValueKind.Null)
                        finishReason = frEl.GetString();

                    if (!choice.TryGetProperty("delta", out var delta))
                        continue;

                    // Accumulate tool call deltas
                    if (delta.TryGetProperty("tool_calls", out var tcDeltas) && tcDeltas.ValueKind == JsonValueKind.Array)
                    {
                        hasToolCallDelta = true;
                        toolCallBuilders ??= [];
                        foreach (var tcDelta in tcDeltas.EnumerateArray())
                        {
                            var idx = tcDelta.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                            if (!toolCallBuilders.TryGetValue(idx, out var entry))
                                entry = (null, string.Empty, new StringBuilder());

                            var id = entry.Id;
                            var name = entry.Name;

                            if (tcDelta.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
                                id = idEl.GetString() ?? id;

                            if (tcDelta.TryGetProperty("function", out var funcEl))
                            {
                                if (funcEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind != JsonValueKind.Null)
                                    name = nameEl.GetString() ?? name;
                                if (funcEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind != JsonValueKind.Null)
                                    entry.Arguments.Append(argsEl.GetString());
                            }

                            toolCallBuilders[idx] = (id, name, entry.Arguments);
                        }
                    }

                    // Text delta
                    if (!hasToolCallDelta &&
                        delta.TryGetProperty("content", out var contentEl) &&
                        contentEl.ValueKind != JsonValueKind.Null)
                    {
                        deltaText = contentEl.GetString();
                    }
                } // chunkDoc disposed here — safe to yield after this point

                if (!string.IsNullOrEmpty(deltaText))
                    yield return new StreamingToken(deltaText, IsComplete: false);
            }
        }

        // Record telemetry
        if (usage is not null)
        {
            activity?.SetTag(AgenticTelemetry.Tags.PromptTokens, usage.PromptTokens);
            activity?.SetTag(AgenticTelemetry.Tags.CompletionTokens, usage.CompletionTokens);
            AgenticTelemetry.PromptTokenCounter.Add(usage.PromptTokens, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));
            AgenticTelemetry.CompletionTokenCounter.Add(usage.CompletionTokens, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));
        }

        // Build accumulated tool calls list
        IReadOnlyList<AgentToolCall>? toolCalls = null;
        if (toolCallBuilders is { Count: > 0 })
        {
            toolCalls = toolCallBuilders
                .OrderBy(kv => kv.Key)
                .Select(kv => new AgentToolCall(kv.Value.Name, kv.Value.Arguments.ToString(), kv.Value.Id))
                .ToList();
        }

        yield return new StreamingToken(
            Delta: string.Empty,
            IsComplete: true,
            FinalUsage: usage,
            FinishReason: finishReason,
            ModelId: modelId,
            ToolCalls: toolCalls);
    }

    private object BuildPayload(IReadOnlyList<ChatMessage> messages, bool stream)
    {
        if (_tools.Count == 0)
        {
            if (stream)
                return new { model = _model, messages = messages.Select(ToOpenAiMessage), stream = true, stream_options = new { include_usage = true } };

            return new { model = _model, messages = messages.Select(ToOpenAiMessage) };
        }

        if (stream)
            return new { model = _model, messages = messages.Select(ToOpenAiMessage), tools = _tools.Select(ToOpenAiTool), stream = true, stream_options = new { include_usage = true } };

        return new { model = _model, messages = messages.Select(ToOpenAiMessage), tools = _tools.Select(ToOpenAiTool) };
    }

    private static object ToOpenAiTool(OpenAiFunctionToolDefinition tool)
    {
        var required = tool.Parameters.Where(parameter => parameter.Required).Select(parameter => parameter.Name).ToArray();

        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = new
                {
                    type = "object",
                    properties = tool.Parameters.ToDictionary(
                        parameter => parameter.Name,
                        parameter => (object)new
                        {
                            type = parameter.Type,
                            description = parameter.Description
                        }),
                    required,
                    additionalProperties = false
                }
            }
        };
    }

    private static object ToOpenAiMessage(ChatMessage message)
    {
        if (message.Role == ChatRole.Tool)
        {
            return new
            {
                role = "tool",
                tool_call_id = message.ToolCallId ?? message.ToolName ?? "unknown",
                content = message.Content
            };
        }

        if (message.Role == ChatRole.Assistant && message.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = "assistant",
                content = string.IsNullOrEmpty(message.Content) ? (object?)null : message.Content,
                tool_calls = message.ToolCalls.Select(tc => new
                {
                    id = tc.ToolCallId ?? tc.Name,
                    type = "function",
                    function = new
                    {
                        name = tc.Name,
                        arguments = tc.Arguments
                    }
                }).ToArray()
            };
        }

        return new
        {
            role = message.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.System => "system",
                ChatRole.Tool => "tool",
                _ => message.Role.ToString().ToLowerInvariant()
            },
            content = message.Content
        };
    }

    private static IReadOnlyList<AgentToolCall>? ParseToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCallsElement) || toolCallsElement.ValueKind != JsonValueKind.Array)
            return null;

        var calls = new List<AgentToolCall>();
        foreach (var toolCall in toolCallsElement.EnumerateArray())
        {
            if (!toolCall.TryGetProperty("function", out var functionElement))
                continue;

            var toolCallId = toolCall.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;

            var name = functionElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;

            var argumentsJson = functionElement.TryGetProperty("arguments", out var argsElement)
                ? argsElement.GetString() ?? "{}"
                : "{}";

            calls.Add(new AgentToolCall(name, argumentsJson, toolCallId));
        }

        return calls.Count == 0 ? null : calls;
    }
}
