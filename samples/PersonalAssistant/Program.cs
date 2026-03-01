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
var useEmbeddings = Environment.GetEnvironmentVariable("USE_EMBEDDINGS")?.ToLower() == "true";
var usePgVector = Environment.GetEnvironmentVariable("USE_PGVECTOR")?.ToLower() == "true";

var builder = new AgentBuilder()
    .WithOpenAi(apiKey, model: model)
    .WithMemory(new SqliteMemoryService());

if (useEmbeddings)
{
    if (usePgVector)
    {
        // Production: pgvector for scalable semantic search
        var connString = Environment.GetEnvironmentVariable("PGVECTOR_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connString))
        {
            Console.WriteLine("Warning: USE_PGVECTOR=true but PGVECTOR_CONNECTION_STRING not set, falling back to in-memory vector store.");
        }

        var embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
        await embeddingProvider.InitializeAsync();
        var vectorStore = string.IsNullOrWhiteSpace(connString)
            ? (Agentic.Abstractions.IVectorStore)new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions)
            : new PgVectorStore(connString, dimensions: embeddingProvider.Dimensions);

        builder = builder
            .WithMemory(new SqliteMemoryService(vectorStore))
            .WithEmbeddingProvider(embeddingProvider)
            .WithVectorStore(vectorStore);

        Console.WriteLine(string.IsNullOrWhiteSpace(connString)
            ? "(embeddings enabled with in-memory vector store)"
            : "(embeddings enabled with pgvector)");
    }
    else
    {
        // Development: in-memory vector store — single convenience call
        builder = builder.WithSemanticMemory(apiKey);
        Console.WriteLine("(embeddings enabled with in-memory vector store)");
    }
}

var assistant = builder.Build();

Console.WriteLine("== OpenAI + SQLite Memory Sample ==" +
                  "\nType a prompt and press Enter. Type 'exit' to quit." +
                  (useEmbeddings ? " (with embeddings)" : "") + "\n");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;

    var reply = await assistant.ReplyAsync(input);
    Console.WriteLine($"Assistant: {reply.Content}\n");
}
