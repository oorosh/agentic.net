# Agentic.NET — User Manual

## Table of Contents

1. [What is Agentic.NET?](#1-what-is-agenticnet)
2. [Prerequisites](#2-prerequisites)
3. [Installation](#3-installation)
4. [Core concepts](#4-core-concepts)
5. [Quick start — minimal chat agent](#5-quick-start--minimal-chat-agent)
6. [Streaming](#6-streaming)
7. [Memory — giving your agent a past](#7-memory--giving-your-agent-a-past)
8. [Tools — letting the agent act](#8-tools--letting-the-agent-act)
9. [Middleware — intercepting the pipeline](#9-middleware--intercepting-the-pipeline)
10. [Agent identity with SOUL.md](#10-agent-identity-with-soulmd)
11. [Agent skills](#11-agent-skills)
12. [Semantic memory with embeddings](#12-semantic-memory-with-embeddings)
13. [Custom chat clients](#13-custom-chat-clients)
14. [OpenTelemetry observability](#14-opentelemetry-observability)
15. [AgentBuilder reference](#15-agentbuilder-reference)
16. [Namespace reference](#16-namespace-reference)
17. [Environment variables](#17-environment-variables)
18. [Common patterns and recipes](#18-common-patterns-and-recipes)
19. [Troubleshooting](#19-troubleshooting)

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
| Embeddings + vector stores | Semantic memory recall using any MEAI `IEmbeddingGenerator<string, Embedding<float>>` |
| OpenTelemetry | Distributed tracing and metrics with zero configuration |

**What it does not do:**

- It does not decide which LLM you use — bring any `IChatClient` from `Microsoft.Extensions.AI`.
- It does not manage API keys for you.
- It does not require a cloud account, a server, or a database (all persistence is optional).

---

## 2. Prerequisites

- **.NET 8, 9, or 10** — the package targets all three.
- A **MEAI-compatible provider package** (e.g., `Microsoft.Extensions.AI.OpenAI`) and the corresponding API key/endpoint.
- (Optional) **PostgreSQL with pgvector** for production-scale semantic memory.
- (Optional) **SQLite** is bundled via `Microsoft.Data.Sqlite`; no separate install needed.

---

## 3. Installation

Add the NuGet package to your project:

```bash
dotnet add package Agentic.NET
```

You also need a MEAI-compatible provider. For OpenAI:

```bash
dotnet add package Microsoft.Extensions.AI.OpenAI
```

For other providers (Azure OpenAI, Ollama, Anthropic, etc.) install the corresponding MEAI package. The library itself only depends on `Microsoft.Extensions.AI.Abstractions`.

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
- `Usage` — a `UsageInfo?` with `PromptTokens`, `CompletionTokens`, and `TotalTokens` (null if the provider doesn't report usage).
- `FinishReason` — a `string?` such as `"stop"` or `"length"` (null if not reported).
- `ModelId` — the model name echoed back by the provider (null if not reported).
- `Duration` — the end-to-end wall-clock time for this reply turn as a `TimeSpan`.
- Implicit cast to `string`, so `Console.WriteLine(reply)` works directly.

### IAgent / IChatClient

`IChatClient` (from `Microsoft.Extensions.AI`) is the LLM abstraction. You bring any MEAI-compatible implementation — OpenAI, Azure OpenAI, Ollama, or your own. `AgentBuilder.WithChatClient(chatClient)` is the single entry point to set it.

### AgentContext

Passed through the middleware pipeline. Contains the current `Input`, the immutable `History` (previous turns), and `WorkingMessages` — the mutable list of messages sent to the LLM for this turn. Middleware can read and modify `WorkingMessages`.

### ITool

An executable function the LLM can request. You implement the interface, annotate properties with `[ToolParameter]`, register it with `.WithTool(...)`, and the framework handles JSON schema generation, argument parsing, and invocation.

---

## 5. Quick start — minimal chat agent

Agentic.NET uses [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) (MEAI) for its model abstraction. You need a MEAI-compatible provider package in addition to `Agentic.NET`.

For OpenAI:

```bash
dotnet add package Agentic.NET
dotnet add package Microsoft.Extensions.AI.OpenAI
```

```csharp
using Agentic.Builder;
using Microsoft.Extensions.AI;
using OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY.");

var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .Build();

// Single turn
var reply = await agent.ReplyAsync("What is the capital of France?");
Console.WriteLine(reply.Content);   // "Paris."

// Multi-turn — history is maintained automatically
var reply2 = await agent.ReplyAsync("And what language do they speak there?");
Console.WriteLine(reply2.Content);  // "French."
```

To use a different model, change the model name string in `AsChatClient(...)`:

```csharp
var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o");
```

Other providers work identically — just swap the `IChatClient` implementation:

```csharp
// Azure OpenAI
using Azure.AI.OpenAI;
var chatClient = new AzureOpenAIClient(endpoint, credential).AsChatClient("gpt-4o");

// Ollama (local model)
var chatClient = new OllamaChatClient(new Uri("http://localhost:11434"), "llama3.2");
```

---

## 6. Streaming

Instead of waiting for the full response, use `StreamAsync` to receive tokens as they are generated. This is ideal for chat UIs, progress indicators, or any scenario where perceived latency matters.

### Basic usage

```csharp
await foreach (var token in agent.StreamAsync("Tell me a story"))
{
    if (!token.IsComplete)
    {
        Console.Write(token.Delta);   // print each chunk immediately
    }
    else
    {
        // The final sentinel token carries metadata
        Console.WriteLine();
        Console.WriteLine($"Finish reason : {token.FinishReason}");
        Console.WriteLine($"Model         : {token.ModelId}");
        Console.WriteLine($"Tokens used   : {token.FinalUsage?.TotalTokens}");
    }
}
```

### StreamingToken fields

| Field | Description |
|---|---|
| `Delta` | The incremental text chunk for this token (empty string on the sentinel). |
| `IsComplete` | `true` only on the final sentinel token — stop iterating after this. |
| `FinalUsage` | `UsageInfo?` with `PromptTokens`, `CompletionTokens`, `TotalTokens` — only on the final token. |
| `FinishReason` | `"stop"`, `"length"`, etc. — only on the final token. |
| `ModelId` | Model name echoed back by the provider — only on the final token. |
| `ToolCalls` | Populated when the model requested tool calls mid-stream (handled internally). |

### Behaviour notes

- History is updated and memory is persisted only **after** the stream is fully consumed (same semantics as `ReplyAsync`).
- If the model requests tool calls mid-stream, the framework resolves them internally and then streams the final answer — callers see only text tokens.
- Middleware participates via `IAssistantMiddleware.StreamAsync`. The default implementation transparently forwards all tokens, so existing middleware that only overrides `InvokeAsync` works without changes.
- Cancellation is supported: pass a `CancellationToken` and the enumeration will stop cleanly.

---

## 7. Memory — giving your agent a past

Without memory, the agent only knows what is in the current conversation window. Memory persists messages across sessions and injects relevant context automatically.

### In-memory (no persistence, good for testing)

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithInMemoryMemory()
    .Build();
```

### SQLite (persistent across restarts)

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
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

## 8. Tools — letting the agent act

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
    .WithChatClient(chatClient)
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
    .WithChatClient(chatClient)
    .WithToolsFromCallingAssembly()
    .Build();

// Alternatively, scan a specific assembly by marker type.
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithToolsFromAssembly<WeatherTool>()   // scans WeatherTool's assembly
    .Build();

// Or supply an Assembly instance directly.
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithToolsFromAssembly(Assembly.GetExecutingAssembly())
    .Build();
```

Auto-discovery and manual `WithTool()` calls can be freely mixed:

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
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

## 9. Middleware — intercepting the pipeline

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
    .WithChatClient(chatClient)
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

## 10. Agent identity with SOUL.md

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
    .WithChatClient(chatClient)
    .WithSoul()
    .Build();

// Or specify a path
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithSoul("./config/SOUL.md")
    .Build();
```

### Dynamic personality learning

You can update the agent's personality based on what it learns during a conversation:

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
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

## 11. Agent skills

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
    .WithChatClient(chatClient)
    .WithSkills()
    .Build();

// Or specify a directory
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithSkills("./my-skills")
    .Build();
```

After `InitializeAsync` (called automatically on first `ReplyAsync`), the skill instructions are included in the system prompt.

---

## 12. Semantic memory with embeddings

Keyword-based memory retrieval works well for exact matches. For fuzzy, meaning-based retrieval ("things the user said about their job" even if the exact word "job" wasn't used), add embeddings.

### Setup with OpenAI embeddings + in-memory vector store

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using Agentic.Builder;
using Agentic.Stores;

var openAiClient = new OpenAIClient(apiKey);
var chatClient = openAiClient.AsChatClient("gpt-4o-mini");
var embeddingGenerator = openAiClient
    .AsEmbeddingGenerator<string, Embedding<float>>("text-embedding-3-small");

var vectorStore = new InMemoryVectorStore(dimensions: 1536);

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithEmbeddingGenerator(embeddingGenerator)
    .WithVectorStore(vectorStore)
    .WithMemory("memory.db")
    .Build();
```

### Production setup with PostgreSQL pgvector

Requires PostgreSQL with the pgvector extension:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using Agentic.Stores;

var openAiClient = new OpenAIClient(apiKey);
var chatClient = openAiClient.AsChatClient("gpt-4o-mini");
var embeddingGenerator = openAiClient
    .AsEmbeddingGenerator<string, Embedding<float>>("text-embedding-3-small");

var vectorStore = new PgVectorStore(
    connectionString: "Host=localhost;Database=agentmemory;Username=postgres;Password=...",
    dimensions: 1536);

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithEmbeddingGenerator(embeddingGenerator)
    .WithVectorStore(vectorStore)
    .WithMemory("memory.db", vectorStore)
    .Build();
```

### How it works

1. After each `ReplyAsync`, the user message and the agent's response are stored in memory.
2. Embeddings (floating-point vectors) are generated for both and stored in the vector store.
3. On the next turn, the query is also embedded and a cosine-similarity search returns the most semantically related past messages.
4. Those messages are injected into the conversation context before the LLM is called.

---

## 13. Custom chat clients

If you need a backend that does not yet have a MEAI provider package, implement `IChatClient` from `Microsoft.Extensions.AI` directly:

```csharp
using Microsoft.Extensions.AI;

public sealed class MyCustomChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("my-provider", null, null);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Build your request, call your backend, parse the response.
        var lastUserMessage = messages.Last(m => m.Role == ChatRole.User).Text;

        return new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Echo: " + lastUserMessage)]);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Minimal: delegate to GetResponseAsync and emit as a single update.
        throw new NotImplementedException("Implement streaming when needed.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

// Register it
var agent = new AgentBuilder()
    .WithChatClient(new MyCustomChatClient())
    .Build();
```

`Microsoft.Extensions.AI.ChatMessage` carries `Role` (`User`, `Assistant`, `System`, `Tool`), `Text`, and `Contents` (a list of `AIContent` items including `TextContent`, `FunctionCallContent`, and `FunctionResultContent` for tool-call messages).

---

## 14. OpenTelemetry observability

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

## 15. AgentBuilder reference

Quick reference of every `AgentBuilder` method.

| Method | What it does |
|---|---|
| `WithChatClient(IChatClient)` | Set the LLM backend — required; accepts any MEAI `IChatClient` |
| `WithEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>>)` | Enable semantic embeddings for memory search |
| `WithMemory(dbPath)` | Persistent SQLite memory |
| `WithMemory(dbPath, IVectorStore)` | Persistent SQLite memory with custom vector store |
| `WithMemory(IMemoryService)` | Custom memory service |
| `WithInMemoryMemory()` | Non-persistent in-memory storage |
| `WithVectorStore(store)` | Attach a vector store (`PgVectorStore` / `InMemoryVectorStore`) |
| `WithTool(tool)` | Register a single tool |
| `WithTools(tools)` | Register multiple tools |
| `WithToolsFromCallingAssembly()` | Auto-discover `[AgenticTool]` classes in calling assembly |
| `WithToolsFromAssembly<TMarker>()` | Auto-discover in TMarker's assembly |
| `WithToolsFromAssembly(Assembly)` | Auto-discover in a specific assembly |
| `WithMiddleware(middleware)` | Add a middleware to the pipeline |
| `WithMiddlewares(middlewares)` | Add multiple middlewares |
| `WithSoul()` | Load `SOUL.md` from the app base directory |
| `WithSoul(path)` | Load `SOUL.md` from a specific file or directory |
| `WithSoul(ISoulLoader)` | Custom soul loader |
| `WithSoulLearning(callback)` | Enable dynamic personality updates |
| `WithSkills()` | Load skills from `./skills/` in the app base directory |
| `WithSkills(path)` | Load skills from a specific directory |
| `WithSkills(ISkillLoader)` | Custom skill loader |
| `WithHeartbeat()` | Enable proactive heartbeat with default options (5-minute interval) |
| `WithHeartbeat(TimeSpan, string?)` | Enable heartbeat with custom interval and optional prompt |
| `WithHeartbeat(HeartbeatOptions)` | Enable heartbeat with fully custom options |
| `WithHeartbeat(Action<HeartbeatOptions>)` | Enable heartbeat configured via callback |
| `WithContextFactory(factory)` | Custom `IAssistantContextFactory` |
| `Build()` | Construct and return the `IAgent` |

---

## 16. Namespace reference

| Type(s) | `using` directive |
|---|---|
| `AgentBuilder` | `using Agentic.Builder;` |
| `ITool`, `ToolParameterAttribute`, `AgenticToolAttribute`, `IMemoryService`, `IHeartbeatService` | `using Agentic.Abstractions;` |
| `ChatMessage`, `ChatRole`, `AgentReply`, `AgentResponse`, `UsageInfo`, `StreamingToken`, `SqliteMemoryService`, `AgenticTelemetry`, `HeartbeatOptions`, `HeartbeatResult` | `using Agentic.Core;` |
| `IAssistantMiddleware`, `AgentContext`, `AgentHandler`, `AgentStreamingHandler`, `MemoryMiddleware` | `using Agentic.Middleware;` |
| `InMemoryVectorStore`, `PgVectorStore` | `using Agentic.Stores;` |
| `SoulDocument`, `ISoulLoader`, `IPersistentSoulLoader` | `using Agentic.Abstractions;` |
| `IChatClient`, `IEmbeddingGenerator<,>`, `ChatMessage` (MEAI), `Embedding<float>`, `ChatRole` (MEAI) | `using Microsoft.Extensions.AI;` |

> **Note:** `Microsoft.Extensions.AI.ChatMessage` and `Agentic.Core.ChatMessage` have the same name. Do **not** add `global using Microsoft.Extensions.AI;` — use explicit file-scoped `using` only where needed and qualify the Agentic type as `Agentic.Core.ChatMessage` in those files.

Most applications only need `Agentic.Builder`, `Agentic.Core`, and `Microsoft.Extensions.AI` (plus the MEAI provider package). Tools additionally need `Agentic.Abstractions`.

---

## 17. Environment variables

Used by the bundled samples and commonly adopted in application code:

| Variable | Required | Default | Description |
|---|---|---|---|
| `OPENAI_API_KEY` | Yes (for OpenAI) | — | Your OpenAI API key |
| `OPENAI_MODEL` | No | `gpt-4o-mini` | Model to use |
| `USE_EMBEDDINGS` | No | `false` | Set `true` to enable semantic embeddings |
| `USE_PGVECTOR` | No | `false` | Set `true` to use PostgreSQL pgvector |
| `PGVECTOR_CONNECTION_STRING` | When pgvector | — | PostgreSQL connection string |

---

## 18. Common patterns and recipes

### Stateless single-turn helper (no history, no memory)

```csharp
var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .Build();

var reply = await agent.ReplyAsync("Summarise this: " + longText);
Console.WriteLine(reply.Content);
```

### Persistent agent (e.g. personal assistant)

```csharp
var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
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
var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithTool(new SearchTool())
    .WithTool(new CalculatorTool())
    .WithMiddleware(new ContentModerationMiddleware())
    .WithMiddleware(new LoggingMiddleware())
    .Build();
```

### Agent with full semantic memory stack

```csharp
var openAiClient = new OpenAIClient(apiKey);
var chatClient = openAiClient.AsChatClient("gpt-4o");
var embeddingGenerator = openAiClient
    .AsEmbeddingGenerator<string, Embedding<float>>("text-embedding-3-small");

var vector = new PgVectorStore(connectionString, dimensions: 1536);

var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithEmbeddingGenerator(embeddingGenerator)
    .WithVectorStore(vector)
    .WithMemory("memory.db", vector)
    .WithSoul("SOUL.md")
    .WithSkills("./skills")
    .Build();
```

### Injecting the agent into ASP.NET Core DI

```csharp
// Program.cs
builder.Services.AddSingleton<IChatClient>(_ =>
    new OpenAIClient(builder.Configuration["OpenAI:ApiKey"]!)
        .AsChatClient("gpt-4o-mini"));

builder.Services.AddSingleton<IAgent>(sp =>
    new AgentBuilder()
        .WithChatClient(sp.GetRequiredService<IChatClient>())
        .WithMemory("memory.db")
        .Build());

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
    .WithChatClient(chatClient)
    .WithMemory("memory.db")
    .Build();
```

---

## 19. Troubleshooting

### The agent does not remember anything between restarts

Check that you are using `WithMemory("path/to/memory.db")` (SQLite, persistent) and **not** `WithInMemoryMemory()` (lost on process exit).

### Tool is never called by the model

1. Ensure the tool's `Description` clearly explains when the model should use it.
2. Add `[ToolParameter]` attributes to all properties the model needs to fill in. Tools without parameters generate a warning in the trace output and an empty JSON schema — the model often ignores them.
3. Check that the tool's `Name` uses only letters, digits, and underscores (`snake_case`).

### `InvalidOperationException: A chat client is required`

You must call `WithChatClient(IChatClient)` before calling `Build()`.

### `InvalidOperationException: Tool 'x' is registered more than once`

Each tool name must be unique. Check that you are not calling `WithTool(new MyTool())` twice with tools that return the same `Name`.

### Memory retrieval returns unrelated results

Switch from keyword to semantic retrieval by calling `WithEmbeddingGenerator(generator)` + `WithVectorStore(store)`. Keyword search matches on overlapping tokens; semantic search matches on meaning.

### The tool loop runs many rounds without finishing

The default maximum is 12 rounds of tool calls. If your tools are returning errors or ambiguous results, the model may keep retrying. Check tool return values — they should be unambiguous, self-contained strings. If the loop detects the identical set of tool calls twice it short-circuits automatically.

### No spans appear in my tracing backend

Make sure you call `AddSource(AgenticTelemetry.ActivitySourceName)` on your `TracerProviderBuilder` **before** the first `ReplyAsync` call. The `ActivitySource` emits spans only to registered listeners.

### CS0618 warning: `UseMiddleware` is obsolete

Replace `UseMiddleware(m)` with `WithMiddleware(m)`. The old method is kept for backward compatibility but will be removed in a future major version.
