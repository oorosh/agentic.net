using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;
using Agentic.Stores;

// PersonalAssistant sample: uses the OpenAI Chat Completion API as the
// model provider and persists conversation memory into SQLite with optional
// vector storage for semantic embeddings.
//
// Environment variables:
//   OPENAI_API_KEY - Required for OpenAI API
//   OPENAI_MODEL - Optional, defaults to gpt-4o-mini
//   USE_EMBEDDINGS - Set to "true" to enable semantic embeddings
//   USE_PGVECTOR - Set to "true" to use PostgreSQL pgvector (requires PGVECTOR_CONNECTION_STRING)
//   PGVECTOR_CONNECTION_STRING - PostgreSQL connection string (when USE_PGVECTOR=true)

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Please set the OPENAI_API_KEY environment variable.");
    return;
}

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? OpenAiModels.Gpt4oMini;

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
        var connString = Environment.GetEnvironmentVariable("PGVECTOR_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connString))
        {
            vectorStore = new PgVectorStore(connString, dimensions: embeddingProvider.Dimensions);
            Console.WriteLine("(using pgvector for semantic memory)");
        }
        else
        {
            Console.WriteLine("Warning: USE_PGVECTOR=true but PGVECTOR_CONNECTION_STRING not set, falling back to in-memory");
            vectorStore = new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions);
        }
    }
    else
    {
        vectorStore = new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions);
    }
}

using var memoryService = vectorStore is not null 
    ? new SqliteMemoryService(vectorStore) 
    : new SqliteMemoryService();
await memoryService.InitializeAsync();

var restored = await memoryService.RetrieveRelevantAsync(string.Empty, topK: 100);
if (restored.Count > 0)
{
    Console.WriteLine($"(loaded {restored.Count} items from memory)");
}

var builder = new AgentBuilder()
    .WithOpenAi(apiKey, model: model)
    .WithMemory(memoryService);

if (embeddingProvider != null)
{
    builder = builder
        .WithEmbeddingProvider(embeddingProvider)
        .WithVectorStore(vectorStore!);
    Console.WriteLine("(embeddings enabled for semantic memory)");
}

var assistant = builder.Build();

Console.WriteLine("== OpenAI + SQLite Memory Sample ==" +
                  "\nType a prompt and press Enter. Type 'exit' to quit." +
                  (embeddingProvider != null ? " (with embeddings)" : "") + "\n");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;

    var reply = await assistant.ReplyAsync(input);
    Console.WriteLine($"Assistant: {reply}\n");
}
