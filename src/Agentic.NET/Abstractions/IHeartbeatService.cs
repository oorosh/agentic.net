using Agentic.Core;

namespace Agentic.Abstractions;

/// <summary>
/// Manages proactive, scheduled heartbeat ticks for an agent.
/// On each tick the agent wakes up, checks its HEARTBEAT.md task list, and either
/// acts on pending tasks or replies with the silent token and prunes the exchange
/// from conversation history to avoid context pollution.
/// </summary>
public interface IHeartbeatService : IAsyncDisposable
{
    /// <summary>
    /// Raised after each heartbeat tick completes (including silent ones).
    /// </summary>
    event EventHandler<HeartbeatResult>? Ticked;

    /// <summary>
    /// Starts the periodic heartbeat timer.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the periodic heartbeat timer and waits for any in-flight tick to finish.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a single heartbeat tick immediately, regardless of the timer schedule.
    /// </summary>
    Task<HeartbeatResult> TickAsync(CancellationToken cancellationToken = default);
}
