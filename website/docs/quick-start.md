---
id: quick-start
title: Quick Start
sidebar_position: 1
---

# Quick Start

Get an AI agent running in .NET in under 5 minutes.

## Install

```bash
dotnet add package Agentic.NET
dotnet add package Microsoft.Extensions.AI.OpenAI
```

## Your first agent

```csharp
using Agentic.Builder;
using OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;

var agent = new AgentBuilder()
    .WithChatClient(new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini"))
    .Build();

var reply = await agent.ReplyAsync("Hello! What can you help me with?");
Console.WriteLine(reply);
```

## Add memory

```csharp
var agent = new AgentBuilder()
    .WithChatClient(new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini"))
    .WithMemory("agent.db")   // persists to SQLite
    .Build();
```

## Add a tool

```csharp
public sealed class GetTimeTool : ITool
{
    public string Name => "get_time";
    public string Description => "Returns the current time.";

    public Task<string> InvokeAsync(string arguments, CancellationToken ct = default)
        => Task.FromResult(DateTime.Now.ToString("HH:mm:ss"));
}

var agent = new AgentBuilder()
    .WithChatClient(...)
    .WithTool(new GetTimeTool())
    .Build();
```

## Streaming

```csharp
await foreach (var token in agent.StreamAsync("Tell me a story"))
{
    if (!token.IsComplete)
        Console.Write(token.Delta);
}
```

## Next steps

- Read the [User Manual](./user-manual) for the full API
- Follow the [article series](/blog) to build real-world agents step by step
