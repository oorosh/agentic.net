# Personal Assistant Sample

This sample demonstrates a complete AI personal assistant using OpenAI with persistent SQLite memory storage and optional semantic embeddings for enhanced context recall.

## Key Features Demonstrated

- **OpenAI Integration**: Uses OpenAI Chat Completion API as the model provider
- **Persistent Memory**: Stores conversation history in SQLite database (`memory.db`)
- **Semantic Memory (Optional)**: Enables vector embeddings for better context relevance
- **Memory Restoration**: Loads previous conversations on startup
- **Real AI Responses**: Unlike demo models, this uses actual OpenAI for intelligent responses
- **Cross-Session Continuity**: Conversations persist between program runs

## Prerequisites

Set the `OPENAI_API_KEY` environment variable:

```bash
export OPENAI_API_KEY=your_openai_api_key
```

## Running the Sample

### Without Embeddings

```bash
dotnet run --project samples/PersonalAssistant/PersonalAssistant.csproj
```

### With Semantic Embeddings

```bash
USE_EMBEDDINGS=true dotnet run --project samples/PersonalAssistant/PersonalAssistant.csproj
```

The sample loads any existing memory and starts an interactive session:

```
(loaded 5 items from memory)
(embeddings enabled for semantic memory)
== OpenAI + SQLite Memory Sample ==
Type a prompt and press Enter. Type 'exit' to quit. (with embeddings)

> Remember that I love coffee
Assistant: I'll remember that you love coffee. Is there anything specific about coffee you'd like to tell me?

> What do I love?
Assistant: You mentioned that you love coffee. Is there anything else you'd like to know or discuss?
```

## Code Highlights

### Memory Setup with SQLite

```csharp
using var memoryService = new SqliteMemoryService();
await memoryService.InitializeAsync();
```

### Optional Embeddings Configuration

```csharp
IEmbeddingProvider? embeddingProvider = null;
var useEmbeddings = Environment.GetEnvironmentVariable("USE_EMBEDDINGS")?.ToLower() == "true";
if (useEmbeddings)
{
    embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
    await embeddingProvider.InitializeAsync();
}
```

### Memory Restoration on Startup

```csharp
var restored = await memoryService.RetrieveRelevantAsync(string.Empty, topK: 100);
if (restored.Count > 0)
{
    Console.WriteLine($"(loaded {restored.Count} items from memory)");
}
```

### Agent Configuration

```csharp
var builder = new AgentBuilder()
    .WithOpenAi(apiKey, model: model)
    .WithMemory(memoryService);

if (embeddingProvider != null)
{
    builder = builder.WithEmbeddingProvider(embeddingProvider);
}
```

## Benefits of Semantic Memory

When embeddings are enabled:

- **Better Recall**: Uses cosine similarity to find semantically related conversations
- **Context Relevance**: Retrieves more relevant memories beyond keyword matching
- **Improved Continuity**: Maintains better conversation flow across sessions

## Database File

The sample creates a `memory.db` file in the current directory. This SQLite database contains:

- All conversation messages
- Optional embeddings for semantic search
- Persistent storage across application restarts

Delete `memory.db` to start with a fresh memory state.