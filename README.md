# Agentic.NET

![Agentic.NET logo](logo.png)

Create AI assistants in .NET with pluggable models, memory, middleware, tools, skills, and identity.

The library exposes a minimal runtime with:

- `IAgentModel` abstraction for the underlying LLM or chat model
- optional memory (`IMemoryService`) with built-in SQLite and in‑memory providers
- optional embeddings (`IEmbeddingProvider`) with pluggable vector storage (`IVectorStore`)
- optional skills (`ISkillLoader`) from Agent Skills format
- optional identity (`ISoulLoader`) from SOUL.md format
- middleware hooks (`IAssistantMiddleware`) to preprocess or postprocess conversation
- a tool‑calling mechanism (`ITool`) that the model can invoke

Designed for clarity and composability, the API lets your app stay in control while leverage AI logic.

## Install

### NuGet (recommended for application developers)

The package is published on nuget.org and can be added by running:

```bash
dotnet add package Agentic.NET
```

NuGet clients (Visual Studio, Rider, CLI) will pull the compiled library and dependencies automatically.

## Minimal usage

```csharp
using Agentic.Builder;
using Agentic.Providers.OpenAi;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY first.");

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? OpenAiModels.Gpt4oMini;

var agent = new AgentBuilder()
    .WithOpenAi(apiKey, model)
    .Build();

var reply = await agent.ReplyAsync("Hello");
// reply.Content  → the response text (string)
// reply          → implicit cast to string; Console.WriteLine(reply) works too
Console.WriteLine(reply.Content);
```

## Tool calling

Register tools so the model can invoke them during a conversation. Use `[ToolParameter]` to declare typed, validated parameters — the library auto-generates the JSON schema that the LLM receives, so it knows exactly what to pass.

```csharp
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;

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
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
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
    .WithOpenAi(apiKey)
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

## Custom model provider (optional)

If you need a non-OpenAI backend, implement your own provider:

```csharp
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

public sealed class DemoModelProvider : IModelProvider
{
    public IAgentModel CreateModel() => new DemoModel();
}

public sealed class DemoModel : IAgentModel
{
    public Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var lastUser = messages.Last(m => m.Role == ChatRole.User).Content;
        return Task.FromResult(new AgentResponse($"Echo: {lastUser}"));
    }
}
```
## Typical integration pattern

1. Choose a model setup:
    - built-in OpenAI via `WithOpenAi(...)`, or
    - custom provider via `WithModelProvider(...)`.
2. Build an `Agent` with `AgentBuilder`.
3. Optionally add memory with `WithMemory(...)`.
4. Optionally add skills with `WithSkills()` or `WithSkills(path)`.
5. Optionally add identity with `WithSoul()` or `WithSoul(path)`.
6. Optionally add middleware with `WithMiddleware(...)`.
7. Optionally register tools with `WithTool(...)`.
8. Call `ReplyAsync(...)` from your app/service/controller.

## Key concepts

- `Agent`: runtime orchestrator for model, middleware, memory, skills, and tools.
- `AgentContext`: current input + history + mutable working messages.
- `IAssistantMiddleware`: pipeline steps around model execution (e.g., memory injection, content safeguards)
- `IMemoryService`: store/retrieve memory for context injection.
- `IEmbeddingProvider` (Vector Provider): generates embeddings for semantic memory search.
- `IVectorStore`: pluggable vector storage (pgvector, in-memory, etc.).
- `ISkillLoader`: loads agent skills from filesystem.
- `ISoulLoader`: loads agent identity from SOUL.md; supports dynamic personality updates with `ReloadSoulAsync()` and `UpdateSoulAsync()`.
- `IPersistentSoulLoader`: extension for read-write SOUL implementations enabling personality learning.
- `ITool`: executable function the model can request.
- `ToolParameterAttribute`: attribute-based parameter definition enabling type-safe tool arguments with automatic validation and JSON schema generation.
- `AgenticToolAttribute`: marks a tool class for automatic discovery via `WithToolsFromAssembly` / `WithToolsFromCallingAssembly`.

If memory is configured, `MemoryMiddleware` is added automatically unless you add your own memory middleware.

### Middleware Execution Order

Middlewares execute in the order they are registered, forming a pipeline where each middleware can process requests before and after the next layer. For detailed information about how middlewares work and their execution flow, see [Middleware Execution Order](docs/middleware-execution-order.md).

## Samples

Agentic.NET includes several runnable samples to demonstrate different features and integration patterns. Each sample includes a detailed README.md explaining what it demonstrates.

### Basic Chat (`samples/BasicChat`)

The simplest example showing how to create an agent with a custom model provider. Demonstrates:
- Basic `AgentBuilder` usage
- Implementing `IModelProvider` and `IAgentModel`
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

A complete AI assistant with persistent memory and real OpenAI integration. Features:
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
| `gen_ai.system` | `llm.complete` | Always `openai` for the built-in provider |
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
| `OpenAiModels` constants | `using Agentic.Providers.OpenAi;` |
| `ITool`, `ToolParameterAttribute`, `IMemoryService` | `using Agentic.Abstractions;` |
| `ChatMessage`, `ChatRole`, `AgentReply`, `SqliteMemoryService` | `using Agentic.Core;` |
| `IAssistantMiddleware`, `AgentContext`, `AgentHandler` | `using Agentic.Middleware;` |
| `InMemoryVectorStore`, `PgVectorStore` | `using Agentic.Stores;` |
| `OpenAiEmbeddingProvider` | `using Agentic.Providers.OpenAi;` |

Most applications only need `Agentic.Builder` and `Agentic.Providers.OpenAi`. Tools additionally need `Agentic.Abstractions` and `Agentic.Core`.

## Repository layout

- `Abstractions/` contracts and interfaces
- `Builder/` fluent `AgentBuilder`
- `Core/` runtime types and built-in memory implementations
- `Loaders/` skill and SOUL document loaders
- `Middleware/` middleware contracts and built-in memory middleware
- `Providers/OpenAi/` OpenAI provider implementation
- `Stores/` vector store implementations (pgvector, in-memory)
- `samples/` runnable usage examples
- `tests/` unit tests