# Heartbeat

The heartbeat feature gives an agent a proactive, scheduled pulse — it wakes up on a configurable interval, checks a `HEARTBEAT.md` task file, and either acts on pending work or stays silent (and self-prunes its history entry so context is not polluted).

---

## Quick start

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithHeartbeat(TimeSpan.FromMinutes(5), "Check HEARTBEAT.md and act on any pending tasks.")
    .Build();

await agent.InitializeAsync();
await agent.Heartbeat!.StartAsync();
```

---

## How it works

On each tick the service:

1. **Checks skip guards** (in order):
   - **Quiet hours** — if the current wall-clock hour falls inside `QuietHoursStart`–`QuietHoursEnd`, the tick is dropped.
   - **In-flight** — a non-blocking semaphore ensures at most one tick runs at a time; overlapping ticks are dropped.
   - **Empty file** — if `HeartbeatFilePath` points to a file that exists but contains only blank lines or `#` headings, the tick is dropped without calling the model.

2. **Injects the task file** — if `HeartbeatFilePath` is set and the file has actionable content, its contents are prepended as a system message.

3. **Calls the model** with the configured prompt (defaults to the built-in prompt shown below).

4. **Detects silence** — if the response contains only the silent token (`HEARTBEAT_OK` by default) within `SilentTokenMaxChars` characters, the exchange is pruned from conversation history via `Agent.PruneLastExchange()` and the result is marked `Silent = true`.

5. **Fires `Ticked`** — the `IHeartbeatService.Ticked` event is raised with a `HeartbeatResult` regardless of outcome (skipped, silent, or active).

---

## HEARTBEAT.md

Create a `HEARTBEAT.md` file next to your agent's entry-point. Write tasks as a Markdown task list:

```markdown
# Heartbeat Tasks

## Pending

- [ ] Send the daily summary email
- [ ] Check the deployment pipeline status

## Notes

Remove a task once it has been actioned.
If there is nothing to do, reply with HEARTBEAT_OK.
```

The file is re-read on every tick, so you can edit it at runtime.

**Skip rules for the file:**

| File state | Behaviour |
|---|---|
| File does not exist | Tick proceeds (no file injection) |
| File exists, all lines blank or `#` headings | Tick is **skipped** (`EmptyFile`) |
| File exists, has actionable content | File contents injected as system message |

---

## Configuration

```csharp
.WithHeartbeat(options =>
{
    options.Interval           = TimeSpan.FromMinutes(5);   // default
    options.Prompt             = null;                       // uses built-in default
    options.HeartbeatFilePath  = "./HEARTBEAT.md";
    options.QuietHoursStart    = 22;   // 10 PM
    options.QuietHoursEnd      = 8;    //  8 AM (cross-midnight)
    options.SilentToken        = "HEARTBEAT_OK";            // default
    options.SilentTokenMaxChars = 300;                      // default
})
```

### `HeartbeatOptions` properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Interval` | `TimeSpan` | 5 min | How often the heartbeat fires. |
| `Prompt` | `string?` | `null` | User message injected on each tick. `null` → built-in default. |
| `HeartbeatFilePath` | `string?` | `null` | Path to `HEARTBEAT.md` (or directory containing one). |
| `QuietHoursStart` | `int?` | `null` | Hour (0–23) at which quiet period begins. `null` = disabled. |
| `QuietHoursEnd` | `int?` | `null` | Hour (0–23) at which quiet period ends. `null` = disabled. |
| `SilentToken` | `string` | `"HEARTBEAT_OK"` | Token the model emits when nothing needs attention. |
| `SilentTokenMaxChars` | `int` | `300` | Max response length eligible to be treated as silent. |

### Quiet hours

- `QuietHoursStart < QuietHoursEnd` → same-day range (e.g. 9–17 = office hours blackout)
- `QuietHoursStart > QuietHoursEnd` → cross-midnight range (e.g. 22–8 = overnight silence)
- `QuietHoursStart == QuietHoursEnd` → always quiet (full 24-hour blackout)

---

## Built-in default prompt

```
Check HEARTBEAT.md if it exists for pending tasks. Follow it strictly.
Do not repeat tasks from prior conversations.
If nothing needs attention right now, reply with HEARTBEAT_OK and nothing else.
```

---

## Observing ticks

Subscribe to `IHeartbeatService.Ticked`:

```csharp
agent.Heartbeat!.Ticked += (_, result) =>
{
    if (result.Skipped)
        Console.WriteLine($"Skipped: {result.SkipReason}");
    else if (result.Silent)
        Console.WriteLine("Silent tick — nothing to do.");
    else
        Console.WriteLine($"Active: {result.Response}");
};
```

### `HeartbeatResult` properties

| Property | Type | Description |
|---|---|---|
| `TickedAt` | `DateTimeOffset` | UTC timestamp when the tick started. |
| `Skipped` | `bool` | `true` if the model was not called. |
| `SkipReason` | `HeartbeatSkipReason` | Why the tick was skipped (`None` when not skipped). |
| `Silent` | `bool` | `true` if the model replied with only the silent token. |
| `Response` | `string?` | Raw model response, or `null` when skipped. |
| `Duration` | `TimeSpan` | Wall-clock time the tick took to complete. |

### `HeartbeatSkipReason` values

| Value | Meaning |
|---|---|
| `None` | Model was called — not skipped. |
| `QuietHours` | Current time is inside the quiet hours window. |
| `InFlight` | A previous tick is still executing. |
| `EmptyFile` | `HEARTBEAT.md` exists but has no actionable content. |

---

## Manual tick

Trigger an immediate tick outside the regular schedule:

```csharp
var result = await agent.Heartbeat!.TickAsync();
```

---

## Fluent builder overloads

```csharp
// No-arg: defaults only (5-min interval, no file, built-in prompt)
.WithHeartbeat()

// Interval + prompt
.WithHeartbeat(TimeSpan.FromMinutes(10), "Check for alerts.")

// Full HeartbeatOptions object
.WithHeartbeat(new HeartbeatOptions { Interval = TimeSpan.FromMinutes(1) })

// Configuration callback
.WithHeartbeat(opts => { opts.Interval = TimeSpan.FromSeconds(30); })
```

---

## Telemetry

Three OpenTelemetry instruments are emitted from `AgenticTelemetry`:

| Instrument | Kind | Description |
|---|---|---|
| `agentic.heartbeat` | Counter | Number of heartbeat ticks fired (tagged with `skip_reason`). |
| `agentic.heartbeat.duration` | Histogram | Duration of each tick in milliseconds. |
| `agentic.heartbeat.skip` | Counter | Number of ticks skipped (tagged with `skip_reason`). |

---

## Sample

See `samples/ProactiveAssistant/` for a runnable example that demonstrates:

- 30-second interval for quick demo feedback
- `HEARTBEAT.md` task file injection
- `Ticked` event subscription with console output
- Quiet hours via environment variables
- Interleaved user chat + background heartbeat ticks

```bash
cd samples/ProactiveAssistant
OPENAI_API_KEY=sk-... dotnet run
```
