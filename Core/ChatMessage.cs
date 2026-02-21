namespace Agentic.Core;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record ChatMessage(ChatRole Role, string Content);

public sealed record AgentToolCall(string Name, string Arguments);

public sealed record AgentResponse(string Content, IReadOnlyList<AgentToolCall>? ToolCalls = null)
{
    public bool HasToolCalls => ToolCalls is { Count: > 0 };
}
