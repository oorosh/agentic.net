namespace Agentic.Core;

/// <summary>
/// Describes why a heartbeat tick was skipped without calling the model.
/// </summary>
public enum HeartbeatSkipReason
{
    /// <summary>Tick was not skipped — the model was called.</summary>
    None,

    /// <summary>The current time falls within the configured quiet hours window.</summary>
    QuietHours,

    /// <summary>A previous tick is still executing; the new tick was dropped to avoid overlap.</summary>
    InFlight,

    /// <summary>HEARTBEAT.md exists but contains no actionable content (empty or comments only).</summary>
    EmptyFile,
}

/// <summary>
/// The outcome of a single heartbeat tick.
/// </summary>
public sealed record HeartbeatResult
{
    /// <summary>
    /// The UTC timestamp when this tick started.
    /// </summary>
    public DateTimeOffset TickedAt { get; init; }

    /// <summary>
    /// Whether the model was actually invoked (<c>Ran</c>) or the tick was bypassed (<c>Skipped</c>).
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// When <see cref="Skipped"/> is <see langword="true"/>, the reason the tick was bypassed.
    /// </summary>
    public HeartbeatSkipReason SkipReason { get; init; }

    /// <summary>
    /// When <see langword="true"/> the agent replied with the silent token only; the exchange
    /// was pruned from conversation history and no visible output was produced.
    /// </summary>
    public bool Silent { get; init; }

    /// <summary>
    /// The model's raw response content, or <see langword="null"/> when the tick was skipped.
    /// </summary>
    public string? Response { get; init; }

    /// <summary>
    /// Wall-clock time the tick took to complete (including model round-trip when not skipped).
    /// </summary>
    public TimeSpan Duration { get; init; }

    // ── Factory helpers ───────────────────────────────────────────────────────

    internal static HeartbeatResult CreateSkipped(HeartbeatSkipReason reason, DateTimeOffset tickedAt, TimeSpan duration) =>
        new() { TickedAt = tickedAt, Skipped = true, SkipReason = reason, Duration = duration };

    internal static HeartbeatResult CreateSilent(DateTimeOffset tickedAt, string response, TimeSpan duration) =>
        new() { TickedAt = tickedAt, Skipped = false, Silent = true, Response = response, Duration = duration };

    internal static HeartbeatResult CreateActive(DateTimeOffset tickedAt, string response, TimeSpan duration) =>
        new() { TickedAt = tickedAt, Skipped = false, Silent = false, Response = response, Duration = duration };
}
