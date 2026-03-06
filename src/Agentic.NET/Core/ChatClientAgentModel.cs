using Agentic.Abstractions;
using Microsoft.Extensions.AI;

namespace Agentic.Core;

internal sealed class ChatClientAgentModel : IAgentModel
{
    private readonly IChatClient _client;

    public ChatClientAgentModel(IChatClient client) => _client = client;

    public async Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var meaiMessages = MapToMeai(messages);
        var response = await _client.GetResponseAsync(meaiMessages, cancellationToken: cancellationToken);

        var lastMessage = response.Messages.LastOrDefault();
        var toolCalls = (lastMessage?.Contents ?? [])
            .OfType<FunctionCallContent>()
            .Select(fc => new AgentToolCall(fc.Name, SerializeArgs(fc.Arguments), fc.CallId))
            .ToList();

        UsageInfo? usage = null;
        if (response.Usage is { } u)
            usage = new UsageInfo((int)(u.InputTokenCount ?? 0), (int)(u.OutputTokenCount ?? 0), (int)(u.TotalTokenCount ?? 0));

        return new AgentResponse(
            response.Text ?? string.Empty,
            toolCalls.Count > 0 ? toolCalls : null,
            usage,
            response.FinishReason is { } fr ? fr.Value : null,
            response.ModelId);
    }

    public async IAsyncEnumerable<StreamingToken> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var meaiMessages = MapToMeai(messages);
        var toolCallBuilders = new Dictionary<string, (string Name, System.Text.StringBuilder Args, string CallId)>();
        UsageInfo? usage = null;
        string? finishReason = null;
        string? modelId = null;

        await foreach (var update in _client.GetStreamingResponseAsync(meaiMessages, cancellationToken: cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc)
                {
                    yield return new StreamingToken(tc.Text, IsComplete: false);
                }
                else if (content is FunctionCallContent fcc)
                {
                    var key = fcc.CallId ?? fcc.Name;
                    if (!toolCallBuilders.TryGetValue(key, out var builder))
                    {
                        builder = (fcc.Name, new System.Text.StringBuilder(), fcc.CallId ?? string.Empty);
                        toolCallBuilders[key] = builder;
                    }
                    if (fcc.Arguments is not null)
                        builder.Args.Append(SerializeArgs(fcc.Arguments));
                }
                else if (content is UsageContent uc)
                {
                    usage = new UsageInfo((int)(uc.Details.InputTokenCount ?? 0), (int)(uc.Details.OutputTokenCount ?? 0), (int)(uc.Details.TotalTokenCount ?? 0));
                }
            }
            if (update.FinishReason is { } finReason) finishReason = finReason.Value;
            if (update.ModelId is not null) modelId = update.ModelId;
        }

        var toolCalls = toolCallBuilders.Count > 0
            ? toolCallBuilders.Values.Select(b => new AgentToolCall(b.Name, b.Args.ToString(), b.CallId)).ToList()
            : null;

        yield return new StreamingToken(
            Delta: string.Empty,
            IsComplete: true,
            FinalUsage: usage,
            FinishReason: finishReason,
            ModelId: modelId,
            ToolCalls: toolCalls);
    }

    private static List<Microsoft.Extensions.AI.ChatMessage> MapToMeai(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<Microsoft.Extensions.AI.ChatMessage>(messages.Count);
        foreach (var msg in messages)
        {
            var meaiRole = MapRole(msg.Role);
            if (msg.Role == ChatRole.Tool)
            {
                if (msg.ToolCallId is not null)
                {
                    var meaiToolMsg = new Microsoft.Extensions.AI.ChatMessage(meaiRole,
                        [new FunctionResultContent(msg.ToolCallId, msg.Content)]);
                    result.Add(meaiToolMsg);
                    continue;
                }
                result.Add(new Microsoft.Extensions.AI.ChatMessage(meaiRole, msg.Content));
            }
            else if (msg.ToolCalls is { Count: > 0 })
            {
                var contents = new List<AIContent>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contents.Add(new TextContent(msg.Content));
                foreach (var tc in msg.ToolCalls)
                {
                    Dictionary<string, object?>? args = null;
                    if (tc.Arguments is not null)
                    {
                        try
                        {
                            args = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(tc.Arguments);
                        }
                        catch (System.Text.Json.JsonException)
                        {
                            args = new Dictionary<string, object?> { ["_raw"] = tc.Arguments };
                        }
                    }
                    contents.Add(new FunctionCallContent(tc.ToolCallId ?? tc.Name, tc.Name, args));
                }
                result.Add(new Microsoft.Extensions.AI.ChatMessage(meaiRole, contents));
            }
            else
            {
                result.Add(new Microsoft.Extensions.AI.ChatMessage(meaiRole, msg.Content));
            }
        }
        return result;
    }

    private static Microsoft.Extensions.AI.ChatRole MapRole(ChatRole role) => role switch
    {
        ChatRole.User => Microsoft.Extensions.AI.ChatRole.User,
        ChatRole.Assistant => Microsoft.Extensions.AI.ChatRole.Assistant,
        ChatRole.System => Microsoft.Extensions.AI.ChatRole.System,
        ChatRole.Tool => Microsoft.Extensions.AI.ChatRole.Tool,
        _ => Microsoft.Extensions.AI.ChatRole.User
    };

    private static string SerializeArgs(IDictionary<string, object?>? args)
    {
        if (args is null) return string.Empty;
        // Special case: _raw key is used by test fakes to pass a plain string argument
        if (args.Count == 1 && args.TryGetValue("_raw", out var raw))
            return raw?.ToString() ?? string.Empty;
        return System.Text.Json.JsonSerializer.Serialize(args);
    }
}
