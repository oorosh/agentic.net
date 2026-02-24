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
Console.WriteLine(reply);
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
6. Optionally add middleware with `UseMiddleware(...)`.
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
- `ISoulLoader`: loads agent identity from SOUL.md.
- `ITool`: executable function the model can request.

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

## Environment Variables

Samples that use OpenAI require the following environment variables:

- `OPENAI_API_KEY`: Your OpenAI API key (required for OpenAI samples)
- `OPENAI_MODEL`: Model to use (optional, defaults to `gpt-4o-mini`)
- `USE_EMBEDDINGS`: Set to `true` to enable semantic embeddings (optional)
- `USE_PGVECTOR`: Set to `true` to use PostgreSQL pgvector (optional)
- `PGVECTOR_CONNECTION_STRING`: PostgreSQL connection string (required when USE_PGVECTOR=true)

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