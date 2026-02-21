# Agentic.NET

A lightweight .NET library for building personal assistant workflows around an LLM-style model, optional memory, and middleware.

## Status

- ✅ Project builds successfully on current source (`net10.0`, C# preview)
- ✅ Core abstractions and assistant pipeline are implemented
- 🚧 `Channels/` is currently empty (extension point for transports)

## Features

- `IAssistantModel` abstraction for model completion
- `IModelProvider` factory integration via fluent builder
- `ITool` support with model-driven tool calls
- `IAssistantContextFactory` for custom context creation before middleware execution
- Middleware pipeline (`IAssistantMiddleware`) around model execution
- Optional memory integration (`IMemoryService`)
- Built-in `MemoryMiddleware` that injects relevant memory as system context
- In-memory memory provider (`InMemoryMemoryService`) for local/dev scenarios
- Conversation history tracked in `Agent.History`

## Project Structure

- `Abstractions/` — interfaces for model, memory, tool, provider, channels
- `Core/` — assistant runtime, chat message types, in-memory memory service
- `Middleware/` — middleware contract + memory middleware
- `Builder/` — fluent `AgenticBuilder` composition API

## Requirements

- .NET SDK with `net10.0` support
- C# preview language support (already configured in project)

## Quick Start

```csharp
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

var assistant = new AgenticBuilder()
    .WithModelProvider(new DemoModelProvider())
    .WithMemory(new InMemoryMemoryService())
    .Build();

var reply1 = await assistant.ReplyAsync("My name is Uros and I like C#");
var reply2 = await assistant.ReplyAsync("What do I like?");

Console.WriteLine(reply1);
Console.WriteLine(reply2);

public sealed class DemoModelProvider : IModelProvider
{
    public IAssistantModel CreateModel() => new DemoModel();
}

public sealed class DemoModel : IAssistantModel
{
    public Task<AssistantResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;
        return Task.FromResult(new AssistantResponse($"Echo: {lastUser}"));
    }
}
```

## How the Pipeline Works

1. `ReplyAsync(input)` auto-initializes memory if needed
2. `AssistantContext` is created via configured `IAssistantContextFactory` (default: history + current user message)
3. Middleware chain executes (memory middleware can prepend system memory context)
4. Model receives `WorkingMessages` and returns an `AssistantResponse`
5. User + assistant messages are appended to in-memory history
6. If memory is configured, both input and response are stored

Middleware execution order:

- Request path: middleware runs in registration order (`mw1 -> mw2 -> model`)
- Response path: unwinds in reverse (`model -> mw2 -> mw1`)

## Build

```bash
dotnet build
```

## Samples

Three runnable console samples are included:

- `samples/BasicChat` — minimal assistant with a simple echo model
- `samples/MemoryAndMiddleware` — assistant with `InMemoryMemoryService` + custom middleware
- `samples/ToolCalling` — assistant with one registered tool (`get_weather`) invoked by model tool calls
- `samples/PersonalAssistant` — example using the real OpenAI Chat API and a persistent
  SQLite-backed memory service (`SqliteMemoryService` in `Core/`).  The
the program loads any stored conversation into the assistant’s context on
  startup (it no longer spits the lines to the console) and the memory
  implementation falls back to the most‑recent messages if a query doesn’t
  produce any matches.

Run them with:

```bash
dotnet run --project samples/BasicChat/BasicChat.csproj
dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj
dotnet run --project samples/ToolCalling/ToolCalling.csproj
# be sure to set OPENAI_API_KEY first
dotnet run --project samples/PersonalAssistant/PersonalAssistant.csproj
```

## Notes

- If you pass `WithMemory(...)`, `AgenticBuilder` auto-adds `MemoryMiddleware` unless you already registered one.
- If you need custom initial context shape (for specialized implementations), register `WithContextFactory(...)`.
- Register tools with `WithTool(...)` / `WithTools(...)`; if a model returns tool calls, `Agent` executes them and re-prompts the model.
- `IChannel` is defined for future transport integrations.

## Custom Context Factory Example

```csharp
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

var assistant = new AgenticBuilder()
    .WithModelProvider(new DemoModelProvider())
    .WithContextFactory(new DomainContextFactory())
    .Build();

public sealed class DomainContextFactory : IAssistantContextFactory
{
    public AssistantContext Create(string input, IReadOnlyList<ChatMessage> history)
    {
        var context = new AssistantContext(input, history);
        context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, "You are a domain-specific assistant."));
        return context;
    }
}
```
