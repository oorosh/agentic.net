namespace Agentic.Core;

/// <summary>
/// Configuration for the proactive heartbeat feature.
/// </summary>
public sealed class HeartbeatOptions
{
    /// <summary>
    /// How often the heartbeat fires. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The prompt injected as a user message each heartbeat tick.
    /// If <see langword="null"/> the default prompt is used.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Path to the HEARTBEAT.md file (or a directory containing one).
    /// When set the file contents are injected as a system message before the prompt.
    /// If <see langword="null"/> or the file doesn't exist the step is skipped.
    /// </summary>
    public string? HeartbeatFilePath { get; set; }

    /// <summary>
    /// Hour of day (0–23, inclusive) at which the quiet period begins.
    /// When <see langword="null"/> quiet hours are disabled.
    /// </summary>
    public int? QuietHoursStart { get; set; }

    /// <summary>
    /// Hour of day (0–23, inclusive) at which the quiet period ends.
    /// When <see langword="null"/> quiet hours are disabled.
    /// </summary>
    public int? QuietHoursEnd { get; set; }

    /// <summary>
    /// The response token the model emits when there is nothing to act on.
    /// Defaults to <c>"HEARTBEAT_OK"</c>.
    /// </summary>
    public string SilentToken { get; set; } = "HEARTBEAT_OK";

    /// <summary>
    /// Maximum length of a response (in characters) that is still eligible to be
    /// treated as a silent heartbeat containing only <see cref="SilentToken"/>.
    /// Defaults to 300.
    /// </summary>
    public int SilentTokenMaxChars { get; set; } = 300;

    // ── Internal helpers ──────────────────────────────────────────────────────

    internal const string DefaultPrompt =
        "Check HEARTBEAT.md if it exists for pending tasks. Follow it strictly. " +
        "Do not repeat tasks from prior conversations. " +
        "If nothing needs attention right now, reply with HEARTBEAT_OK and nothing else.";

    /// <summary>
    /// Returns the effective prompt, using <see cref="DefaultPrompt"/> when
    /// <see cref="Prompt"/> is <see langword="null"/> or whitespace.
    /// </summary>
    internal string EffectivePrompt =>
        string.IsNullOrWhiteSpace(Prompt) ? DefaultPrompt : Prompt;
}
