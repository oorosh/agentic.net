---
id: namespaces
title: Namespace Reference
---

# Namespace Reference

| What you need | `using` directive |
|---|---|
| `AgentBuilder` | `using Agentic.Builder;` |
| `ITool`, `IMemoryService`, `IHeartbeatService` | `using Agentic.Abstractions;` |
| `ChatMessage`, `SqliteMemoryService`, `HeartbeatOptions`, `StreamingToken` | `using Agentic.Core;` |
| `IAssistantMiddleware`, `AgentContext` | `using Agentic.Middleware;` |
| `InMemoryVectorStore`, `PgVectorStore` | `using Agentic.Stores;` |
| `IChatClient`, `IEmbeddingGenerator`, `Embedding<float>` | `using Microsoft.Extensions.AI;` |
