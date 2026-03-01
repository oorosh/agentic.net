# Agentic.NET — User Manual

## Table of Contents

1. [What is Agentic.NET?](#1-what-is-agenticnet)
2. [Prerequisites](#2-prerequisites)
3. [Installation](#3-installation)
4. [Core concepts](#4-core-concepts)
5. [Quick start — minimal chat agent](#5-quick-start--minimal-chat-agent)
6. [Memory — giving your agent a past](#6-memory--giving-your-agent-a-past)
7. [Tools — letting the agent act](#7-tools--letting-the-agent-act)
8. [Middleware — intercepting the pipeline](#8-middleware--intercepting-the-pipeline)
9. [Agent identity with SOUL.md](#9-agent-identity-with-soulmd)
10. [Agent skills](#10-agent-skills)
11. [Semantic memory with embeddings](#11-semantic-memory-with-embeddings)
12. [Custom model providers](#12-custom-model-providers)
13. [OpenTelemetry observability](#13-opentelemetry-observability)
14. [AgentBuilder reference](#14-agentbuilder-reference)
15. [Namespace reference](#15-namespace-reference)
16. [Environment variables](#16-environment-variables)
17. [Common patterns and recipes](#17-common-patterns-and-recipes)
18. [Troubleshooting](#18-troubleshooting)

---

## 1. What is Agentic.NET?

Agentic.NET is an open-source .NET library for building AI assistants (agents) that can hold conversations, remember past interactions, call external tools, and carry a defined identity — all through a clean, composable API.

It is **not** a prompt-engineering framework and **not** a cloud service. It is a lightweight runtime that sits between your application code and any Large Language Model (LLM) backend. You bring the API key; the library wires everything together.

**What it gives you out of the box:**

| Feature | Description |
|---|---|
| Conversation loop | Multi-turn chat with automatic history management |
| Tool calling | The model can request and execute functions you define |
| Memory | Store and retrieve past messages (keyword or semantic search) |
| Middleware pipeline | Insert logic before and after every LLM call |
| Agent identity (SOUL.md) | Give the agent a name, role, personality, and rules |
| Agent skills | Load capabilities from a standard `SKILL.md` directory |
| Embeddings + vector stores | Semantic memory recall using OpenAI or any custom provider |
| OpenTelemetry | Distributed tracing and metrics with zero configuration |

**What it does not do:**

- It does not decide which LLM you use — the OpenAI provider is included, but any backend can be plugged in.
- It does not manage API keys for you.
- It does not require a cloud account, a server, or a database (all persistence is optional).

---

## 2. Prerequisites

- **.NET 8, 9, or 10** — the package targets all three.
- An **OpenAI API key** if you want to use the built-in OpenAI provider. You can use a custom provider without one.
- (Optional) **PostgreSQL with pgvector** for production-scale semantic memory.
- (Optional) **SQLite** is bundled via `Microsoft.Data.Sqlite`; no separate install needed.

---

## 3. Installation

Add the NuGet package to your project:

```bash
dotnet add package Agentic.NET
```

That is the only package you need for basic usage. Additional packages like `OpenTelemetry.Exporter.OpenTelemetryProtocol` are only needed if you want to export traces/metrics.

---

## 4. Core concepts

Understanding these six types is enough to use the full library.

### Agent

`Agent` is the runtime object you talk to. You never create it directly — you always go through `AgentBuilder`. Call `ReplyAsync(string input)` to send a message and receive a response.

### AgentBuilder

The fluent configuration API. All library features are enabled through `With...` methods on `AgentBuilder`. Calling `.Build()` returns the configured `IAgent`.

### AgentReply

The return type of `ReplyAsync`. Contains:
- `Content` — the response text as a `string`.
- `UserMessage` / `AssistantMessage` — the `ChatMessage` objects that were added to history.
- Implicit cast to `string`, so `Console.WriteLine(reply)` works directly.

### IAgentModel / IModelProvider

`IAgentModel` is the thin abstraction over an LLM. `IModelProvider` is a factory for it. The built-in `OpenAiChatModelProvider` implements both for the OpenAI Chat Completions API.

### AgentContext

Passed through the middleware pipeline. Contains the current `Input`, the immutable `History` (previous turns), and `WorkingMessages` — the mutable list of messages sent to the LLM for this turn. Middleware can read and modify `WorkingMessages`.

### ITool

An executable function the LLM can request. You implement the interface, annotate properties with `[ToolParameter]`, register it with `.WithTool(...)`, and the framework handles JSON schema generation, argument parsing, and invocation.

---

## 5. Quick start — minimal chat agent

```csharp
using Agentic.Builder;
using Agentic.Providers.OpenAi;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY.");

var agent = new AgentBuilder()
    .WithOpenAi(apiKey)        // uses gpt-4o-mini by default
    .Build();

// Single turn
var reply = await agent.ReplyAsync("What is the capital of France?");
Console.WriteLine(reply.Content);   // "Paris."

// Multi-turn — history is maintained automatically
var reply2 = await agent.ReplyAsync("And what language do they speak there?");
Console.WriteLine(reply2.Content);  // "French."
```

To use a different model:

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey, OpenAiModels.Gpt4o)
    .Build();
```

Available model constants are in `Agentic.Providers.OpenAi.OpenAiModels`.

---

## 6. Memory — giving your agent a past

Without memory, the agent only knows what is in the current conversation window. Memory persists messages across sessions and injects relevant context automatically.

### In-memory (no persistence, good for testing)

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithInMemoryMemory()
    .Build();
```

### SQLite (persistent across restarts)

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMemory("memory.db")   // SQLite file path
    .Build();
```

The database file is created automatically. On the next run of the application the agent will remember previous conversations.

### How memory injection works

When memory is configured, `MemoryMiddleware` is added to the pipeline automatically. Before each LLM call it:

1. Searches stored messages for content relevant to the current input.
2. Prepends a `System` message containing the relevant past conversation to `WorkingMessages`.
3. The LLM then has context it can use to answer more accurately.

On the very first turn of a new session (empty history), all stored messages are loaded instead of running a search, so the agent can recall names, preferences, and other facts from previous sessions immediately.

---

## 7. Tools — letting the agent act

Tools let the model do things: search the web, query a database, call an API, run a calculation. The model decides when to call a tool based on the conversation; you define what the tool does.

### Defining a tool

```csharp
using Agentic.Abstractions;
using Agentic.Core;

public sealed class CalculatorTool : ITool
{
    public string Name => "calculator";
    public string Description => "Evaluates a simple arithmetic expression and returns the result.";

    [ToolParameter("expression", "The arithmetic expression to evaluate, e.g. '2 + 2' or '10 * 3.5'")]
    public string Expression { get; set; } = string.Empty;

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        // Expression is already populated by the framework.
        // Implement your logic here.
        return Task.FromResult($"Result: {Evaluate(Expression)}");
    }

    private static double Evaluate(string expr)
    {
        // ... your evaluation logic
        return 0;
    }
}
```

Key points:
- `Name` must be unique across all registered tools and should use `snake_case` (the LLM refers to it by this name).
- `Description` is shown to the LLM verbatim — write it clearly.
- Each `[ToolParameter]` property is automatically included in the JSON schema sent to the model. The framework reads the property back after parsing arguments, before `InvokeAsync` is called.
- `InvokeAsync` receives the raw JSON arguments string as a fallback; you rarely need to parse it yourself.

### Registering tools

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithTool(new CalculatorTool())
    .WithTool(new WeatherTool())
    .Build();
```

### Auto-discovering tools with `[AgenticTool]`

Instead of registering every tool manually, you can decorate your tool classes with `[AgenticTool]` and let the builder discover them automatically by scanning an assembly.

**1. Decorate your tool class**

```csharp
using Agentic.Core;

[AgenticTool]
public sealed class WeatherTool : ITool
{
    public string Name => "get_weather";
    public string Description => "Returns the current weather for a city.";

    [ToolParameter("city", "City to look up", required: true)]
    public string City { get; set; } = string.Empty;

    public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
        => Task.FromResult($"Weather in {City}: sunny, 22°C.");
}
```

**2. Scan an assembly at build time**

```csharp
// Scan the calling assembly — picks up all [AgenticTool] classes in your project.
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithToolsFromCallingAssembly()
    .Build();

// Alternatively, scan a specific assembly by marker type.
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithToolsFromAssembly<WeatherTool>()   // scans WeatherTool's assembly
    .Build();

// Or supply an Assembly instance directly.
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithToolsFromAssembly(Assembly.GetExecutingAssembly())
    .Build();
```

Auto-discovery and manual `WithTool()` calls can be freely mixed:

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithToolsFromCallingAssembly()   // auto-discover [AgenticTool] classes
    .WithTool(new RuntimeConfiguredTool(config))  // also add one manually
    .Build();
```

**Discovery rules**

| Condition | Behaviour |
|---|---|
| Class is `abstract` | Skipped |
| Class is not decorated with `[AgenticTool]` | Skipped |
| Class does not implement `ITool` | Skipped |
| Class has no public parameterless constructor | `InvalidOperationException` thrown |

**Overriding name and description via the attribute**

When you want the LLM-facing name or description to differ from the `ITool` property values (useful for versioning or renaming without changing the interface), set them directly on the attribute:

```csharp
[AgenticTool(Name = "get_weather_v2", Description = "Returns weather with extended forecast.")]
public sealed class WeatherToolV2 : ITool
{
    // ITool.Name / ITool.Description are still required by the interface,
    // but the attribute values take precedence for the LLM and tool lookup.
    public string Name => "get_weather";
    public string Description => "Basic weather lookup.";
    ...
}
```

The effective name (`[AgenticTool(Name = ...)]` if set, otherwise `ITool.Name`) is used as the dictionary key when the framework routes tool-call responses back to the correct tool instance.

### ToolParameter options

```csharp
// Required parameter
[ToolParameter("city", "City name to look up", required: true)]
public string City { get; set; } = string.Empty;

// Optional parameter with a default
[ToolParameter("unit", "Temperature unit: celsius or fahrenheit")]
public string Unit { get; set; } = "celsius";

// Numeric parameter with constraints
[ToolParameter("count", "Number of results to return", min: 1, max: 50)]
public int Count { get; set; } = 10;
```

### Tool calling flow

```
User input
  → Agent builds working messages
  → LLM responds with tool_calls
  → Framework invokes each tool
  → Tool results appended to messages
  → LLM called again with results
  → Final text response returned
```

This loop repeats until the LLM returns a plain text response (no tool calls), or the maximum depth of 12 tool call rounds is reached.

---

## 8. Middleware — intercepting the pipeline

Middleware lets you run code before and/or after the LLM is called. Common uses: logging, caching, rate limiting, content moderation, authentication, and response validation.

### Writing a middleware

```csharp
using Agentic.Abstractions;
using Agentic.Core;
using Agentic.Middleware;

public sealed class LoggingMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[IN]  {context.Input}");

        var response = await next(context, cancellationToken);   // call the next step

        Console.WriteLine($"[OUT] {response.Content}");
        return response;
    }
}
```

### Registering middleware

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMiddleware(new LoggingMiddleware())
    .Build();
```

### Middleware execution order

Middlewares execute in registration order. The pipeline is:

```
WithMiddleware(A)
WithMiddleware(B)
  └─ MemoryMiddleware (auto-added when memory is configured)
     └─ LLM call
```

`A.InvokeAsync` runs first (pre-processing), then calls `next`, which runs `B`, which calls `next`, and so on down to the actual LLM call. On the way back up, each middleware can inspect or modify the `AgentResponse`.

### Short-circuit responses

A middleware can return early without calling `next`:

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context, AgentHandler next, CancellationToken ct)
{
    if (context.Input.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        return new AgentResponse("I cannot help with that.");   // short-circuit

    return await next(context, ct);
}
```

---

## 9. Agent identity with SOUL.md

SOUL.md gives your agent a name, role, personality, and rules that are automatically prepended to every conversation as a system prompt.

### SOUL.md format

```markdown
# MyAssistant

## Role
You are a helpful customer support agent for Acme Corp.

## Personality
- Tone: Friendly and professional
- Style: Concise, direct answers; avoid jargon

## Rules
- ALWAYS stay on topic — Acme Corp products only
- NEVER reveal internal pricing beyond the published rate card
- If unsure, say so and offer to escalate

## Tools
- Use the order_lookup tool to find customer orders

## Handoffs
- Transfer billing disputes to @BillingAgent
```

Save this file as `SOUL.md` alongside your application.

### Loading SOUL.md

```csharp
// Load from ./SOUL.md next to the executable
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul()
    .Build();

// Or specify a path
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul("./config/SOUL.md")
    .Build();
```

### Dynamic personality learning

You can update the agent's personality based on what it learns during a conversation:

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul("SOUL.md")
    .WithSoulLearning((userInput, agentReply, currentSoul) =>
    {
        // Return an updated SoulDocument, or null to keep the current one.
        if (userInput.Contains("please call me "))
        {
            var name = ExtractName(userInput);
            return currentSoul with
            {
                Rules = currentSoul.Rules + $"\n- Always address the user as {name}"
            };
        }
        return null;
    })
    .Build();
```

---

## 10. Agent skills

Skills are capability bundles loaded from a directory. Each skill is a folder containing a `SKILL.md` file with YAML frontmatter and instructions.

### Skill directory structure

```
skills/
  pdf-processing/
    SKILL.md
    scripts/
      extract_text.py
  web-search/
    SKILL.md
```

### SKILL.md format

```yaml
---
name: web-search
description: Search the web for current information using a search query.
license: MIT
---
# Instructions
When the user asks about recent events or current data, use this skill to search the web.
1. Extract the core search query from the user's message.
2. Call the web_search tool with the query.
3. Summarize the top results in plain language.
```

### Loading skills

```csharp
// Load from ./skills/ next to the executable
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSkills()
    .Build();

// Or specify a directory
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSkills("./my-skills")
    .Build();
```

After `InitializeAsync` (called automatically on first `ReplyAsync`), the skill instructions are included in the system prompt.

---

## 11. Semantic memory with embeddings

Keyword-based memory retrieval works well for exact matches. For fuzzy, meaning-based retrieval ("things the user said about their job" even if the exact word "job" wasn't used), add embeddings.

### Quick setup with OpenAI embeddings

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSemanticMemory(apiKey)     // sets up OpenAI embeddings + in-memory vector store
    .Build();
```

### Production setup with PostgreSQL pgvector

Requires PostgreSQL with the pgvector extension:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

```csharp
using Agentic.Providers.OpenAi;
using Agentic.Stores;

var embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
await embeddingProvider.InitializeAsync();    // fetches embedding dimensions from API

var vectorStore = new PgVectorStore(
    connectionString: "Host=localhost;Database=agentmemory;Username=postgres;Password=...",
    dimensions: embeddingProvider.Dimensions);

var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMemory("memory.db", vectorStore)
    .WithEmbeddingProvider(embeddingProvider)
    .WithVectorStore(vectorStore)
    .Build();
```

### How it works

1. After each `ReplyAsync`, the user message and the agent's response are stored in memory.
2. Embeddings (floating-point vectors) are generated for both and stored in the vector store.
3. On the next turn, the query is also embedded and a cosine-similarity search returns the most semantically related past messages.
4. Those messages are injected into the conversation context before the LLM is called.

---

## 12. Custom model providers

If you want to use a model other than OpenAI (Anthropic, Azure OpenAI, a local Ollama instance, etc.), implement `IModelProvider` and `IAgentModel`:

```csharp
using Agentic.Abstractions;
using Agentic.Core;

// The factory
public sealed class MyModelProvider : IModelProvider
{
    public IAgentModel CreateModel() => new MyModel();
}

// The model itself
public sealed class MyModel : IAgentModel
{
    public async Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        // Build your request, call your backend, parse the response.
        var lastUserMessage = messages.Last(m => m.Role == ChatRole.User).Content;

        // Return AgentResponse with just text (no tool calls):
        return new AgentResponse("Echo: " + lastUserMessage);

        // Or with tool calls (if your backend supports function calling):
        // var toolCalls = new List<AgentToolCall> { new("tool_name", "{}", "call_id") };
        // return new AgentResponse(string.Empty, toolCalls);
    }
}

// Register it
var agent = new AgentBuilder()
    .WithModelProvider(new MyModelProvider())
    .Build();
```

The `ChatMessage` type carries `Role` (`User`, `Assistant`, `System`, `Tool`), `Content`, and optional `ToolCalls` / `ToolName` / `ToolCallId` for tool-call messages.

---

## 13. OpenTelemetry observability

Agentic.NET emits distributed traces and metrics with no configuration required inside the library. Instrumentation is zero-cost when no listener is attached.

### Wire up tracing and metrics

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Agentic.Core;

// Standalone (console app, background service, etc.)
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(AgenticTelemetry.ActivitySourceName)
    .AddConsoleExporter()
    .Build();

var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter(AgenticTelemetry.MeterName)
    .AddConsoleExporter()
    .Build();
```

```csharp
// ASP.NET Core
services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource(AgenticTelemetry.ActivitySourceName)
        .AddOtlpExporter())
    .WithMetrics(b => b
        .AddMeter(AgenticTelemetry.MeterName)
        .AddOtlpExporter());
```

### What gets traced

```
agent.reply                          ← one span per ReplyAsync call
  └─ memory.retrieval                ← if memory is configured
  └─ llm.complete                    ← first LLM call
  └─ tool.call                       ← one span per tool invocation
  └─ llm.complete                    ← follow-up calls in the tool loop
```

### Key span attributes

| Attribute | Description |
|---|---|
| `gen_ai.request.model` | The model name, e.g. `gpt-4o-mini` |
| `gen_ai.usage.input_tokens` | Prompt tokens consumed |
| `gen_ai.usage.output_tokens` | Completion tokens generated |
| `agentic.tool.name` | Name of the invoked tool |
| `agentic.tool.success` | Whether the tool call succeeded |
| `agentic.memory.mode` | `semantic` or `keyword` |
| `agentic.memory.items` | Number of memory items injected |

### Available metrics

| Metric | What it measures |
|---|---|
| `agentic.reply.duration` (ms) | End-to-end latency of each `ReplyAsync` call |
| `agentic.llm.prompt_tokens` | Prompt tokens sent to the LLM |
| `agentic.llm.completion_tokens` | Completion tokens received from the LLM |
| `agentic.tool.call.duration` (ms) | Per-tool execution time |
| `agentic.memory.retrieval.items` | Memory items returned per retrieval |

---

## 14. AgentBuilder reference

Quick reference of every `AgentBuilder` method.

| Method | What it does |
|---|---|
| `WithOpenAi(apiKey)` | Use OpenAI with the default model (`gpt-4o-mini`) |
| `WithOpenAi(apiKey, model)` | Use OpenAI with a specific model |
| `WithOpenAi(apiKey, options => ...)` | Use OpenAI with full `OpenAiProviderOptions` |
| `WithModelProvider(provider)` | Use a custom `IModelProvider` |
| `WithMemory(dbPath)` | Persistent SQLite memory |
| `WithMemory(IMemoryService)` | Custom memory service |
| `WithInMemoryMemory()` | Non-persistent in-memory storage |
| `WithSemanticMemory(apiKey)` | OpenAI embeddings + in-memory vector store (one call) |
| `WithEmbeddingProvider(provider)` | Custom embedding provider |
| `WithOpenAiEmbeddings(apiKey)` | OpenAI text-embedding-3-small |
| `WithVectorStore(store)` | Custom or `PgVectorStore` / `InMemoryVectorStore` |
| `WithTool(tool)` | Register a single tool |
| `WithTools(tools)` | Register multiple tools |
| `WithMiddleware(middleware)` | Add a middleware to the pipeline |
| `WithMiddlewares(middlewares)` | Add multiple middlewares |
| `WithSoul()` | Load `SOUL.md` from the app base directory |
| `WithSoul(path)` | Load `SOUL.md` from a specific file path |
| `WithSoul(ISoulLoader)` | Custom soul loader |
| `WithSoulLearning(callback)` | Enable dynamic personality updates |
| `WithSkills()` | Load skills from `./skills/` in the app base directory |
| `WithSkills(path)` | Load skills from a specific directory |
| `WithSkills(ISkillLoader)` | Custom skill loader |
| `WithContextFactory(factory)` | Custom `IAssistantContextFactory` |
| `Build()` | Construct and return the `IAgent` |

---

## 15. Namespace reference

| Type(s) | `using` directive |
|---|---|
| `AgentBuilder` | `using Agentic.Builder;` |
| `OpenAiModels`, `OpenAiEmbeddingProvider`, `OpenAiChatModelProvider` | `using Agentic.Providers.OpenAi;` |
| `ITool`, `IMemoryService`, `IEmbeddingProvider`, `IModelProvider` | `using Agentic.Abstractions;` |
| `ChatMessage`, `ChatRole`, `AgentReply`, `AgentResponse`, `SqliteMemoryService`, `AgenticTelemetry` | `using Agentic.Core;` |
| `IAssistantMiddleware`, `AgentContext`, `AgentHandler`, `MemoryMiddleware` | `using Agentic.Middleware;` |
| `InMemoryVectorStore`, `PgVectorStore` | `using Agentic.Stores;` |
| `SoulDocument`, `ISoulLoader`, `IPersistentSoulLoader` | `using Agentic.Abstractions;` |

Most applications only need `Agentic.Builder` and `Agentic.Providers.OpenAi`. Tools additionally need `Agentic.Abstractions` and `Agentic.Core`.

---

## 16. Environment variables

Used by the bundled samples and commonly adopted in application code:

| Variable | Required | Default | Description |
|---|---|---|---|
| `OPENAI_API_KEY` | Yes (for OpenAI) | — | Your OpenAI API key |
| `OPENAI_MODEL` | No | `gpt-4o-mini` | Model to use |
| `USE_EMBEDDINGS` | No | `false` | Set `true` to enable semantic embeddings |
| `USE_PGVECTOR` | No | `false` | Set `true` to use PostgreSQL pgvector |
| `PGVECTOR_CONNECTION_STRING` | When pgvector | — | PostgreSQL connection string |

---

## 17. Common patterns and recipes

### Stateless single-turn helper (no history, no memory)

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .Build();

var reply = await agent.ReplyAsync("Summarise this: " + longText);
Console.WriteLine(reply.Content);
```

### Persistent agent (e.g. personal assistant)

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMemory("assistant.db")
    .WithSoul("SOUL.md")
    .Build();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) break;
    var reply = await agent.ReplyAsync(input);
    Console.WriteLine($"Agent: {reply}");
}
```

### Agent with tools and safeguards

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithTool(new SearchTool())
    .WithTool(new CalculatorTool())
    .WithMiddleware(new ContentModerationMiddleware())
    .WithMiddleware(new LoggingMiddleware())
    .Build();
```

### Agent with full semantic memory stack

```csharp
var embedding = new OpenAiEmbeddingProvider(apiKey);
await embedding.InitializeAsync();

var vector = new PgVectorStore(connectionString, embedding.Dimensions);

var agent = new AgentBuilder()
    .WithOpenAi(apiKey, OpenAiModels.Gpt4o)
    .WithMemory("memory.db", vector)
    .WithEmbeddingProvider(embedding)
    .WithVectorStore(vector)
    .WithSoul("SOUL.md")
    .WithSkills("./skills")
    .Build();
```

### Injecting the agent into ASP.NET Core DI

```csharp
// Program.cs
builder.Services.AddSingleton<IAgent>(sp =>
{
    var apiKey = builder.Configuration["OpenAI:ApiKey"]!;
    return new AgentBuilder()
        .WithOpenAi(apiKey)
        .WithMemory("memory.db")
        .Build();
});

// Controller or minimal API
app.MapPost("/chat", async (ChatRequest req, IAgent agent) =>
{
    var reply = await agent.ReplyAsync(req.Message);
    return Results.Ok(new { reply.Content });
});
```

### Disposing the agent

`IAgent` implements `IAsyncDisposable`. If you create an agent outside DI and it has memory or embeddings configured, dispose it properly:

```csharp
await using var agent = (IAsyncDisposable)new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMemory("memory.db")
    .Build();
```

---

## 18. Troubleshooting

### The agent does not remember anything between restarts

Check that you are using `WithMemory("path/to/memory.db")` (SQLite, persistent) and **not** `WithInMemoryMemory()` (lost on process exit).

### Tool is never called by the model

1. Ensure the tool's `Description` clearly explains when the model should use it.
2. Add `[ToolParameter]` attributes to all properties the model needs to fill in. Tools without parameters generate a warning in the trace output and an empty JSON schema — the model often ignores them.
3. Check that the tool's `Name` uses only letters, digits, and underscores (`snake_case`).

### `InvalidOperationException: A model provider is required`

You must call `WithOpenAi(...)` or `WithModelProvider(...)` before calling `Build()`.

### `InvalidOperationException: Tool 'x' is registered more than once`

Each tool name must be unique. Check that you are not calling `WithTool(new MyTool())` twice with tools that return the same `Name`.

### Memory retrieval returns unrelated results

Switch from keyword to semantic retrieval by calling `WithSemanticMemory(apiKey)` or configuring `WithEmbeddingProvider` + `WithVectorStore`. Keyword search matches on overlapping tokens; semantic search matches on meaning.

### The tool loop runs many rounds without finishing

The default maximum is 12 rounds of tool calls. If your tools are returning errors or ambiguous results, the model may keep retrying. Check tool return values — they should be unambiguous, self-contained strings. If the loop detects the identical set of tool calls twice it short-circuits automatically.

### No spans appear in my tracing backend

Make sure you call `AddSource(AgenticTelemetry.ActivitySourceName)` on your `TracerProviderBuilder` **before** the first `ReplyAsync` call. The `ActivitySource` emits spans only to registered listeners.

### CS0618 warning: `UseMiddleware` is obsolete

Replace `UseMiddleware(m)` with `WithMiddleware(m)`. The old method is kept for backward compatibility but will be removed in a future major version.
