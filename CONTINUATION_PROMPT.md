# ~~Continuation Prompt — Agentic.NET MEAI Migration~~

> **MIGRATION COMPLETE** — All 24 steps have been executed. The library, tests, and documentation are fully updated to MEAI. This file is kept for historical reference only.
>
> - Library builds with 0 errors
> - All 185 tests pass
> - All documentation updated

---

## Goal

Migrate **Agentic.NET** (`/home/uros/Code/agentic.net`) from its custom LLM abstraction (`IModelProvider` / handrolled OpenAI HTTP client) to a **full `Microsoft.Extensions.AI` (MEAI) adoption**, using `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>` as the core LLM/embedding interfaces.

---

## Instructions

- **No backward compatibility required** — clean break is acceptable (preview project)
- **Full MEAI adoption** — not a partial adapter approach
- The core library (`Agentic.NET`) should be **zero-provider** — it accepts `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>` from the caller; no OpenAI-specific code in core
- Keep the `AgentBuilder` fluent API shape but update internals
- **Keep all Agentic.NET differentiators**: `ITool` + `[ToolParameter]` + `ToolParameterBinder`, `IMemoryService`, `IVectorStore`, `IAssistantMiddleware`, SOUL.md, Skills, Heartbeat, `AgenticTelemetry`
- **Do NOT use `UseFunctionInvocation()`** — it bypasses `ToolParameterBinder` validation; the custom tool loop must stay in Agentic.NET
- `AgenticTelemetry` stays (own `ActivitySource` + `Meter`) alongside MEAI's telemetry
- MEAI package versions: `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.Abstractions` both at **version 10.3.0**

### Architecture Strategy

Keep `IAgentModel` as an **internal** interface (not deleted). The entire middleware pipeline (`IAssistantMiddleware`, `AgentContext`, `AgentHandler`, `AgentStreamingHandler`) continues using internal types. The only public-facing boundary change is:
- `AgentBuilder.WithChatClient(IChatClient)` replaces `WithModelProvider` / `WithOpenAi`
- `AgentBuilder.WithEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>>)` replaces `WithEmbeddingProvider` / `WithOpenAiEmbeddings` / `WithSemanticMemory`
- A new internal `ChatClientAgentModel` adapter bridges `IChatClient` to `IAgentModel`

### 24-Step Implementation Plan

1. DONE - Update `Agentic.NET.csproj`
2. DONE - Delete `IModelProvider.cs`, `IEmbeddingProvider.cs`, `VectorMath.cs`, `Providers/OpenAi/`
3. TODO - Make `IAgentModel` internal
4. TODO - Update `IMemoryService.cs`: `float[]` to `ReadOnlyMemory<float>`
5. TODO - Update `IVectorStore.cs`: `float[]` to `ReadOnlyMemory<float>`
6. TODO - Update `InMemoryMemoryService.cs`: `ReadOnlyMemory<float>`, inline cosine similarity
7. TODO - Update `SqliteMemoryService.cs`: `ReadOnlyMemory<float>`, inline cosine similarity
8. TODO - Update `InMemoryVectorStore.cs`: `ReadOnlyMemory<float>`, inline cosine similarity
9. TODO - Update `PgVectorStore.cs`: `ReadOnlyMemory<float>`
10. TODO - Create `Core/ChatClientAgentModel.cs`: internal `IChatClient` to `IAgentModel` adapter
11. TODO - Update `Middleware/MemoryMiddleware.cs`: `IEmbeddingProvider` to `IEmbeddingGenerator`
12. TODO - Rewrite `Builder/AgentBuilder.cs`: `WithChatClient` + `WithEmbeddingGenerator`, remove all OpenAI-specific code
13. TODO - Update `Core/Agent.cs`: `IEmbeddingProvider` to `IEmbeddingGenerator`
14. TODO - Update `GlobalUsings.cs`: add `global using Microsoft.Extensions.AI;`
15. TODO - Update `Agentic.Tests.csproj`: add MEAI.Abstractions 10.3.0
16. TODO - Replace `Fakes/FakeModelProvider.cs` with `Fakes/FakeChatClient.cs`
17. TODO - Update `AgentBuilderTests.cs`
18. TODO - Update `EmbeddingProviderTests.cs`: remove 2 OpenAI tests
19. TODO - Update `NewFeatureTests.cs`: remove 9 OpenAI tests
20. TODO - Update `MiddlewareTests.cs`
21. TODO - Update `StreamingTests.cs`
22. TODO - Update `HeartbeatTests.cs`
23. TODO - Update `ToolExecutionTests.cs` + `ToolDiscoveryTests.cs`
24. TODO - `dotnet build` + `dotnet test`, fix errors

**Current state**: Steps 1–2 are complete. The project does NOT build — `AgentBuilder.cs`,
`Agent.cs`, `MemoryMiddleware.cs` etc. still reference the deleted `IModelProvider` /
`IEmbeddingProvider` types. Steps 3–24 are entirely pending. Begin at step 3.

---

## Discoveries

### Key design decisions

- `IAgentModel` stays **internal** (not deleted) — all test inline models like `EchoModel : IAgentModel`, `ToolCallingModel : IAgentModel`, `StubAgentModel : IAgentModel` etc. in test files continue to work because `InternalsVisibleTo("Agentic.Tests")` is already configured in the csproj
- `FakeModelProvider` is **deleted**, replaced by `FakeChatClient : IChatClient` which internally wraps an `IAgentModel` for test convenience. `FakeModelStreamHelper` stays in the same file (renamed to `FakeChatClient.cs`)
- `float[]` to `ReadOnlyMemory<float>` in interfaces: test call sites using `[1f, 0f, 0f]` array literals **do NOT need updating** because `float[]` has implicit conversion to `ReadOnlyMemory<float>`
- `VectorMath.CosineSimilarity` becomes inline `private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)` in each class that needs it
- All `OpenAiEmbeddingProvider` tests in `NewFeatureTests.cs` (lines 32-78): the 7 `OpenAiEmbeddingProvider_*` tests **must be deleted** because `OpenAiEmbeddingProvider` class no longer exists (deleted in step 2), plus the 2 `AgentBuilder_WithOpenAiEmbeddings_*` tests (lines 85-107). The `using Agentic.Providers.OpenAi;` using on line 9 is also **deleted**. Total: 9 tests removed.
- `CallbackProvider : IModelProvider` in `AgentBuilderTests.cs` is replaced with a `CallbackChatClient : IChatClient` that wraps a `CallbackModel : IAgentModel` internally (the `CallbackModel` inner class stays unchanged but is now nested inside `CallbackChatClient`)
- `TestToolModelProvider : IModelProvider` in `ToolExecutionTests.cs`: remove the `IModelProvider` interface; change `CreateModel()` to `CreateClient()` returning `new FakeChatClient(_model)`; update call site to `.WithChatClient(provider.CreateClient())` and `.WithTool(provider.Tool)`. The `Multiple_tools_registered` test uses `new FakeModelProvider(new TestAgentModel())` which also must be replaced with `new FakeChatClient(new TestAgentModel())`

---

## Full file content for new files

### `Core/ChatClientAgentModel.cs` (CREATE NEW)

```csharp
using Agentic.Abstractions;
using Microsoft.Extensions.AI;

namespace Agentic.Core;

internal sealed class ChatClientAgentModel : IAgentModel
{
    private readonly IChatClient _client;

    public ChatClientAgentModel(IChatClient client) => _client = client;

    public async Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var meaiMessages = MapToMeai(messages);
        var completion = await _client.CompleteAsync(meaiMessages, cancellationToken: cancellationToken);

        var toolCalls = completion.Message.Contents
            .OfType<FunctionCallContent>()
            .Select(fc => new AgentToolCall(fc.Name, SerializeArgs(fc.Arguments), fc.CallId))
            .ToList();

        UsageInfo? usage = null;
        if (completion.Usage is { } u)
            usage = new UsageInfo(u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0, u.TotalTokenCount ?? 0);

        return new AgentResponse(
            completion.Message.Text ?? string.Empty,
            toolCalls.Count > 0 ? toolCalls : null,
            usage,
            completion.FinishReason?.Value,
            completion.ModelId);
    }

    public async IAsyncEnumerable<StreamingToken> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var meaiMessages = MapToMeai(messages);
        var contentBuilder = new System.Text.StringBuilder();
        var toolCallBuilders = new Dictionary<string, (string Name, System.Text.StringBuilder Args, string CallId)>();
        UsageInfo? usage = null;
        string? finishReason = null;
        string? modelId = null;

        await foreach (var update in _client.CompleteStreamingAsync(meaiMessages, cancellationToken: cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc)
                {
                    contentBuilder.Append(tc.Text);
                    yield return new StreamingToken(tc.Text, IsComplete: false);
                }
                else if (content is FunctionCallContent fcc)
                {
                    var key = fcc.CallId ?? fcc.Name;
                    if (!toolCallBuilders.TryGetValue(key, out var builder))
                    {
                        builder = (fcc.Name, new System.Text.StringBuilder(), fcc.CallId ?? string.Empty);
                        toolCallBuilders[key] = builder;
                    }
                    if (fcc.Arguments is not null)
                        builder.Args.Append(SerializeArgs(fcc.Arguments));
                }
                else if (content is UsageContent uc)
                {
                    usage = new UsageInfo(uc.Details.InputTokenCount ?? 0, uc.Details.OutputTokenCount ?? 0, uc.Details.TotalTokenCount ?? 0);
                }
            }
            if (update.FinishReason is not null) finishReason = update.FinishReason.Value;
            if (update.ModelId is not null) modelId = update.ModelId;
        }

        var toolCalls = toolCallBuilders.Count > 0
            ? toolCallBuilders.Values.Select(b => new AgentToolCall(b.Name, b.Args.ToString(), b.CallId)).ToList()
            : null;

        yield return new StreamingToken(
            Delta: string.Empty,
            IsComplete: true,
            FinalUsage: usage,
            FinishReason: finishReason,
            ModelId: modelId,
            ToolCalls: toolCalls);
    }

    private static List<Microsoft.Extensions.AI.ChatMessage> MapToMeai(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<Microsoft.Extensions.AI.ChatMessage>(messages.Count);
        foreach (var msg in messages)
        {
            var meaiRole = MapRole(msg.Role);
            if (msg.Role == ChatRole.Tool)
            {
                if (msg.ToolCallId is not null)
                {
                    var meaiToolMsg = new Microsoft.Extensions.AI.ChatMessage(meaiRole,
                        [new FunctionResultContent(msg.ToolCallId, msg.Content)]);
                    result.Add(meaiToolMsg);
                    continue;
                }
                result.Add(new Microsoft.Extensions.AI.ChatMessage(meaiRole, msg.Content));
            }
            else if (msg.ToolCalls is { Count: > 0 })
            {
                var contents = new List<AIContent>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contents.Add(new TextContent(msg.Content));
                foreach (var tc in msg.ToolCalls)
                {
                    var args = tc.Arguments is not null
                        ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(tc.Arguments)
                        : null;
                    contents.Add(new FunctionCallContent(tc.ToolCallId ?? tc.Name, tc.Name, args));
                }
                result.Add(new Microsoft.Extensions.AI.ChatMessage(meaiRole, contents));
            }
            else
            {
                result.Add(new Microsoft.Extensions.AI.ChatMessage(meaiRole, msg.Content));
            }
        }
        return result;
    }

    private static Microsoft.Extensions.AI.ChatRole MapRole(ChatRole role) => role switch
    {
        ChatRole.User => Microsoft.Extensions.AI.ChatRole.User,
        ChatRole.Assistant => Microsoft.Extensions.AI.ChatRole.Assistant,
        ChatRole.System => Microsoft.Extensions.AI.ChatRole.System,
        ChatRole.Tool => Microsoft.Extensions.AI.ChatRole.Tool,
        _ => Microsoft.Extensions.AI.ChatRole.User
    };

    private static string SerializeArgs(IDictionary<string, object?>? args)
    {
        if (args is null) return string.Empty;
        return System.Text.Json.JsonSerializer.Serialize(args);
    }
}
```

### `Fakes/FakeChatClient.cs` (CREATE NEW — delete `FakeModelProvider.cs`)

```csharp
using Agentic.Abstractions;
using Agentic.Core;
using Microsoft.Extensions.AI;

namespace Agentic.Tests.Fakes;

internal sealed class FakeChatClient : IChatClient
{
    private readonly IAgentModel _model;
    public FakeChatClient(IAgentModel model) => _model = model;

    public ChatClientMetadata Metadata => new("fake", null, null);

    public async Task<ChatCompletion> CompleteAsync(
        IList<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agenticMessages = messages.Select(m => new Agentic.Core.ChatMessage(
            MapRole(m.Role), m.Text ?? string.Empty)).ToList();
        var response = await _model.CompleteAsync(agenticMessages, cancellationToken);
        return new ChatCompletion(new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.Assistant, response.Content));
    }

    public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(
        IList<Microsoft.Extensions.AI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public TService? GetService<TService>(object? key = null) where TService : class => default;
    public void Dispose() { }

    private static Agentic.Core.ChatRole MapRole(Microsoft.Extensions.AI.ChatRole role)
    {
        if (role == Microsoft.Extensions.AI.ChatRole.User) return Agentic.Core.ChatRole.User;
        if (role == Microsoft.Extensions.AI.ChatRole.Assistant) return Agentic.Core.ChatRole.Assistant;
        if (role == Microsoft.Extensions.AI.ChatRole.System) return Agentic.Core.ChatRole.System;
        if (role == Microsoft.Extensions.AI.ChatRole.Tool) return Agentic.Core.ChatRole.Tool;
        return Agentic.Core.ChatRole.User;
    }
}

internal static class FakeModelStreamHelper
{
    public static async IAsyncEnumerable<StreamingToken> StreamFromCompleteAsync(
        IAgentModel model,
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        var response = await model.CompleteAsync(messages, cancellationToken);
        if (!string.IsNullOrEmpty(response.Content))
            yield return new StreamingToken(response.Content, IsComplete: false);

        yield return new StreamingToken(
            Delta: string.Empty,
            IsComplete: true,
            FinalUsage: response.Usage,
            FinishReason: response.FinishReason,
            ModelId: response.ModelId,
            ToolCalls: response.ToolCalls);
    }
}
```

### No-op `IEmbeddingGenerator` stub for tests (add as private class in AgentBuilderTests)

```csharp
private sealed class NoOpEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata => new("noop", null, null, null);
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
            values.Select(_ => new Embedding<float>(new float[3])).ToList()));
    public object? GetService(Type serviceType, object? key = null) => null;
    public void Dispose() { }
}
```

### Inline cosine similarity helper (add to InMemoryMemoryService, SqliteMemoryService, InMemoryVectorStore)

```csharp
private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
{
    float dot = 0, magA = 0, magB = 0;
    for (int i = 0; i < a.Length; i++)
    {
        dot  += a[i] * b[i];
        magA += a[i] * a[i];
        magB += b[i] * b[i];
    }
    float denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
    return denom == 0f ? 0f : dot / denom;
}
```

---

## Relevant files / directories

```
/home/uros/Code/agentic.net/
├── Agentic.NET.csproj                          ALREADY UPDATED
├── GlobalUsings.cs                             step 14: add global using Microsoft.Extensions.AI
├── Abstractions/
│   ├── IAgentModel.cs                          step 3: public -> internal
│   ├── IMemoryService.cs                       step 4: float[] -> ReadOnlyMemory<float>
│   └── IVectorStore.cs                         step 5: float[] -> ReadOnlyMemory<float>, return type
├── Builder/
│   └── AgentBuilder.cs                         step 12: MAJOR REWRITE (WithChatClient, WithEmbeddingGenerator)
├── Core/
│   ├── Agent.cs                                step 13: IEmbeddingProvider -> IEmbeddingGenerator
│   ├── ChatClientAgentModel.cs                 step 10: CREATE NEW (full content above)
│   ├── InMemoryMemoryService.cs                step 6: ReadOnlyMemory<float> + inline cosine
│   └── SqliteMemoryService.cs                  step 7: ReadOnlyMemory<float> + inline cosine
├── Middleware/
│   └── MemoryMiddleware.cs                     step 11: IEmbeddingProvider -> IEmbeddingGenerator
├── Stores/
│   ├── InMemoryVectorStore.cs                  step 8: ReadOnlyMemory<float> + inline cosine
│   └── PgVectorStore.cs                        step 9: ReadOnlyMemory<float>
└── tests/Agentic.Tests/
    ├── Agentic.Tests.csproj                    step 15: add MEAI.Abstractions 10.3.0
    ├── Fakes/
    │   ├── FakeModelProvider.cs                DELETE
    │   └── FakeChatClient.cs                   step 16: CREATE NEW (full content above)
    ├── AgentBuilderTests.cs                    step 17
    ├── EmbeddingProviderTests.cs               step 18
    ├── HeartbeatTests.cs                       step 22
    ├── MiddlewareTests.cs                      step 20
    ├── NewFeatureTests.cs                      step 19
    ├── StreamingTests.cs                       step 21
    ├── ToolDiscoveryTests.cs                   step 23
    └── ToolExecutionTests.cs                   step 23
```
