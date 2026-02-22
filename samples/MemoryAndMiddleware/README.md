# Memory and Middleware Sample

This sample demonstrates how to add memory services and custom middleware to an Agentic.NET assistant. It shows both basic memory usage and optional semantic memory with embeddings.

## Key Features Demonstrated

- **Memory Services**: Uses `IMemoryService` to store and retrieve conversation history
- **Custom Middleware**: Implements `IAssistantMiddleware` to modify conversation behavior
- **Context Factory**: Custom `IAssistantContextFactory` for additional context management
- **Semantic Memory (Optional)**: Enables embeddings for improved context relevance when `USE_EMBEDDINGS=true`
- **Vector Storage**: Supports in-memory and pgvector for embedding storage
- **In-Memory Storage**: Uses `InMemoryMemoryService` for non-persistent memory

## Running the Sample

### Without Embeddings

```bash
dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj
```

### With Semantic Embeddings (In-Memory)

```bash
USE_EMBEDDINGS=true OPENAI_API_KEY=your_key dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj
```

### With Semantic Embeddings (PgVector)

```bash
USE_EMBEDDINGS=true USE_PGVECTOR=true PGVECTOR_CONNECTION_STRING="Host=localhost;Database=memory" OPENAI_API_KEY=your_key dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj
```

The sample runs a fixed conversation and shows how memory retains context:

```
== Memory + Middleware Sample ==

Assistant: I noted: My favorite language is C#.
Assistant: Your favorite language is C#.

History:
- user: My favorite language is C#.
- assistant: I noted: My favorite language is C#.
- user: What is my favorite language?
- assistant: Your favorite language is C#.
```

## Code Highlights

### Memory Configuration

```csharp
var builder = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
    .WithMemory(new InMemoryMemoryService())
    .WithContextFactory(new DemoContextFactory())
    .UseMiddleware(new ToneMiddleware());
```

### Optional Embeddings Setup

```csharp
IEmbeddingProvider? embeddingProvider = null;
IVectorStore? vectorStore = null;

var useEmbeddings = Environment.GetEnvironmentVariable("USE_EMBEDDINGS")?.ToLower() == "true";
var usePgVector = Environment.GetEnvironmentVariable("USE_PGVECTOR")?.ToLower() == "true";

if (useEmbeddings)
{
    embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
    await embeddingProvider.InitializeAsync();

    if (usePgVector)
    {
        vectorStore = new PgVectorStore(connString, dimensions: embeddingProvider.Dimensions);
    }
    else
    {
        vectorStore = new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions);
    }
}

if (embeddingProvider != null)
{
    builder = builder
        .WithEmbeddingProvider(embeddingProvider)
        .WithVectorStore(vectorStore!);
}
```

### Custom Middleware

```csharp
public sealed class ToneMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, "Keep responses concise and friendly."));
        return await next(context, cancellationToken);
    }
}
```

### Context Factory

```csharp
public sealed class DemoContextFactory : IAssistantContextFactory
{
    public AgentContext Create(string input, IReadOnlyList<ChatMessage> history)
    {
        var context = new AgentContext(input, history);
        context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, "ContextFactory: include memory-aware behavior."));
        return context;
    }
}
```

This sample shows how to extend Agentic.NET with custom behavior while leveraging built-in memory capabilities.