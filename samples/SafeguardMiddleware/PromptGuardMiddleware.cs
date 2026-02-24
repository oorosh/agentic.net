using Agentic.Core;
using Agentic.Middleware;

namespace SafeguardMiddleware;

/// <summary>
/// Middleware that validates user prompts before sending them to the LLM.
/// Blocks prompts containing prohibited words.
/// </summary>
public sealed class PromptGuardMiddleware : IAssistantMiddleware
{
    public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
    {
        // Simple example: block prompts containing "bad"
        if (context.Input.Contains("bad", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new AgentResponse("I'm sorry, but I cannot process that request as it contains inappropriate content."));
        }

        // Proceed to next middleware/LLM
        return next(context, cancellationToken);
    }
}