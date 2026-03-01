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
    /// Initializes the agent, loading memory, embeddings, skills, and soul.
    /// Safe to call multiple times — initialization only happens once.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a user message and returns the agent's reply.
    /// </summary>
    Task<AgentReply> ReplyAsync(string input, CancellationToken cancellationToken = default);
}
