---
id: agent-builder
title: AgentBuilder Reference
---

# AgentBuilder Reference

`AgentBuilder` is the fluent entry point for configuring and constructing an `IAgent`.

## Required

| Method | Description |
|--------|-------------|
| `WithChatClient(IChatClient)` | Sets the underlying LLM. Required. |

## Memory

| Method | Description |
|--------|-------------|
| `WithMemory(string dbPath)` | SQLite persistent memory. |
| `WithMemory(IMemoryService)` | Custom memory service. |
| `WithInMemoryMemory()` | Non-persistent, dev/test only. |
| `WithVectorStore(IVectorStore)` | Pluggable vector store for embeddings. |
| `WithEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>>)` | Enables semantic memory. |

## Tools

| Method | Description |
|--------|-------------|
| `WithTool(ITool)` | Register a single tool. |
| `WithTools(IEnumerable<ITool>)` | Register multiple tools. |
| `WithToolsFromAssembly(Assembly)` | Auto-discover `[AgenticTool]` tools. |
| `WithToolsFromCallingAssembly()` | Auto-discover in calling assembly. |

## Identity & Skills

| Method | Description |
|--------|-------------|
| `WithSoul(string path)` | Load agent identity from SOUL.md. |
| `WithSoul(ISoulLoader)` | Custom soul loader. |
| `WithSkills(string dir)` | Load skills from directory. |
| `WithSkills(ISkillLoader)` | Custom skill loader. |

## Middleware

| Method | Description |
|--------|-------------|
| `WithMiddleware(IAssistantMiddleware)` | Add middleware to the pipeline. |
| `WithMiddlewares(IEnumerable<IAssistantMiddleware>)` | Add multiple middlewares. |

## Heartbeat

| Method | Description |
|--------|-------------|
| `WithHeartbeat()` | Default 5-minute proactive heartbeat. |
| `WithHeartbeat(TimeSpan)` | Custom interval. |
| `WithHeartbeat(Action<HeartbeatOptions>)` | Full configuration. |
