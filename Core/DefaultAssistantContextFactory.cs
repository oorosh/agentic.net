using Agentic.Abstractions;

namespace Agentic.Core;

public sealed class DefaultAssistantContextFactory : IAssistantContextFactory
{
    public AgentContext Create(string input, IReadOnlyList<ChatMessage> history)
    {
        return new AgentContext(input, history);
    }
}
