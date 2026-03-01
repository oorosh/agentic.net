using Agentic.Core;

namespace Agentic.Abstractions;

public interface IAgentModel
{
    Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams token deltas for the given messages, yielding one <see cref="StreamingToken"/> per chunk.
    /// The final chunk has <see cref="StreamingToken.IsComplete"/> set to <see langword="true"/> and
    /// carries <see cref="StreamingToken.FinalUsage"/>, <see cref="StreamingToken.FinishReason"/>,
    /// <see cref="StreamingToken.ModelId"/>, and any accumulated <see cref="StreamingToken.ToolCalls"/>.
    /// </summary>
    IAsyncEnumerable<StreamingToken> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
}
