using Agentic.Core;

namespace Agentic.Middleware;

public delegate Task<AgentResponse> AgentHandler(AgentContext context, CancellationToken cancellationToken);

public interface IAssistantMiddleware
{
    Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default);
}
