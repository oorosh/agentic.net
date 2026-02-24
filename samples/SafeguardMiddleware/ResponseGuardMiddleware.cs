using Agentic.Core;
using Agentic.Middleware;

namespace SafeguardMiddleware;

/// <summary>
/// Middleware that validates LLM responses before returning them to the user.
/// Filters out prohibited content from responses.
/// </summary>
public sealed class ResponseGuardMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
    {
        // Get the response from LLM
        var response = await next(context, cancellationToken);

        // Simple example: censor responses containing "bad"
        var filteredContent = response.Content.Replace("bad", "[censored]", StringComparison.OrdinalIgnoreCase);

        // Return modified response
        return new AgentResponse(filteredContent, response.ToolCalls);
    }
}