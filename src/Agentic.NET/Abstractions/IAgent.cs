using Agentic.Core;

namespace Agentic.Abstractions;

/// <summary>
/// Represents an AI agent that can engage in conversation.
/// </summary>
public interface IAgent
{
    /// <summary>
    /// The conversation history accumulated across all turns.
    /// </summary>
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>
    /// The skills loaded for this agent, or <see langword="null"/> if none were configured.
    /// </summary>
    IReadOnlyList<Skill>? Skills { get; }

    /// <summary>
    /// The soul (identity/personality) loaded for this agent, or <see langword="null"/> if none was configured.
    /// </summary>
    SoulDocument? Soul { get; }

    /// <summary>
    /// The heartbeat service for this agent, or <see langword="null"/> if heartbeat was not configured.
    /// </summary>
    IHeartbeatService? Heartbeat { get; }

    /// <summary>
    /// Initializes the agent, loading memory, embeddings, skills, and soul.
    /// Safe to call multiple times — initialization only happens once.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a user message and returns the agent's reply.
    /// </summary>
    Task<AgentReply> ReplyAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a user message and streams the response token-by-token.
    /// Each yielded <see cref="StreamingToken"/> contains an incremental <see cref="StreamingToken.Delta"/>.
    /// The final chunk has <see cref="StreamingToken.IsComplete"/> set to <see langword="true"/> and
    /// carries usage, finish reason, and model ID.
    /// History and memory are updated after the stream is fully consumed.
    /// </summary>
    IAsyncEnumerable<StreamingToken> StreamAsync(string input, CancellationToken cancellationToken = default);
}
