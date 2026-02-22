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

Note: this package is a **preview** release; expect
breaking changes until a stable version is published.

For full documentation, examples and contribution instructions see
https://github.com/oorosh/agentic.net.
