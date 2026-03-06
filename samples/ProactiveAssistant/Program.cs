using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Microsoft.Extensions.AI;

// ProactiveAssistant sample: demonstrates the heartbeat feature — an agent that
// wakes up on a schedule, checks HEARTBEAT.md for tasks, and acts proactively.
// Silent ticks (nothing to do) are pruned from history automatically.
//
// Environment variables:
//   OPENAI_API_KEY   - Required for OpenAI API
//   OPENAI_MODEL     - Optional, defaults to gpt-4o-mini
//   HEARTBEAT_FILE   - Optional path to HEARTBEAT.md (defaults to ./HEARTBEAT.md)
//   QUIET_START      - Optional quiet-hours start (0-23, e.g. "22" = 10 PM)
//   QUIET_END        - Optional quiet-hours end   (0-23, e.g. "8"  = 8 AM)

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Please set the OPENAI_API_KEY environment variable.");
    return;
}

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
var heartbeatFile = Environment.GetEnvironmentVariable("HEARTBEAT_FILE") ?? "./HEARTBEAT.md";

int? quietStart = null;
int? quietEnd = null;
if (int.TryParse(Environment.GetEnvironmentVariable("QUIET_START"), out var qs)) quietStart = qs;
if (int.TryParse(Environment.GetEnvironmentVariable("QUIET_END"), out var qe)) quietEnd = qe;

// Build the agent with a 30-second heartbeat interval for demo purposes.
// In production you would use TimeSpan.FromMinutes(5) or longer.
var assistant = new AgentBuilder()
    .WithChatClient(new OpenAI.Chat.ChatClient(model, apiKey).AsIChatClient())
    .WithHeartbeat(options =>
    {
        options.Interval = TimeSpan.FromSeconds(30);
        options.HeartbeatFilePath = heartbeatFile;
        options.QuietHoursStart = quietStart;
        options.QuietHoursEnd = quietEnd;
        // Override the silent token if desired:
        // options.SilentToken = "HEARTBEAT_OK";
    })
    .Build();

// Subscribe to heartbeat events to observe each tick.
assistant.Heartbeat!.Ticked += OnHeartbeatTicked;

await assistant.InitializeAsync();

// Start the background heartbeat timer.
await assistant.Heartbeat.StartAsync();

var quietLabel = quietStart.HasValue && quietEnd.HasValue
    ? $" | quiet hours: {quietStart:D2}:00 – {quietEnd:D2}:00"
    : "";

Console.WriteLine("== Proactive Assistant (Heartbeat Demo) ==");
Console.WriteLine($"Interval: 30s | Model: {model}{quietLabel}");
Console.WriteLine($"HEARTBEAT.md: {Path.GetFullPath(heartbeatFile)}");
Console.WriteLine("Type a message to chat, or press Enter to wait for the next tick.");
Console.WriteLine("Type 'exit' to quit.\n");

using var cts = new CancellationTokenSource();

// Run the interactive chat loop on a background task so heartbeat ticks
// interleave naturally with user input.
var chatTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        Console.Write("> ");
        var input = Console.ReadLine();

        if (input is null || string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        {
            cts.Cancel();
            break;
        }

        if (string.IsNullOrWhiteSpace(input))
            continue;

        var reply = await assistant.ReplyAsync(input, cts.Token);
        Console.WriteLine($"Assistant: {reply}\n");
    }
}, cts.Token);

try
{
    await chatTask;
}
catch (OperationCanceledException) { }

// Gracefully stop the heartbeat timer and dispose.
await assistant.Heartbeat.StopAsync();
if (assistant is IAsyncDisposable disposable)
    await disposable.DisposeAsync();

Console.WriteLine("Goodbye.");

// ── Event handler ─────────────────────────────────────────────────────────────

static void OnHeartbeatTicked(object? sender, HeartbeatResult result)
{
    var time = result.TickedAt.ToLocalTime().ToString("HH:mm:ss");
    var duration = result.Duration.TotalMilliseconds;

    if (result.Skipped)
    {
        Console.WriteLine($"\n[{time}] Heartbeat skipped ({result.SkipReason}) in {duration:F0}ms");
    }
    else if (result.Silent)
    {
        Console.WriteLine($"\n[{time}] Heartbeat silent (nothing to do) in {duration:F0}ms");
    }
    else
    {
        Console.WriteLine($"\n[{time}] Heartbeat active ({duration:F0}ms):");
        Console.WriteLine($"  {result.Response}");
    }

    Console.Write("> ");
}
