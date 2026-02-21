namespace Agentic.Core;

public enum ChatRole
{
    System,
    User,
    Assistant
}

public sealed record ChatMessage(ChatRole Role, string Content);

public sealed record AgentResponse(string Content);
