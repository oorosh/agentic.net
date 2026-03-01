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

public sealed record AgentResponse(string Content, IReadOnlyList<AgentToolCall>? ToolCalls = null)
{
    public bool HasToolCalls => ToolCalls is { Count: > 0 };
}

/// <summary>
/// The result of a single conversation turn returned by <see cref="Abstractions.IAgent.ReplyAsync"/>.
/// </summary>
public sealed record AgentReply(
    /// <summary>The text content of the agent's response.</summary>
    string Content,
    /// <summary>The user message that triggered this reply.</summary>
    ChatMessage UserMessage,
    /// <summary>The assistant message added to history.</summary>
    ChatMessage AssistantMessage)
{
    /// <summary>Implicit conversion to string for backward compatibility.</summary>
    public static implicit operator string(AgentReply reply) => reply.Content;

    /// <inheritdoc/>
    public override string ToString() => Content;
}
