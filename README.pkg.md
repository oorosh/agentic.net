# Agentic.NET NuGet Package

Create AI assistants in .NET with pluggable models, memory, middleware, and
tool calling. Agentic.NET gives you a small runtime to compose assistant
workflows without coupling your app to a single provider.

**New in v0.1.5:** Dynamic SOUL.md learning - agents can now update their personality
during conversations and persist changes back to disk or any custom storage.

This README is kept short for NuGet users; full documentation and samples are
available in the GitHub repository.

## Install

```bash
dotnet add package Agentic.NET
```

## Quick example

```csharp
using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY first.");
var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? OpenAiModels.Gpt4oMini;

// configure built-in OpenAI provider directly in the pipeline
var agent = new AgentBuilder()
    .WithOpenAi(apiKey, model: model)
    .Build();

var reply = await agent.ReplyAsync("What's up?");
Console.WriteLine(reply);

// optional: add memory, middleware and tools the same way
```

## Dynamic Personality Learning (v0.1.5+)

Agents can now learn and adapt their personality:

```csharp
// Load initial SOUL.md
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul(new FileSystemSoulLoader("./SOUL.md"))
    .Build();

// Update personality based on feedback
var updatedSoul = agent.Soul with 
{ 
    Personality = "More friendly and approachable" 
};
await agent.UpdateSoulAsync(updatedSoul);  // Saves to SOUL.md
```

## Custom SOUL Loaders

Implement `ISoulLoader` or `IPersistentSoulLoader` to load personality from any source:
- Database (PostgreSQL, SQLite, SQL Server, MongoDB)
- API endpoints (REST, GraphQL)
- Cloud storage (S3, Azure Blob, Firestore)
- Configuration servers
- Your own custom implementation

```csharp
public sealed class DatabaseSoulLoader : IPersistentSoulLoader { ... }

var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul(new DatabaseSoulLoader(connectionString))
    .Build();
```

Note: this package is a **preview** release; expect
breaking changes until a stable version is published.

For full documentation, examples and contribution instructions see
https://github.com/oorosh/agentic.net.
