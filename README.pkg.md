# Agentic.NET NuGet Package

Create AI assistants in .NET with pluggable models, memory, middleware, and
tool calling. Agentic.NET gives you a small runtime to compose assistant
workflows without coupling your app to a single provider.

This README is kept short for NuGet users; full documentation and samples are
available in the GitHub repository.

## Install

```bash
dotnet add package Agentic.NET --version 0.1.1-preview
```

## Quick example

```csharp
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

// create a simple agent that echoes back the last user message
var agent = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
    .Build();

var reply = await agent.ReplyAsync("What's up?");
Console.WriteLine(reply); // -> "Echo: What's up?"

// the same agent supports optional memory, middleware and tools

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

Note: this package is a **preview** release (`-preview1` suffix); expect
breaking changes until a stable version is published.

For full documentation, examples and contribution instructions see
https://github.com/oorosh/agentic.net.
