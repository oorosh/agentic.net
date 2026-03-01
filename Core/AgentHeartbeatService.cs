using System.Diagnostics;
using Agentic.Abstractions;

namespace Agentic.Core;

/// <summary>
/// Default implementation of <see cref="IHeartbeatService"/>.
/// Uses a <see cref="PeriodicTimer"/> to fire ticks on a fixed interval and guards
/// against overlapping ticks with a <see cref="SemaphoreSlim"/>(1,1).
/// </summary>
public sealed class AgentHeartbeatService : IHeartbeatService
{
    private readonly IAgent _agent;
    private readonly HeartbeatOptions _options;
    private PeriodicTimer? _timer;
    private Task? _timerLoop;
    private CancellationTokenSource? _cts;

    // Guards against overlapping ticks — WaitAsync(0) is used so in-flight skips are non-blocking.
    private readonly SemaphoreSlim _tickLock = new(1, 1);

    public AgentHeartbeatService(IAgent agent, HeartbeatOptions options)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public event EventHandler<HeartbeatResult>? Ticked;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_timerLoop is not null)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new PeriodicTimer(_options.Interval);
        _timerLoop = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is null)
            return;

        await _cts.CancelAsync();
        _timer?.Dispose();
        _timer = null;

        if (_timerLoop is not null)
        {
            try { await _timerLoop.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
            _timerLoop = null;
        }

        _cts.Dispose();
        _cts = null;
    }

    /// <inheritdoc/>
    public async Task<HeartbeatResult> TickAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteTickAsync(cancellationToken);
        Ticked?.Invoke(this, result);
        return result;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (_timer is null)
            return;

        try
        {
            while (await _timer.WaitForNextTickAsync(ct))
            {
                var result = await ExecuteTickAsync(ct);
                Ticked?.Invoke(this, result);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — swallow.
        }
    }

    private async Task<HeartbeatResult> ExecuteTickAsync(CancellationToken cancellationToken)
    {
        var tickedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        // ── Guard: quiet hours ────────────────────────────────────────────────
        if (IsQuietHours(tickedAt))
        {
            sw.Stop();
            AgenticTelemetry.HeartbeatSkipCounter.Add(1,
                new KeyValuePair<string, object?>(AgenticTelemetry.Tags.HeartbeatSkipReason, nameof(HeartbeatSkipReason.QuietHours)));
            return HeartbeatResult.CreateSkipped(HeartbeatSkipReason.QuietHours, tickedAt, sw.Elapsed);
        }

        // ── Guard: in-flight ──────────────────────────────────────────────────
        if (!await _tickLock.WaitAsync(0, cancellationToken))
        {
            sw.Stop();
            AgenticTelemetry.HeartbeatSkipCounter.Add(1,
                new KeyValuePair<string, object?>(AgenticTelemetry.Tags.HeartbeatSkipReason, nameof(HeartbeatSkipReason.InFlight)));
            return HeartbeatResult.CreateSkipped(HeartbeatSkipReason.InFlight, tickedAt, sw.Elapsed);
        }

        try
        {
            // ── Guard: HEARTBEAT.md empty ─────────────────────────────────────
            var heartbeatFileContent = await ReadHeartbeatFileAsync(cancellationToken);
            if (heartbeatFileContent is not null && !HasActionableContent(heartbeatFileContent))
            {
                sw.Stop();
                AgenticTelemetry.HeartbeatSkipCounter.Add(1,
                    new KeyValuePair<string, object?>(AgenticTelemetry.Tags.HeartbeatSkipReason, nameof(HeartbeatSkipReason.EmptyFile)));
                return HeartbeatResult.CreateSkipped(HeartbeatSkipReason.EmptyFile, tickedAt, sw.Elapsed);
            }

            // ── Build prompt ──────────────────────────────────────────────────
            var prompt = BuildPrompt(heartbeatFileContent, tickedAt);

            // ── Call the agent ────────────────────────────────────────────────
            var reply = await _agent.ReplyAsync(prompt, cancellationToken);
            sw.Stop();

            AgenticTelemetry.HeartbeatCounter.Add(1);
            AgenticTelemetry.HeartbeatDuration.Record(sw.Elapsed.TotalMilliseconds);

            var response = reply.Content;

            // ── Detect silent token ───────────────────────────────────────────
            if (IsSilentResponse(response))
            {
                // Prune the heartbeat exchange from history so it doesn't pollute context.
                if (_agent is Agent concreteAgent)
                    concreteAgent.PruneLastExchange();

                return HeartbeatResult.CreateSilent(tickedAt, response, sw.Elapsed);
            }

            return HeartbeatResult.CreateActive(tickedAt, response, sw.Elapsed);
        }
        finally
        {
            _tickLock.Release();
        }
    }

    private bool IsQuietHours(DateTimeOffset now)
    {
        if (_options.QuietHoursStart is not { } start || _options.QuietHoursEnd is not { } end)
            return false;

        var hour = now.LocalDateTime.Hour;

        // start == end means "always quiet" (full 24-hour blackout).
        if (start == end)
            return true;

        // Handles both same-day (e.g. 8–18) and cross-midnight (e.g. 22–6) ranges.
        if (start < end)
            return hour >= start && hour < end;

        // Cross-midnight: quiet from start until midnight OR from midnight until end.
        return hour >= start || hour < end;
    }

    private async Task<string?> ReadHeartbeatFileAsync(CancellationToken cancellationToken)
    {
        if (_options.HeartbeatFilePath is null)
            return null;

        var path = _options.HeartbeatFilePath;

        // Allow passing a directory — look for HEARTBEAT.md inside it.
        if (Directory.Exists(path))
            path = Path.Combine(path, "HEARTBEAT.md");

        if (!File.Exists(path))
            return null;

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <summary>
    /// Returns <see langword="false"/> when the file content contains only blank lines or
    /// lines starting with <c>#</c> (markdown comments / headings with no body).
    /// </summary>
    private static bool HasActionableContent(string content)
    {
        foreach (var line in content.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed[0] != '#')
                return true;
        }
        return false;
    }

    private string BuildPrompt(string? heartbeatFileContent, DateTimeOffset now)
    {
        var sb = new System.Text.StringBuilder();

        if (heartbeatFileContent is not null)
        {
            sb.AppendLine("HEARTBEAT.md:");
            sb.AppendLine("```");
            sb.AppendLine(heartbeatFileContent);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.Append(_options.EffectivePrompt);
        sb.AppendLine();
        sb.Append($"Current time: {now.LocalDateTime:dddd, MMMM d, yyyy — HH:mm} UTC");

        return sb.ToString();
    }

    private bool IsSilentResponse(string response)
    {
        if (response.Length > _options.SilentTokenMaxChars)
            return false;

        return response.Contains(_options.SilentToken, StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _tickLock.Dispose();
    }
}
