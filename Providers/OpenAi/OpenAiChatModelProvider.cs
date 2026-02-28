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
        var payload = BuildPayload(messages);

        using var content = new StringContent(JsonSerializer.Serialize(payload, PayloadSerializerOptions));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl) { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await SharedHttpClient.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var message = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var toolCalls = ParseToolCalls(message);
        var text = message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;

        return new AgentResponse(text, toolCalls);
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
            var toolName = message.ToolName ?? "unknown";
            return new
            {
                role = "assistant",
                content = $"Tool '{toolName}' returned: {message.Content}. Use this result to answer the user."
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

            var name = functionElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;

            var argumentsJson = functionElement.TryGetProperty("arguments", out var argsElement)
                ? argsElement.GetString() ?? "{}"
                : "{}";

            var arguments = TryExtractSingleStringArgument(argumentsJson) ?? argumentsJson;
            calls.Add(new AgentToolCall(name, arguments));
        }

        return calls.Count == 0 ? null : calls;
    }

    private static string? TryExtractSingleStringArgument(string argumentsJson)
    {
        try
        {
            using var argsDoc = JsonDocument.Parse(argumentsJson);
            if (argsDoc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            using var enumerator = argsDoc.RootElement.EnumerateObject();
            if (!enumerator.MoveNext())
            {
                return null;
            }

            var first = enumerator.Current;
            if (enumerator.MoveNext())
            {
                return null;
            }

            return first.Value.ValueKind == JsonValueKind.String
                ? first.Value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
