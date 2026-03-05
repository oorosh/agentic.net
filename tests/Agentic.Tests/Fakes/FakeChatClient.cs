using Agentic.Abstractions;
using Agentic.Core;
using Microsoft.Extensions.AI;

namespace Agentic.Tests.Fakes;

internal sealed class FakeChatClient : IChatClient
{
    private readonly IAgentModel _model;
    public FakeChatClient(IAgentModel model) => _model = model;

    public ChatClientMetadata Metadata => new("fake", null, null);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agenticMessages = MapToAgentic(messages);
        var r = await _model.CompleteAsync(agenticMessages, cancellationToken);

        List<AIContent> contents = [];
        if (!string.IsNullOrEmpty(r.Content))
            contents.Add(new TextContent(r.Content));

        if (r.ToolCalls is { Count: > 0 } calls)
        {
            foreach (var call in calls)
            {
                var args = new Dictionary<string, object?> { ["_raw"] = call.Arguments };
                contents.Add(new FunctionCallContent(
                    call.ToolCallId ?? Guid.NewGuid().ToString(),
                    call.Name,
                    args));
            }
        }

        var chatMsg = new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.Assistant, contents);
        var response = new ChatResponse(chatMsg)
        {
            ModelId = r.ModelId,
            FinishReason = r.FinishReason is not null
                ? new ChatFinishReason(r.FinishReason) : null,
            Usage = r.Usage is not null
                ? new UsageDetails
                {
                    InputTokenCount = r.Usage.PromptTokens,
                    OutputTokenCount = r.Usage.CompletionTokens,
                    TotalTokenCount = r.Usage.TotalTokens
                }
                : null
        };
        return response;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agenticMessages = MapToAgentic(messages);
        return WrapStreamAsync(agenticMessages, cancellationToken);
    }

    private static IReadOnlyList<Agentic.Core.ChatMessage> MapToAgentic(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages) =>
        messages.Select(m =>
        {
            if (m.Role == Microsoft.Extensions.AI.ChatRole.Tool)
            {
                var funcResult = m.Contents.OfType<FunctionResultContent>().FirstOrDefault();
                var content = funcResult?.Result?.ToString() ?? m.Text ?? string.Empty;
                return new Agentic.Core.ChatMessage(Agentic.Core.ChatRole.Tool, content);
            }
            return new Agentic.Core.ChatMessage(MapRole(m.Role), m.Text ?? string.Empty);
        }).ToList();

    private async IAsyncEnumerable<ChatResponseUpdate> WrapStreamAsync(
        IReadOnlyList<Agentic.Core.ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var token in _model.StreamAsync(messages, cancellationToken))
        {
            var update = new ChatResponseUpdate(
                Microsoft.Extensions.AI.ChatRole.Assistant,
                token.IsComplete ? null : token.Delta);
            if (token.IsComplete)
            {
                if (token.FinishReason is not null)
                    update.FinishReason = new Microsoft.Extensions.AI.ChatFinishReason(token.FinishReason);
                update.ModelId = token.ModelId;
                if (token.FinalUsage is { } u)
                    update.Contents.Add(new UsageContent(new UsageDetails
                    {
                        InputTokenCount = u.PromptTokens,
                        OutputTokenCount = u.CompletionTokens,
                        TotalTokenCount = u.TotalTokens
                    }));
            }
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

    private static Agentic.Core.ChatRole MapRole(Microsoft.Extensions.AI.ChatRole role)
    {
        if (role == Microsoft.Extensions.AI.ChatRole.User) return Agentic.Core.ChatRole.User;
        if (role == Microsoft.Extensions.AI.ChatRole.Assistant) return Agentic.Core.ChatRole.Assistant;
        if (role == Microsoft.Extensions.AI.ChatRole.System) return Agentic.Core.ChatRole.System;
        if (role == Microsoft.Extensions.AI.ChatRole.Tool) return Agentic.Core.ChatRole.Tool;
        return Agentic.Core.ChatRole.User;
    }
}

internal static class FakeModelStreamHelper
{
    public static async IAsyncEnumerable<StreamingToken> StreamFromCompleteAsync(
        IAgentModel model,
        IReadOnlyList<Agentic.Core.ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        var response = await model.CompleteAsync(messages, cancellationToken);
        if (!string.IsNullOrEmpty(response.Content))
            yield return new StreamingToken(response.Content, IsComplete: false);

        yield return new StreamingToken(
            Delta: string.Empty,
            IsComplete: true,
            FinalUsage: response.Usage,
            FinishReason: response.FinishReason,
            ModelId: response.ModelId,
            ToolCalls: response.ToolCalls);
    }
}
