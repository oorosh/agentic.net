# Agentic.NET

![Agentic.NET logo](logo.png)

Build AI agents in .NET with pluggable models, memory, middleware, tools, skills, and identity.

Agentic.NET is built on [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) (MEAI) abstractions. You bring any `IChatClient` — OpenAI, Azure OpenAI, Ollama, Anthropic, or your own — and the library wires conversation, memory, middleware, tools, skills, and identity around it.

The library exposes a minimal runtime with:

- `IChatClient` (MEAI) as the underlying LLM abstraction — bring any MEAI-compatible provider
- optional memory (`IMemoryService`) with built-in SQLite and in‑memory providers
- optional embeddings (`IEmbeddingGenerator<string, Embedding<float>>`, MEAI) with pluggable vector storage (`IVectorStore`)
- optional skills (`ISkillLoader`) from Agent Skills format
- optional identity (`ISoulLoader`) from SOUL.md format
- middleware hooks (`IAssistantMiddleware`) to preprocess or postprocess conversation
- a tool‑calling mechanism (`ITool`) that the model can invoke
- optional heartbeat (`IHeartbeatService`) for proactive, time-driven agent behavior

Designed for clarity and composability, the API lets your app stay in control while leveraging AI logic.

## Install

```bash
dotnet add package Agentic.NET
```

You also need a MEAI-compatible chat client. For OpenAI:

```bash
dotnet add package Microsoft.Extensions.AI.OpenAI
```

NuGet clients (Visual Studio, Rider, CLI) will pull the compiled library and dependencies automatically.

## Minimal usage

```csharp
using Agentic.Builder;
using Microsoft.Extensions.AI;
using OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY first.");

var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .Build();

var reply = await agent.ReplyAsync("Hello");
// reply.Content      → the response text (string)
// reply              → implicit cast to string; Console.WriteLine(reply) works too
// reply.Usage        → UsageInfo(PromptTokens, CompletionTokens, TotalTokens) — nullable
// reply.FinishReason → "stop", "length", etc. — nullable
// reply.ModelId      → model name echoed back by the provider — nullable
// reply.Duration     → end-to-end wall-clock time for this turn
Console.WriteLine(reply.Content);
```

## Streaming

Stream tokens incrementally as the model generates them using `StreamAsync`. Each `StreamingToken` carries a `Delta` (the incremental text chunk) and a final sentinel with `IsComplete = true`.

```csharp
await foreach (var token in agent.StreamAsync("Tell me a story"))
{
    if (!token.IsComplete)
    {
        Console.Write(token.Delta);   // print each chunk as it arrives
    }
    else
    {
        // final token — metadata is populated here
        Console.WriteLine();
        Console.WriteLine($"Finish reason : {token.FinishReason}");
        Console.WriteLine($"Tokens used   : {token.FinalUsage?.TotalTokens}");
    }
}
```

History is updated (and memory persisted) only after the stream is fully consumed — the same as `ReplyAsync`.

## Tool calling

Register tools so the model can invoke them during a conversation. Use `[ToolParameter]` to declare typed, validated parameters — the library auto-generates the JSON schema that the LLM receives, so it knows exactly what to pass.

```csharp
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Microsoft.Extensions.AI;
using OpenAI;

// Define a tool with typed parameters
public sealed class WeatherTool : ITool
{
    public string Name => "get_weather";
    public string Description => "Returns the current weather for a given city.";

    [ToolParameter("city", "City name to look up", required: true)]
    public string City { get; set; } = string.Empty;

    [ToolParameter("unit", "Temperature unit: 'celsius' or 'fahrenheit'")]
    public string Unit { get; set; } = "celsius";

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        // City and Unit are already populated by the framework before InvokeAsync is called.
        return Task.FromResult($"Weather in {City}: 22°{(Unit == "celsius" ? "C" : "F")}, sunny.");
    }
}

// Register with the builder — schema auto-generated, no manual JSON needed
var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithTool(new WeatherTool())
    .Build();

var reply = await agent.ReplyAsync("What's the weather in Paris?");
Console.WriteLine(reply.Content);   // use .Content for the text; implicit string cast also works
```

### Auto-discovering tools

Add `[AgenticTool]` to any `ITool` class and the builder will find and register it automatically — no manual `WithTool()` call needed.

```csharp
[AgenticTool]
public sealed class WeatherTool : ITool { ... }

[AgenticTool]
public sealed class CalcTool : ITool { ... }

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithToolsFromCallingAssembly()   // scans your assembly for [AgenticTool] classes
    .Build();
```

Other overloads:

```csharp
.WithToolsFromAssembly<WeatherTool>()          // scan WeatherTool's assembly
.WithToolsFromAssembly(Assembly.GetExecutingAssembly())  // explicit assembly
```

Manual `WithTool()` and auto-discovery can be freely mixed. The attribute also accepts optional `Name` and `Description` overrides if you want the LLM-facing values to differ from the `ITool` properties:

```csharp
[AgenticTool(Name = "get_weather_v2", Description = "Extended weather forecast.")]
public sealed class WeatherToolV2 : ITool { ... }
```

## MCP (Model Context Protocol)

Connect the agent to any [MCP](https://modelcontextprotocol.io/) server and its tools are automatically discovered and registered — no manual `ITool` implementation needed. This unlocks the entire MCP ecosystem: file systems, databases, browsers, APIs, and thousands of community servers.

### Connecting to an MCP server

**Stdio server** (most common — spawns a local process):

```csharp
using ModelContextProtocol.Client;

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithMcpServer(new StdioClientTransportOptions
    {
        Name = "filesystem",
        Command = "npx",
        Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "."]
    })
    .Build();

await agent.InitializeAsync();  // connects and discovers tools here
```

**HTTP / SSE server**:

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithMcpServer(new Uri("http://localhost:3001"))
    .Build();
```

**Pre-connected client** (you control the lifetime):

```csharp
await using var mcpClient = await McpClient.CreateAsync(transport);

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithMcpClient(mcpClient)   // agent uses it but does not dispose it
    .Build();
```

### How it works

- Tools are discovered lazily when `InitializeAsync()` is called — no blocking in the constructor.
- Each MCP tool is wrapped as an `ITool` and mixed in with any natively registered tools.
- The MCP tool's JSON schema is embedded in its description, so the LLM receives accurate parameter guidance.
- `McpClient` instances created from transports (via `WithMcpServer`) are owned by the agent and disposed with it.

### Multiple servers

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithMcpServer(stdioTransportOptions)              // file system tools
    .WithMcpServer(new Uri("http://localhost:3002"))   // web search tools
    .WithTool(new MyCustomTool())                      // native tool — freely mixed
    .Build();
```

### Sample

See `samples/McpServer` for a self-contained runnable example with an in-process demo server.

## Bring your own model

Agentic.NET accepts any `IChatClient` from `Microsoft.Extensions.AI`. Use any MEAI-compatible provider:

```csharp
// OpenAI
using Microsoft.Extensions.AI;
using OpenAI;
var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");

// Azure OpenAI
using Azure.AI.OpenAI;
var chatClient = new AzureOpenAIClient(endpoint, credential).AsChatClient("gpt-4o");

// Ollama (local)
using Microsoft.Extensions.AI;
var chatClient = new OllamaChatClient(new Uri("http://localhost:11434"), "llama3.2");

// Or implement IChatClient yourself for any other backend
```

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .Build();
```

## Heartbeat

Give your agent a timer-driven "heartbeat" so it can take initiative without waiting for user input — useful for reminders, background monitoring, or proactive status updates.

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithHeartbeat(interval: TimeSpan.FromMinutes(5))
    .Build();

await agent.InitializeAsync();

// Start the heartbeat loop
await agent.Heartbeat!.StartAsync();

// Each tick fires the Ticked event
agent.Heartbeat.Ticked += (_, result) =>
{
    if (!result.Skipped && !result.Silent)
        Console.WriteLine($"[Agent] {result.Response}");
};
```

Key options via `HeartbeatOptions`:
- `Interval` — how often to tick (default: 5 minutes)
- `SilentToken` — response prefix the model uses to indicate "nothing to say" (default: `"HEARTBEAT_OK"`)
- `SilentTokenMaxChars` — how many leading characters to check for the silent token (default: 300)

**See:** [Heartbeat guide](docs/heartbeat.md) | [ProactiveAssistant sample](samples/ProactiveAssistant/)

## Typical integration pattern

1. Choose a chat client: any `IChatClient` from `Microsoft.Extensions.AI` (OpenAI, Azure, Ollama, custom).
2. Build an `Agent` with `AgentBuilder`.
3. Optionally add memory with `WithMemory(...)`.
4. Optionally add skills with `WithSkills()` or `WithSkills(path)`.
5. Optionally add identity with `WithSoul()` or `WithSoul(path)`.
6. Optionally add middleware with `WithMiddleware(...)`.
7. Optionally register tools with `WithTool(...)`.
8. Optionally enable proactive behavior with `WithHeartbeat(...)`.
9. Call `ReplyAsync(...)` for a full response, or `StreamAsync(...)` to iterate tokens as they arrive.

## Key concepts

- `Agent`: runtime orchestrator for model, middleware, memory, skills, and tools.
- `AgentContext`: current input + history + mutable working messages.
- `AgentReply`: result of `ReplyAsync` — carries `Content` (text), `UserMessage`, `AssistantMessage`, `Usage` (token counts), `FinishReason`, `ModelId`, and `Duration`. Implicitly converts to `string`.
- `StreamingToken`: a single chunk from `StreamAsync` — `Delta` (incremental text), `IsComplete` (sentinel flag), `FinalUsage`, `FinishReason`, `ModelId` on the last chunk.
- `IAssistantMiddleware`: pipeline steps around model execution (e.g., memory injection, content safeguards); implement `StreamAsync` to participate in the streaming pipeline.
- `IMemoryService`: store/retrieve memory for context injection.
- `IEmbeddingGenerator<string, Embedding<float>>` (MEAI): generates embeddings for semantic memory search.
- `IVectorStore`: pluggable vector storage (pgvector, in-memory, etc.).
- `ISkillLoader`: loads agent skills from filesystem.
- `ISoulLoader`: loads agent identity from SOUL.md; supports dynamic personality updates with `ReloadSoulAsync()` and `UpdateSoulAsync()`.
- `IPersistentSoulLoader`: extension for read-write SOUL implementations enabling personality learning.
- `IHeartbeatService`: drives proactive, time-based agent ticks independently of user input.
- `HeartbeatOptions`: configures tick interval, silent token, and quiet-hours.
- `HeartbeatResult`: result of a single heartbeat tick — includes `TickedAt`, `Skipped`, `SkipReason`, `Silent`, `Response`, and `Duration`.
- `ITool`: executable function the model can request.
- `ToolParameterAttribute`: attribute-based parameter definition enabling type-safe tool arguments with automatic validation and JSON schema generation.
- `AgenticToolAttribute`: marks a tool class for automatic discovery via `WithToolsFromAssembly` / `WithToolsFromCallingAssembly`.

If memory is configured, `MemoryMiddleware` is added automatically unless you add your own memory middleware.

### Middleware Execution Order

Middlewares execute in the order they are registered, forming a pipeline where each middleware can process requests before and after the next layer. For detailed information about how middlewares work and their execution flow, see [Middleware Execution Order](docs/middleware-execution-order.md).

## Samples

Agentic.NET includes several runnable samples to demonstrate different features and integration patterns. Each sample includes a detailed README.md explaining what it demonstrates.

### Basic Chat (`samples/BasicChat`)

The simplest example showing how to create an agent with a custom chat client. Demonstrates:
- Basic `AgentBuilder` usage
- Implementing `IChatClient` for a custom echo model
- Interactive chat loop with console input/output
- No external dependencies required

```bash
dotnet run --project samples/BasicChat/BasicChat.csproj
```

### Memory and Middleware (`samples/MemoryAndMiddleware`)

Shows how to add memory and custom middleware to enhance agent behavior. Demonstrates:
- Memory services with `IMemoryService`
- Custom middleware implementation with `IAssistantMiddleware`
- Context factory for additional processing
- Optional semantic embeddings for better memory recall

```bash
# Without embeddings
dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj

# With embeddings (requires OPENAI_API_KEY)
USE_EMBEDDINGS=true OPENAI_API_KEY=your_key dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj
```

### Safeguard Middleware (`samples/SafeguardMiddleware`)

Demonstrates content moderation and guardrails through custom middleware. Shows:
- Prompt validation before LLM processing (pre-LLM safeguards)
- Response filtering after LLM generation (post-LLM safeguards)
- Middleware chaining and short-circuit responses
- Basic keyword-based content moderation

```bash
dotnet run --project samples/SafeguardMiddleware/SafeguardMiddleware.csproj
```

### Personal Assistant (`samples/PersonalAssistant`)

An example of a persistent agent use case: OpenAI-backed agent with SQLite conversation memory and optional semantic embeddings. Features:
- SQLite-based persistent conversation storage
- Optional semantic embeddings for enhanced recall
- Memory restoration across application restarts
- Full OpenAI Chat Completion API integration

```bash
# Without embeddings
OPENAI_API_KEY=your_key dotnet run --project samples/PersonalAssistant/PersonalAssistant.csproj

# With embeddings
USE_EMBEDDINGS=true OPENAI_API_KEY=your_key dotnet run --project samples/PersonalAssistant/PersonalAssistant.csproj
```

### Dynamic SOUL (`samples/DynamicSOUL`)

Demonstrates dynamic personality learning where agents adapt their SOUL.md identity during conversations. Features:
- Load agent personality from SOUL.md
- Update personality based on conversation feedback  
- Persist personality changes back to disk
- Implement custom ISoulLoader for any data source (database, API, cloud storage, etc.)

```bash
# Run the main demo - file-based SOUL
OPENAI_API_KEY=your_key dotnet run --project samples/DynamicSOUL/DynamicSOUL.csproj
```

**See:** [Dynamic SOUL README](samples/DynamicSOUL/README.md) and [Custom Loaders Guide](samples/DynamicSOUL/README_CUSTOM.md)

### Structured Tools (`samples/StructuredTools`)

Demonstrates the type-safe structured tool parameters feature with automatic JSON schema generation and validation. Shows:
- Defining tool parameters with `ToolParameterAttribute` for type-safe constraints
- Automatic parsing and validation of tool arguments
- Numeric constraints (min/max values, ranges)
- String validation (length, pattern, enum values)
- JSON schema generation for LLM understanding
- Type-safe parameter binding eliminating manual JSON parsing
- Tool examples: calculator, hotel search, hotel booking

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/StructuredTools/StructuredTools.csproj
```

**See:** [Structured Tools README](samples/StructuredTools/README.md)

### Proactive Assistant (`samples/ProactiveAssistant`)

Demonstrates the heartbeat feature — the agent wakes up on a timer and generates proactive messages without any user input. Features:
- Time-driven ticks via `IHeartbeatService`
- Configurable interval and quiet-hours via `HeartbeatOptions`
- Silent-token suppression (no output when the model has nothing useful to say)
- Skippable ticks when a user interaction is already in flight

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/ProactiveAssistant/ProactiveAssistant.csproj
```

**See:** [Heartbeat guide](docs/heartbeat.md)

### MCP Server (`samples/McpServer`)

Demonstrates connecting an agent to an MCP server. Uses an in-process demo server (no external process needed) with three example tools: `echo`, `celsius_to_fahrenheit`, and `word_count`. Includes commented-out snippets showing real-world stdio and HTTP connection patterns.

```bash
dotnet run --project samples/McpServer/McpServer.csproj
```

### Middleware Examples

Agentic.NET includes several focused middleware samples demonstrating different patterns and use cases:

#### LoggingMiddleware (`samples/LoggingMiddleware`)

Demonstrates request/response logging for debugging and audit trails. Shows:
- Request logging with timestamp and history size
- Response logging with duration tracking
- Formatted console output for visibility

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/LoggingMiddleware/LoggingMiddleware.csproj
```

**See:** [LoggingMiddleware README](samples/LoggingMiddleware/README.md)

#### RateLimitingMiddleware (`samples/RateLimitingMiddleware`)

Demonstrates rate limiting using token bucket algorithm. Shows:
- Per-client rate limiting
- Automatic token refill
- Preventing abuse and controlling costs

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/RateLimitingMiddleware/RateLimitingMiddleware.csproj
```

**See:** [RateLimitingMiddleware README](samples/RateLimitingMiddleware/README.md)

#### AuthenticationMiddleware (`samples/AuthenticationMiddleware`)

Demonstrates authentication and role-based authorization. Shows:
- API key validation
- User identification
- Role-based access control (RBAC)

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/AuthenticationMiddleware/AuthenticationMiddleware.csproj
```

**See:** [AuthenticationMiddleware README](samples/AuthenticationMiddleware/README.md)

#### CachingMiddleware (`samples/CachingMiddleware`)

Demonstrates response caching for improved performance and reduced costs. Shows:
- Cache hits and misses
- TTL-based cache expiration
- Significant cost savings for repeated queries

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/CachingMiddleware/CachingMiddleware.csproj
```

**See:** [CachingMiddleware README](samples/CachingMiddleware/README.md)

#### ErrorHandlingMiddleware (`samples/ErrorHandlingMiddleware`)

Demonstrates error recovery and resilience patterns. Shows:
- Automatic retry with exponential backoff
- Request timeout handling
- Graceful error messages

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/ErrorHandlingMiddleware/ErrorHandlingMiddleware.csproj
```

**See:** [ErrorHandlingMiddleware README](samples/ErrorHandlingMiddleware/README.md)

#### ResponseValidationMiddleware (`samples/ResponseValidationMiddleware`)

Demonstrates LLM output quality validation and retry logic. Shows:
- Validates responses AFTER LLM generation (post-processing)
- Detects common "stupid" response patterns
- Automatic retry up to 2 times on validation failure
- Fallback responses when validation repeatedly fails
- Guards against incomplete, hallucinated, or nonsensical responses

```bash
OPENAI_API_KEY=your_key dotnet run --project samples/ResponseValidationMiddleware/ResponseValidationMiddleware.csproj
```

**See:** [ResponseValidationMiddleware README](samples/ResponseValidationMiddleware/README.md)

**See:** [Response Validation Patterns Guide](docs/response-validation-patterns.md)

## OpenTelemetry

Agentic.NET emits distributed traces (spans) and metrics out of the box using the standard `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter` APIs.
No extra configuration is required inside the library — instrumentation is **zero-cost when no listener is registered**.

### Wire up in your host

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Agentic.Core; // AgenticTelemetry

var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(AgenticTelemetry.ActivitySourceName)
    .AddConsoleExporter()          // or AddOtlpExporter(), AddZipkinExporter(), etc.
    .Build();

var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter(AgenticTelemetry.MeterName)
    .AddConsoleExporter()
    .Build();
```

Or with ASP.NET Core / `IServiceCollection`:

```csharp
services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource(AgenticTelemetry.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(b => b
        .AddMeter(AgenticTelemetry.MeterName)
        .AddOtlpExporter());
```

### Span hierarchy

```
agent.reply                          ← one per ReplyAsync call
  └─ memory.retrieval                ← MemoryMiddleware (if configured)
  └─ llm.complete                    ← first LLM call
  └─ tool.call {agentic.tool.name}   ← per tool invocation (0..N)
  └─ llm.complete                    ← subsequent LLM calls in the tool loop
```

### Span tags (Gen AI semantic conventions)

| Tag | Span | Description |
|---|---|---|
| `gen_ai.operation.name` | `llm.complete` | Always `chat` |
| `gen_ai.request.model` | `llm.complete` | Model name (e.g. `gpt-4o-mini`) |
| `gen_ai.usage.input_tokens` | `llm.complete` | Prompt token count |
| `gen_ai.usage.output_tokens` | `llm.complete` | Completion token count |
| `agentic.history.length` | `agent.reply` | Chat history length at call start |
| `agentic.tool.name` | `tool.call` | Name of the invoked tool |
| `agentic.tool.success` | `tool.call` | `true` / `false` |
| `agentic.memory.mode` | `memory.retrieval` | `semantic` or `keyword` |
| `agentic.memory.items` | `memory.retrieval` | Number of items returned |

### Metrics

| Metric | Type | Unit | Description |
|---|---|---|---|
| `agentic.reply.count` | Counter | `{reply}` | Total `ReplyAsync` completions |
| `agentic.reply.duration` | Histogram | `ms` | End-to-end `ReplyAsync` latency |
| `agentic.llm.call.count` | Counter | `{call}` | LLM `CompleteAsync` call count |
| `agentic.llm.prompt_tokens` | Counter | `{token}` | Prompt tokens consumed |
| `agentic.llm.completion_tokens` | Counter | `{token}` | Completion tokens generated |
| `agentic.tool.call.count` | Counter | `{call}` | Tool invocation count |
| `agentic.tool.call.duration` | Histogram | `ms` | Per-tool execution latency |
| `agentic.memory.retrieval.count` | Counter | `{retrieval}` | Memory retrieval operations |
| `agentic.memory.retrieval.items` | Histogram | `{item}` | Items returned per retrieval |
| `agentic.heartbeat` | Counter | `{tick}` | Heartbeat ticks fired |
| `agentic.heartbeat.duration` | Histogram | `ms` | Duration of each heartbeat tick |
| `agentic.heartbeat.skip` | Counter | `{skip}` | Heartbeat ticks skipped (with `agentic.heartbeat.skip.reason` tag) |

## Environment Variables

Samples that use OpenAI require the following environment variables:

- `OPENAI_API_KEY`: Your OpenAI API key (required for OpenAI samples)
- `OPENAI_MODEL`: Model to use (optional, defaults to `gpt-4o-mini`)
- `USE_EMBEDDINGS`: Set to `true` to enable semantic embeddings (optional)
- `USE_PGVECTOR`: Set to `true` to use PostgreSQL pgvector (optional)
- `PGVECTOR_CONNECTION_STRING`: PostgreSQL connection string (required when USE_PGVECTOR=true)

## Namespace reference

| What you need | `using` directive |
|---|---|
| `AgentBuilder` | `using Agentic.Builder;` |
| `ITool`, `ToolParameterAttribute`, `IMemoryService`, `IHeartbeatService` | `using Agentic.Abstractions;` |
| `ChatMessage`, `ChatRole`, `AgentReply`, `AgentResponse`, `StreamingToken`, `UsageInfo`, `SqliteMemoryService`, `HeartbeatOptions`, `HeartbeatResult`, `HeartbeatSkipReason` | `using Agentic.Core;` |
| `IAssistantMiddleware`, `AgentContext`, `AgentHandler`, `AgentStreamingHandler` | `using Agentic.Middleware;` |
| `InMemoryVectorStore`, `PgVectorStore` | `using Agentic.Stores;` |
| `IChatClient`, `IEmbeddingGenerator`, `ChatMessage` (MEAI), `Embedding<float>` | `using Microsoft.Extensions.AI;` |

Most applications only need `Agentic.Builder` and `Microsoft.Extensions.AI` (plus the MEAI provider package). Tools additionally need `Agentic.Abstractions` and `Agentic.Core`.

## Roadmap

The following capabilities are planned for upcoming releases, roughly in priority order:

### 🤝 Multi-agent orchestration
Runtime support for agent handoffs defined in SOUL.md. One agent delegates work to another and receives results back — the foundation for building systems like a CLI coding assistant backed by specialist sub-agents.

### 📄 RAG / document ingestion pipeline
A document ingestion API (chunking, embedding, indexing) so agents can answer questions against external knowledge bases — PDFs, web pages, code repos — not just conversation history.

### 💬 Conversation management
Automatic context-window management: configurable summarization or sliding-window pruning so long-running chat bots never silently hit token limits.

### 🗄️ SQLite-vec vector store
A built-in `SqliteVecVectorStore` so semantic memory works with zero extra infrastructure — no Postgres required. Natural companion to the existing SQLite memory service.

### 🧱 Structured output
A `WithStructuredOutput<T>()` builder method that instructs the model to return JSON matching a schema and deserializes it automatically into a typed result.

### 🔁 Resilience middleware
Built-in retry and rate-limiting middleware for transient LLM errors, with configurable back-off policies.

### 🖼️ Multimodal input
Pass images and other content types through to MEAI providers that support them (vision models, document understanding).

## Repository layout

- `src/Agentic.NET/` — library source (Abstractions, Builder, Core, Loaders, Middleware, Stores)
- `src/Agentic.Tests/` — unit tests
- `samples/` — runnable usage examples

## Contributing

Contributions are welcome! Here's how to get started:

1. **Fork** the repository and clone your fork locally.
2. **Create a branch** for your change: `git checkout -b my-feature`.
3. **Make your changes** — follow the code style described in [`AGENTS.md`](AGENTS.md) (file-scoped namespaces, `sealed` classes by default, nullable enabled, etc.).
4. **Add or update tests** in `src/Agentic.Tests/` for any new behaviour.
5. **Build and test** to make sure everything passes:
   ```bash
   dotnet build src/Agentic.NET/Agentic.NET.csproj
   dotnet test src/Agentic.Tests/Agentic.Tests.csproj -c Release
   ```
6. **Open a pull request** against `main` with a clear description of what you changed and why.

### Good first contributions
- Adding a new `IVectorStore` implementation (Azure AI Search, Qdrant, Pinecone, …)
- Adding a new sample under `samples/`
- Picking up an item from the [Roadmap](#roadmap)
- Improving documentation or fixing typos

Please open an issue first if you plan a large or breaking change — it helps avoid duplicated effort.