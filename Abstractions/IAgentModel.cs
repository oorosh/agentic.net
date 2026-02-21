using Agentic.Core;

namespace Agentic.Abstractions;

public interface IAgentModel
{
    Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
}
