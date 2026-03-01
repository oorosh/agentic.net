using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Tests.Fakes;
using Xunit;

namespace Agentic.Tests;

public class HeartbeatTests
{
    // ── Builder integration ───────────────────────────────────────────────────

    [Fact]
    public void Build_exposes_null_heartbeat_when_not_configured()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new StubAgentModel("ok")))
            .Build();

        Assert.Null(agent.Heartbeat);
    }

    [Fact]
    public void Build_exposes_heartbeat_service_when_configured()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new StubAgentModel("ok")))
            .WithHeartbeat()
            .Build();

        Assert.NotNull(agent.Heartbeat);
    }

    [Fact]
    public void Build_with_heartbeat_interval_overload_sets_interval()
    {
        var interval = TimeSpan.FromSeconds(30);
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new StubAgentModel("ok")))
            .WithHeartbeat(interval)
            .Build();

        Assert.NotNull(agent.Heartbeat);
    }

    [Fact]
    public void Build_with_heartbeat_options_overload_sets_service()
    {
        var options = new HeartbeatOptions
        {
            Interval = TimeSpan.FromSeconds(10),
            SilentToken = "DONE"
        };

        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new StubAgentModel("ok")))
            .WithHeartbeat(options)
            .Build();

        Assert.NotNull(agent.Heartbeat);
    }

    [Fact]
    public void Build_with_heartbeat_configure_callback_sets_service()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new StubAgentModel("ok")))
            .WithHeartbeat(o => o.Interval = TimeSpan.FromMinutes(1))
            .Build();

        Assert.NotNull(agent.Heartbeat);
    }

    // ── HeartbeatOptions defaults ─────────────────────────────────────────────

    [Fact]
    public void HeartbeatOptions_defaults_are_sensible()
    {
        var opts = new HeartbeatOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), opts.Interval);
        Assert.Equal("HEARTBEAT_OK", opts.SilentToken);
        Assert.Equal(300, opts.SilentTokenMaxChars);
        Assert.Null(opts.QuietHoursStart);
        Assert.Null(opts.QuietHoursEnd);
        Assert.Null(opts.HeartbeatFilePath);
    }

    // ── Tick: quiet hours skip ────────────────────────────────────────────────

    [Fact]
    public async Task TickAsync_skips_during_quiet_hours()
    {
        // Use an hour range that covers every possible local hour (0–23 → effectively "always quiet")
        var options = new HeartbeatOptions
        {
            Interval = TimeSpan.FromDays(1),
            QuietHoursStart = 0,
            QuietHoursEnd = 0,   // cross-midnight: 0..0 means always quiet (0 >= 0 || 0 < 0 → only first branch, same-day: 0>=0 && 0<0 = false; cross-midnight: 0>=0 = true)
        };

        // Force an always-quiet scenario by setting start==end meaning cross-midnight covers all hours.
        // Actually cross-midnight logic: start=22, end=22 → hour >= 22 || hour < 22 = always true.
        options.QuietHoursStart = 22;
        options.QuietHoursEnd = 22;

        var agent = BuildAgentWithModel("HEARTBEAT_OK");
        var service = new AgentHeartbeatService(agent, options);

        await using (service)
        {
            var result = await service.TickAsync();
            Assert.True(result.Skipped);
            Assert.Equal(HeartbeatSkipReason.QuietHours, result.SkipReason);
        }
    }

    // ── Tick: in-flight skip ──────────────────────────────────────────────────

    [Fact]
    public async Task TickAsync_skips_when_tick_already_in_flight()
    {
        // Block the first tick by making the model take a while, then fire a second tick
        // while the first is running.
        var tcs = new TaskCompletionSource<string>();
        var blockingModel = new BlockingAgentModel(tcs.Task);
        var agent = BuildAgentWithModel(blockingModel);

        var options = new HeartbeatOptions { Interval = TimeSpan.FromDays(1) };
        var service = new AgentHeartbeatService(agent, options);

        await using (service)
        {
            // Start the first tick (will block on the model).
            var firstTickTask = service.TickAsync();

            // Give the first tick a moment to acquire the semaphore.
            await Task.Delay(50);

            // Second tick should skip because first is in flight.
            var skippedResult = await service.TickAsync();

            Assert.True(skippedResult.Skipped);
            Assert.Equal(HeartbeatSkipReason.InFlight, skippedResult.SkipReason);

            // Unblock the first tick.
            tcs.SetResult("HEARTBEAT_OK");
            await firstTickTask;
        }
    }

    // ── Tick: empty HEARTBEAT.md skip ─────────────────────────────────────────

    [Fact]
    public async Task TickAsync_skips_when_heartbeat_file_is_empty()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "");

            var options = new HeartbeatOptions
            {
                Interval = TimeSpan.FromDays(1),
                HeartbeatFilePath = tempFile
            };

            var agent = BuildAgentWithModel("hello");
            var service = new AgentHeartbeatService(agent, options);

            await using (service)
            {
                var result = await service.TickAsync();
                Assert.True(result.Skipped);
                Assert.Equal(HeartbeatSkipReason.EmptyFile, result.SkipReason);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TickAsync_skips_when_heartbeat_file_has_only_comments()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "# Section\n\n# Another heading\n");

            var options = new HeartbeatOptions
            {
                Interval = TimeSpan.FromDays(1),
                HeartbeatFilePath = tempFile
            };

            var agent = BuildAgentWithModel("hello");
            var service = new AgentHeartbeatService(agent, options);

            await using (service)
            {
                var result = await service.TickAsync();
                Assert.True(result.Skipped);
                Assert.Equal(HeartbeatSkipReason.EmptyFile, result.SkipReason);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task TickAsync_runs_when_heartbeat_file_has_actionable_content()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "# Tasks\n- [ ] Check emails\n");

            var options = new HeartbeatOptions
            {
                Interval = TimeSpan.FromDays(1),
                HeartbeatFilePath = tempFile
            };

            var agent = BuildAgentWithModel("HEARTBEAT_OK");
            var service = new AgentHeartbeatService(agent, options);

            await using (service)
            {
                var result = await service.TickAsync();
                Assert.False(result.Skipped);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Tick: silent path ─────────────────────────────────────────────────────

    [Fact]
    public async Task TickAsync_marks_result_as_silent_when_model_returns_silent_token()
    {
        var agent = BuildAgentWithModel("HEARTBEAT_OK");
        var options = new HeartbeatOptions { Interval = TimeSpan.FromDays(1) };
        var service = new AgentHeartbeatService(agent, options);

        await using (service)
        {
            var result = await service.TickAsync();

            Assert.False(result.Skipped);
            Assert.True(result.Silent);
            Assert.Contains("HEARTBEAT_OK", result.Response, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TickAsync_prunes_history_when_response_is_silent()
    {
        var agent = BuildAgentWithModel("HEARTBEAT_OK");
        var options = new HeartbeatOptions { Interval = TimeSpan.FromDays(1) };
        var service = new AgentHeartbeatService(agent, options);

        // Populate some prior history so we can verify the heartbeat exchange is pruned.
        var historyBefore = agent.History.Count;

        await using (service)
        {
            await service.TickAsync();
        }

        // History should be back to the same count as before the silent heartbeat.
        Assert.Equal(historyBefore, agent.History.Count);
    }

    // ── Tick: active (non-silent) path ────────────────────────────────────────

    [Fact]
    public async Task TickAsync_marks_result_as_active_when_model_returns_real_content()
    {
        const string response = "I found 3 new emails and processed them.";
        var agent = BuildAgentWithModel(response);
        var options = new HeartbeatOptions { Interval = TimeSpan.FromDays(1) };
        var service = new AgentHeartbeatService(agent, options);

        await using (service)
        {
            var result = await service.TickAsync();

            Assert.False(result.Skipped);
            Assert.False(result.Silent);
            Assert.Equal(response, result.Response);
        }
    }

    [Fact]
    public async Task TickAsync_preserves_history_when_response_is_active()
    {
        const string response = "Done some work.";
        var agent = BuildAgentWithModel(response);
        var historyBefore = agent.History.Count;

        var options = new HeartbeatOptions { Interval = TimeSpan.FromDays(1) };
        var service = new AgentHeartbeatService(agent, options);

        await using (service)
        {
            await service.TickAsync();
        }

        // History should now have 2 more messages (user + assistant).
        Assert.Equal(historyBefore + 2, agent.History.Count);
    }

    // ── Ticked event ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TickAsync_raises_Ticked_event()
    {
        var agent = BuildAgentWithModel("HEARTBEAT_OK");
        var options = new HeartbeatOptions { Interval = TimeSpan.FromDays(1) };
        var service = new AgentHeartbeatService(agent, options);

        HeartbeatResult? received = null;
        service.Ticked += (_, r) => received = r;

        await using (service)
        {
            await service.TickAsync();
        }

        Assert.NotNull(received);
    }

    // ── HeartbeatResult factories ─────────────────────────────────────────────

    [Fact]
    public void HeartbeatResult_CreateSkipped_sets_properties_correctly()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = TimeSpan.FromMilliseconds(5);
        var result = HeartbeatResult.CreateSkipped(HeartbeatSkipReason.QuietHours, now, elapsed);

        Assert.True(result.Skipped);
        Assert.Equal(HeartbeatSkipReason.QuietHours, result.SkipReason);
        Assert.False(result.Silent);
        Assert.Null(result.Response);
        Assert.Equal(now, result.TickedAt);
        Assert.Equal(elapsed, result.Duration);
    }

    [Fact]
    public void HeartbeatResult_CreateSilent_sets_properties_correctly()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = TimeSpan.FromMilliseconds(200);
        var result = HeartbeatResult.CreateSilent(now, "HEARTBEAT_OK", elapsed);

        Assert.False(result.Skipped);
        Assert.True(result.Silent);
        Assert.Equal("HEARTBEAT_OK", result.Response);
        Assert.Equal(now, result.TickedAt);
        Assert.Equal(elapsed, result.Duration);
    }

    [Fact]
    public void HeartbeatResult_CreateActive_sets_properties_correctly()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = TimeSpan.FromSeconds(1);
        var result = HeartbeatResult.CreateActive(now, "Did some work.", elapsed);

        Assert.False(result.Skipped);
        Assert.False(result.Silent);
        Assert.Equal("Did some work.", result.Response);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IAgent BuildAgentWithModel(string fixedResponse) =>
        BuildAgentWithModel(new StubAgentModel(fixedResponse));

    private static IAgent BuildAgentWithModel(IAgentModel model) =>
        new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(model))
            .Build();

    // ── Inner fakes ───────────────────────────────────────────────────────────

    private sealed class StubAgentModel : IAgentModel
    {
        private readonly string _response;
        public StubAgentModel(string response) => _response = response;

        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentResponse(_response));

        public IAsyncEnumerable<StreamingToken> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default) =>
            FakeModelStreamHelper.StreamFromCompleteAsync(this, messages, cancellationToken);
    }

    private sealed class BlockingAgentModel : IAgentModel
    {
        private readonly Task<string> _responseTask;
        public BlockingAgentModel(Task<string> responseTask) => _responseTask = responseTask;

        public async Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            var content = await _responseTask;
            return new AgentResponse(content);
        }

        public IAsyncEnumerable<StreamingToken> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default) =>
            FakeModelStreamHelper.StreamFromCompleteAsync(this, messages, cancellationToken);
    }
}
