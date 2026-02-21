using Agentic.Core;

namespace Agentic.Abstractions;

public interface IAssistantContextFactory
{
    AgentContext Create(string input, IReadOnlyList<ChatMessage> history);
}
