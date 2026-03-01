using Agentic.Core;

namespace Agentic.Middleware;

public delegate Task<AgentResponse> AgentHandler(AgentContext context, CancellationToken cancellationToken);

public delegate IAsyncEnumerable<StreamingToken> AgentStreamingHandler(AgentContext context, CancellationToken cancellationToken);

public interface IAssistantMiddleware
{
    Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming variant of <see cref="InvokeAsync"/>. The default implementation buffers the
    /// result of <see cref="InvokeAsync"/> and emits a single complete <see cref="StreamingToken"/>,
    /// so middleware that does not need streaming-specific behaviour does not need to override this.
    /// </summary>
    IAsyncEnumerable<StreamingToken> StreamAsync(AgentContext context, AgentStreamingHandler next, CancellationToken cancellationToken = default)
        => DefaultStreamAsync(context, next, this, cancellationToken);

    // Static helper so the default interface method can delegate without recursion.
    private static async IAsyncEnumerable<StreamingToken> DefaultStreamAsync(
        AgentContext context,
        AgentStreamingHandler next,
        IAssistantMiddleware self,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var token in next(context, cancellationToken).WithCancellation(cancellationToken))
            yield return token;
    }
}
