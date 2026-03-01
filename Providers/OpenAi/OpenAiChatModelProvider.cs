using System.Diagnostics;
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
            var payload = BuildPayload(messages);

            using var content = new StringContent(JsonSerializer.Serialize(payload, PayloadSerializerOptions));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl) { Content = content };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            // Parse token usage if present
            if (json.RootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var promptTokensEl) && promptTokensEl.TryGetInt64(out var promptTokens))
                {
                    activity?.SetTag(AgenticTelemetry.Tags.PromptTokens, promptTokens);
                    AgenticTelemetry.PromptTokenCounter.Add(promptTokens, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));
                }
                if (usage.TryGetProperty("completion_tokens", out var completionTokensEl) && completionTokensEl.TryGetInt64(out var completionTokens))
                {
                    activity?.SetTag(AgenticTelemetry.Tags.CompletionTokens, completionTokens);
                    AgenticTelemetry.CompletionTokenCounter.Add(completionTokens, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.GenAiModel, _model));
                }
            }

            var message = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message");

            var toolCalls = ParseToolCalls(message);
            var text = message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;

            return new AgentResponse(text, toolCalls);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private object BuildPayload(IReadOnlyList<ChatMessage> messages)
    {
        if (_tools.Count == 0)
        {
            return new
            {
                model = _model,
                messages = messages.Select(ToOpenAiMessage)
            };
        }

        return new
        {
            model = _model,
            messages = messages.Select(ToOpenAiMessage),
            tools = _tools.Select(ToOpenAiTool)
        };
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
        {
            return null;
        }

        var calls = new List<AgentToolCall>();
        foreach (var toolCall in toolCallsElement.EnumerateArray())
        {
            if (!toolCall.TryGetProperty("function", out var functionElement))
            {
                continue;
            }

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
