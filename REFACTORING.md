# Refactoring Suggestions

A code review of the Agentic.NET library. Findings are ordered by priority.

---

## High Priority

### 1. `IVectorStore` is in the wrong namespace

**File:** `Abstractions/IVectorStore.cs:3`

The file lives in the `Abstractions/` folder but declares `namespace Agentic.Stores`. Every other file in `Abstractions/` uses `namespace Agentic.Abstractions`. This is a public API inconsistency — consumers of the library get `IVectorStore` from a `Stores` namespace while `IMemoryService`, `IEmbeddingProvider`, etc. all come from `Abstractions`.

**Suggestion:** Change the namespace to `Agentic.Abstractions` (and update all `using Agentic.Stores` references that exist only for this interface). This is a breaking change, so it should ship with a major version bump or a `[Obsolete]` shim.

---

### 2. Concrete classes mixed into the `Abstractions/` folder

**Files:**
- `Abstractions/IToolParameterMetadata.cs:67` — `ToolParameterMetadata` (concrete `sealed class`)
- `Abstractions/ToolParameterSchema.cs:9` — `ToolParameterSchema` (concrete `sealed class`)
- `Abstractions/ToolParameterSchema.cs:144` — `ParametersSchema` (concrete `sealed class`)
- `Abstractions/ToolParameterAttribute.cs` — `ToolParameterAttribute` (attribute class)

The `Abstractions/` folder should contain only interfaces and contracts. Having concrete implementations there violates the convention established by every other file in the folder and confuses consumers about what is stable API vs. implementation detail.

**Suggestion:** Move concrete types to `Core/` (or a new `Tools/` subfolder). `ToolParameterMetadata.ExtractFromTool()` is a factory method on a concrete class — that logic could also move to a dedicated `ToolParameterReader` class in `Core/`.

---

### 3. `ToolParameterMetadata.ExtractFromTool` is called redundantly in a hot path

**File:** `Core/Agent.cs:147` and `Core/Agent.cs:216`

`ReplyAsync` calls `ToolParameterMetadata.ExtractFromTool(tool)` inside the tool execution loop (once per tool call, every iteration). `BuildToolCatalogMessage` also calls `ExtractFromTool` for every tool on every request. Both use reflection each time.

**Suggestion:** Cache the parameter metadata per tool type at build time (in `AgentBuilder.Build()` or when the tool is first registered). Store a `IReadOnlyDictionary<string, IReadOnlyList<IToolParameterMetadata>>` alongside the tool lookup dictionary.

---

### 4. Tool result encoding is a fragile string convention

**Files:** `Core/Agent.cs:164`, `Providers/OpenAi/OpenAiChatModelProvider.cs:142-150`

Tool results are serialized into `ChatMessage` as `"toolName: result"` plain strings, then re-parsed in `ToOpenAiMessage` with `Split(':', 2)`. If a tool name or result contains a colon the parsing silently produces wrong output. The `BuildToolLoopFallbackResponse` method also re-parses this format (`Agent.cs:241`).

**Suggestion:** Introduce a dedicated `ToolResultMessage` type or add `ToolName` and `ToolResult` fields to `ChatMessage`. Alternatively, use a structured `record ToolResultPayload(string ToolName, string Result)` serialized to JSON as the message content. Remove the split-and-reformat logic from the OpenAI provider.

---

### 5. `ReplyAsync` is too long and does too much

**File:** `Core/Agent.cs:97-202`

The method handles: lazy initialization, context creation, middleware pipeline construction, tool call dispatch loop, duplicate tool call detection, history persistence, and embedding storage — 105 lines in a single method.

**Suggestion:** Extract into smaller private methods:
- `BuildPipeline()` — construct the middleware handler chain (currently rebuilt on every call)
- `ExecuteToolCallsAsync()` — the `while (response.HasToolCalls)` loop
- `PersistToMemoryAsync()` — the memory + embedding storage block

The middleware pipeline in particular (`foreach (var middleware in _middlewares.Reverse())`) is rebuilt on every `ReplyAsync` call and could be built once in the constructor.

---

## Medium Priority

### 6. `_initialized` guard repeated in every `IMemoryService` method

**File:** `Core/InMemoryMemoryService.cs:19,31,69,79`

The `if (!_initialized) throw` pattern is copy-pasted into four methods. `SqliteMemoryService` has the same pattern.

**Suggestion:** Extract to a shared helper or a base class. Alternatively, use a `ThrowIfNotInitialized()` private method.

---

### 7. `HttpClient` is instantiated as a field directly

**File:** `Providers/OpenAi/OpenAiChatModelProvider.cs:46`

```csharp
private readonly HttpClient _httpClient = new();
```

Creating `HttpClient` instances directly is the classic socket-exhaustion anti-pattern. Because `OpenAiChatModel` is created fresh by `IModelProvider.CreateModel()` on each `Build()` call, every agent gets its own `HttpClient` instance that is never pooled.

**Suggestion:** Accept an `HttpClient` via constructor injection (or `IHttpClientFactory` if the library targets DI consumers). At minimum, make `HttpClient` a static or shared instance.

---

### 8. `OpenAiProviderOptions` is defined in `OpenAiChatModelProvider.cs`

**File:** `Providers/OpenAi/OpenAiChatModelProvider.cs:7`

`OpenAiProviderOptions` is a public type but it lives inside the same file as the provider implementation. It is a configuration options class and belongs in its own file.

**Suggestion:** Move `OpenAiProviderOptions` to `Providers/OpenAi/OpenAiProviderOptions.cs`.

---

### 9. `ToolParameterBinder` uses reflection on every invocation

**File:** `Core/ToolParameterBinder.cs:31`

```csharp
var property = tool.GetType().GetProperty(param.Name);
```

`GetProperty` via reflection is called for every parameter on every tool call. For a chatbot handling many requests this adds up.

**Suggestion:** Cache `PropertyInfo` objects in a `ConcurrentDictionary<(Type, string), PropertyInfo>` keyed by `(toolType, paramName)`, or use compiled expression trees / `ILEmit` for the setters.

---

### 10. `CosineSimilarity` is duplicated

**File:** `Core/InMemoryMemoryService.cs:101`

The same cosine similarity algorithm (dot product, two norms, guard for zero vectors) will be needed in any future memory service implementation. It is currently a private static method buried inside `InMemoryMemoryService`.

**Suggestion:** Extract to an `internal static class VectorMath` in `Core/` so it can be reused by `SqliteMemoryService` and future vector store implementations.

---

### 11. `JsonSerializerOptions` instances are created per call

**File:** `Abstractions/ToolParameterSchema.cs:42` and `Abstractions/ToolParameterSchema.cs:138`

```csharp
var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
// ...
public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
```

`JsonSerializerOptions` is expensive to construct and is designed to be reused. Creating it inline causes unnecessary allocations.

**Suggestion:** Define `private static readonly JsonSerializerOptions` fields or use `JsonSerializerOptions.Default`.

---

### 12. Test helpers are duplicated across test files

**Files:** `tests/Agentic.Tests/AgentBuilderTests.cs`, `ToolExecutionTests.cs`, `StructuredToolParametersTests.cs`, `MiddlewareTests.cs`, etc.

Each test file declares its own private `FakeModel`, `FakeTool`, `FakeMemoryService`, and `FakeMiddleware` helper classes that are near-identical across files.

**Suggestion:** Extract shared test doubles to a `tests/Agentic.Tests/Fakes/` directory (e.g., `FakeAgentModel.cs`, `FakeTool.cs`, `FakeMemoryService.cs`). This reduces test maintenance when interfaces change.

---

## Low Priority

### 13. Magic strings for OpenAI API URL and role names

**File:** `Providers/OpenAi/OpenAiChatModelProvider.cs:72,155`

```csharp
"https://api.openai.com/v1/chat/completions"
// and
message.Role.ToString().ToLowerInvariant()
```

The API endpoint is a hardcoded magic string. The role conversion (`ToLowerInvariant()`) assumes the `ChatRole` enum value names exactly match OpenAI's string values, which is an implicit coupling.

**Suggestion:** Define the endpoint as a `private const string`, and map `ChatRole` to OpenAI role strings explicitly (e.g., via a `switch` expression) rather than relying on `.ToString().ToLower()`.

---

### 14. `BuildToolCatalogMessage` rebuilds on every request

**File:** `Core/Agent.cs:204`

The tool catalog system prompt is reconstructed from scratch on every `ReplyAsync` call, including re-serializing the full JSON parameter schemas.

**Suggestion:** Cache the tool catalog message string after the first build (or at construction time in `AgentBuilder.Build()`), since tools don't change at runtime.

---

### 15. `AgentBuilder.WithOpenAi` accepts `IReadOnlyList<OpenAiFunctionToolDefinition>` — a leaky abstraction

**File:** `Builder/AgentBuilder.cs:31`

The builder's `WithOpenAi` overload exposes an OpenAI-specific type (`OpenAiFunctionToolDefinition`) in its public API surface. This means the `AgentBuilder` (which is supposed to be provider-agnostic) now has a direct dependency on the OpenAI provider type.

**Suggestion:** Remove this parameter from `WithOpenAi` in the builder. Tools registered via `WithTool(ITool)` should be the only tool registration path. The `OpenAiFunctionToolDefinition` path is redundant and provider-specific.

---

### 16. `Regex.IsMatch` in `ToolParameterBinder` is not compiled

**File:** `Core/ToolParameterBinder.cs:162`

```csharp
if (!Regex.IsMatch(stringVal, param.Pattern))
```

`Regex.IsMatch` with a plain string pattern constructs a new `Regex` object each time (unless the internal cache happens to hit). For tools called frequently, this is wasteful.

**Suggestion:** Pre-compile the regex when `ToolParameterMetadata` is constructed and store it as a `Regex?` field, or use `[GeneratedRegex]` if the pattern is known at compile time.

---

### 17. `Guid.NewGuid()` used as memory message IDs

**File:** `Core/Agent.cs:187,189`

```csharp
var userId = Guid.NewGuid().ToString("N");
// ...
var responseId = Guid.NewGuid().ToString("N");
```

Random GUIDs are used as IDs for memory messages, making it impossible to later retrieve or deduplicate a specific message. There is no connection between the stored memory entry and the conversation history entry.

**Suggestion:** Use a deterministic ID scheme (e.g., based on conversation session ID + message index, or a hash of the content + timestamp) to make memory entries addressable and deduplicate-able.

---

### 18. `using` directive mismatch in `ToolParameterSchema.cs`

**File:** `Abstractions/ToolParameterSchema.cs:3-4`

```csharp
namespace Agentic.Abstractions;

using System.Text.Json;
using System.Text.Json.Serialization;
```

The `using` directives appear *after* the namespace declaration. While valid C#, this is inconsistent with every other file in the project where `using` directives precede the `namespace` statement.

**Suggestion:** Move the `using` directives above the `namespace` declaration to be consistent.

---

### 19. `AsEnumerable()` calls are redundant

**File:** `Core/InMemoryMemoryService.cs:39,49`

```csharp
_store.AsEnumerable().Reverse()...
```

`List<T>` already implements `IEnumerable<T>`. The `AsEnumerable()` call adds nothing.

**Suggestion:** Remove the `AsEnumerable()` calls.

---

### 20. No `IDisposable` / `IAsyncDisposable` on `Agent`

**File:** `Core/Agent.cs`

`Agent` holds `IMemoryService` (which may implement `IDisposable`, as `SqliteMemoryService` does) and `IEmbeddingProvider`, but `Agent` itself doesn't implement `IDisposable`. Consumers have no way to deterministically release the underlying SQLite connection or HTTP client through the `Agent` abstraction.

**Suggestion:** Implement `IAsyncDisposable` on `Agent` and dispose owned resources (`_memoryService`, `_embeddingProvider`) if they implement `IDisposable`/`IAsyncDisposable`.
