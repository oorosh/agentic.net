using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Middleware;
using Agentic.Stores;

// MemoryAndMiddleware sample: demonstrates memory services and custom middleware.
// Now supports embeddings for semantic memory retrieval (set OPENAI_API_KEY and USE_EMBEDDINGS=true).
// For production with larger datasets, use WithVectorStore() with PgVectorStore.

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
IEmbeddingProvider? embeddingProvider = null;
IVectorStore? vectorStore = null;

var useEmbeddings = Environment.GetEnvironmentVariable("USE_EMBEDDINGS")?.ToLower() == "true";
var usePgVector = Environment.GetEnvironmentVariable("USE_PGVECTOR")?.ToLower() == "true";

if (!string.IsNullOrWhiteSpace(apiKey) && useEmbeddings)
{
    embeddingProvider = new Agentic.Providers.OpenAi.OpenAiEmbeddingProvider(apiKey);
    await embeddingProvider.InitializeAsync();

    // Use pgvector for production (requires PostgreSQL with pgvector extension)
    // Set USE_PGVECTOR=true and PGVECTOR_CONNECTION_STRING environment variables
    if (usePgVector)
    {
        var connString = Environment.GetEnvironmentVariable("PGVECTOR_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connString))
        {
            vectorStore = new PgVectorStore(connString, dimensions: embeddingProvider.Dimensions);
            Console.WriteLine("(using pgvector for semantic memory)");
        }
    }
    else
    {
        // Use in-memory vector store for development/testing
        vectorStore = new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions);
    }
}

var builder = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
    .WithMemory(new InMemoryMemoryService())
    .WithContextFactory(new DemoContextFactory())
    .UseMiddleware(new ToneMiddleware());

if (embeddingProvider != null)
{
    builder = builder
        .WithEmbeddingProvider(embeddingProvider)
        .WithVectorStore(vectorStore!);
}

var assistant = builder.Build();

Console.WriteLine("== Memory + Middleware Sample ==\n");

var first = await assistant.ReplyAsync("My favorite language is C#.");
Console.WriteLine($"Assistant: {first}");

var second = await assistant.ReplyAsync("What is my favorite language?");
Console.WriteLine($"Assistant: {second}\n");

Console.WriteLine("History:");
foreach (var message in assistant.History)
{
    Console.WriteLine($"- {message.Role}: {message.Content}");
}

public sealed class DemoModelProvider : IModelProvider
{
    public IAgentModel CreateModel() => new DemoModel();
}

public sealed class DemoModel : IAgentModel
{
    public Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var systemContext = messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Content)
            .ToList();

        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;

        if (lastUserMessage.Contains("favorite language", StringComparison.OrdinalIgnoreCase)
            && systemContext.Any(ctx => ctx.Contains("C#", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(new AgentResponse("Your favorite language is C#."));
        }

        return Task.FromResult(new AgentResponse($"I noted: {lastUserMessage}"));
    }
}

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

public sealed class DemoContextFactory : IAssistantContextFactory
{
    public AgentContext Create(string input, IReadOnlyList<ChatMessage> history)
    {
        var context = new AgentContext(input, history);
        context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, "ContextFactory: include memory-aware behavior."));
        return context;
    }
}
