# Agentic.NET

![Agentic.NET logo](icon.png)

Agentic.NET is a lightweight .NET library for building AI assistants in your own applications.

It provides a small runtime around:
- a model interface (`IAgentModel`)
- optional memory (`IMemoryService`)
- middleware (`IAssistantMiddleware`)
- tool calling (`ITool`)

The goal is simple: keep your app code in control while giving you clean building blocks for assistant workflows.

## What this project is

Use Agentic.NET when you want to:
- add assistant/chat behavior to an existing .NET app
- keep model providers swappable
- add custom business tools the model can call
- plug memory and middleware into the request pipeline

This repository currently targets `net10.0` and C# preview.

## Install

### Option 1: NuGet (recommended for app projects)

```bash
dotnet add package Agentic.NET
```

### Option 2: Project reference (recommended for local development)

```bash
dotnet add <YourApp>.csproj reference ../agentic.net/Agentic.NET.csproj
```

Or add this to your app `.csproj` manually:

```xml
<ItemGroup>
  <ProjectReference Include="../agentic.net/Agentic.NET.csproj" />
</ItemGroup>
```

## Minimal usage

```csharp
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

var agent = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
    .Build();

var reply = await agent.ReplyAsync("Hello");
Console.WriteLine(reply);

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

1. Implement `IModelProvider` for your LLM backend.
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

## Publishing (maintainers)

NuGet publishing is automated by GitHub Actions in `.github/workflows/publish-nuget.yml`.

Required one-time setup:
1. Create a NuGet API key with push permissions.
2. Add it as repository secret: `NUGET_API_KEY`.

Release flow:
1. Update `<Version>` in `Agentic.NET.csproj`.
2. Commit and push to `main`.
3. Create and push a version tag, for example:

```bash
git tag v0.1.1
git push origin v0.1.1
```

The workflow builds, tests, packs, and publishes on `v*` tags.
