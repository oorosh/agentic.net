namespace Agentic.Core;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    string? ToolName = null,
    string? ToolCallId = null,
    IReadOnlyList<AgentToolCall>? ToolCalls = null);

public sealed record AgentToolCall(string Name, string Arguments, string? ToolCallId = null);

/// <summary>Token usage statistics from a single LLM completion call.</summary>
public sealed record UsageInfo(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    /// <summary>Adds two <see cref="UsageInfo"/> instances together.</summary>
    public static UsageInfo operator +(UsageInfo a, UsageInfo b) =>
        new(a.PromptTokens + b.PromptTokens,
            a.CompletionTokens + b.CompletionTokens,
            a.TotalTokens + b.TotalTokens);
}

public sealed record AgentResponse(
    string Content,
    IReadOnlyList<AgentToolCall>? ToolCalls = null,
    UsageInfo? Usage = null,
    string? FinishReason = null,
    string? ModelId = null)
{
    public bool HasToolCalls => ToolCalls is { Count: > 0 };
}

/// <summary>
/// A single token chunk emitted during a streaming response.
/// </summary>
public sealed record StreamingToken(
    /// <summary>The incremental text delta for this chunk.</summary>
    string Delta,
    /// <summary><see langword="true"/> on the final chunk; consumers should stop iterating after this.</summary>
    bool IsComplete,
    /// <summary>Populated on the final chunk with aggregated token usage (when the provider supports it).</summary>
    UsageInfo? FinalUsage = null,
    /// <summary>Populated on the final chunk with the model's finish reason (e.g. <c>stop</c>, <c>tool_calls</c>, <c>length</c>).</summary>
    string? FinishReason = null,
    /// <summary>The model identifier echoed back by the provider, populated on the final chunk.</summary>
    string? ModelId = null,
    /// <summary>Tool calls accumulated over the stream, populated on the final chunk when the finish reason is <c>tool_calls</c>.</summary>
    IReadOnlyList<AgentToolCall>? ToolCalls = null);

/// <summary>
/// The result of a single conversation turn returned by <see cref="Abstractions.IAgent.ReplyAsync"/>.
/// </summary>
public sealed record AgentReply(
    /// <summary>The text content of the agent's response.</summary>
    string Content,
    /// <summary>The user message that triggered this reply.</summary>
    ChatMessage UserMessage,
    /// <summary>The assistant message added to history.</summary>
    ChatMessage AssistantMessage,
    /// <summary>Aggregated token usage across all LLM calls in this turn (including tool-call loops).</summary>
    UsageInfo? Usage = null,
    /// <summary>The finish reason from the final LLM call (e.g. <c>stop</c>, <c>tool_calls</c>, <c>length</c>).</summary>
    string? FinishReason = null,
    /// <summary>The model identifier reported by the provider.</summary>
    string? ModelId = null,
    /// <summary>End-to-end wall-clock duration of the reply turn.</summary>
    TimeSpan Duration = default)
{
    /// <summary>Implicit conversion to string for backward compatibility.</summary>
    public static implicit operator string(AgentReply reply) => reply.Content;

    /// <inheritdoc/>
    public override string ToString() => Content;
}
