# Agentic.NET NuGet Package

Create AI assistants in .NET with pluggable models, memory, middleware, and
tool calling. Agentic.NET gives you a small runtime to compose assistant
workflows without coupling your app to a single provider.

This README is kept short for NuGet users; full documentation and samples are
available in the GitHub repository.

## Install

```bash
dotnet add package Agentic.NET
```

## Quick example

Agentic.NET is built on [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) (MEAI). You need a MEAI-compatible provider package alongside this one:

```bash
dotnet add package Microsoft.Extensions.AI.OpenAI
```

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

var reply = await agent.ReplyAsync("What's up?");
Console.WriteLine(reply);

// optional: add memory, middleware and tools the same way
```

## Key Features

- **Provider-agnostic**: Works with any `IChatClient` from Microsoft.Extensions.AI (OpenAI, Azure, Ollama, custom)
- **Streaming**: Receive tokens incrementally via `StreamAsync` with `IAsyncEnumerable<StreamingToken>`
- **Memory**: SQLite, in-memory, or implement `IMemoryService` for custom storage
- **Semantic memory**: Bring any `IEmbeddingGenerator<string, Embedding<float>>` for vector-based recall
- **Middleware**: Pre/post-process conversation with `IAssistantMiddleware`
- **Tools**: Register executable functions the model can invoke
- **Skills**: Load capabilities from Agent Skills format
- **Identity**: Define agent personality with SOUL.md format

Note: this package is a **preview** release; expect
breaking changes until a stable version is published.

For full documentation, examples and contribution instructions see
https://github.com/oorosh/agentic.net.
