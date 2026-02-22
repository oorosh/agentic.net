# Agentic.NET

![Agentic.NET logo](logo.png)

Create AI assistants in .NET with pluggable models, memory, middleware, and tools.

The library exposes a minimal runtime with:

- `IAgentModel` abstraction for the underlying LLM or chat model
- optional memory (`IMemoryService`) with built-in SQLite and in‑memory providers
- middleware hooks (`IAssistantMiddleware`) to preprocess or postprocess conversation
- a tool‑calling mechanism (`ITool`) that the model can invoke

Designed for clarity and composability, the API lets your app stay in control while leveraging AI logic.

## Install

### NuGet (recommended for application developers)

The package is published on nuget.org and can be added by running:

```bash
# pick the version you want; 0.1.1-preview is current
dotnet add package Agentic.NET --version 0.1.1-preview (preview release)
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
4. Optionally add middleware with `UseMiddleware(...)`.
5. Optionally register tools with `WithTool(...)`.
6. Call `ReplyAsync(...)` from your app/service/controller.

## Key concepts

- `Agent`: runtime orchestrator for model, middleware, memory, and tools.
- `AgentContext`: current input + history + mutable working messages.
- `IAssistantMiddleware`: pipeline steps around model execution.
- `IMemoryService`: store/retrieve memory for context injection.
- `ITool`: executable function the model can request.

If memory is configured, `MemoryMiddleware` is added automatically unless you add your own memory middleware.

## Samples

- `samples/BasicChat` — minimal chat loop
- `samples/MemoryAndMiddleware` — memory + custom middleware + context factory
- `samples/ToolCalling` — OpenAI function-style tool calls (`get_weather`)
- `samples/PersonalAssistant` — OpenAI + SQLite persistent memory

Run:

```bash
dotnet run --project samples/BasicChat/BasicChat.csproj
dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj
dotnet run --project samples/ToolCalling/ToolCalling.csproj
dotnet run --project samples/PersonalAssistant/PersonalAssistant.csproj
```

For OpenAI samples, set `OPENAI_API_KEY` first.

## Repository layout

- `Abstractions/` contracts and interfaces
- `Builder/` fluent `AgentBuilder`
- `Core/` runtime types and built-in memory implementations
- `Middleware/` middleware contracts and built-in memory middleware
- `Providers/OpenAi/` OpenAI provider implementation
- `samples/` runnable usage examples
- `tests/` unit tests