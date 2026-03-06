using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Middleware;
using Agentic.Stores;
using Microsoft.Extensions.AI;
using MEAI = Microsoft.Extensions.AI;

// MemoryAndMiddleware sample: demonstrates memory services and custom middleware.
// Now supports embeddings for semantic memory retrieval (set OPENAI_API_KEY and USE_EMBEDDINGS=true).
// For production with larger datasets, use WithVectorStore() with PgVectorStore.

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
MEAI.IEmbeddingGenerator<string, MEAI.Embedding<float>>? embeddingGenerator = null;
IVectorStore? vectorStore = null;

var useEmbeddings = Environment.GetEnvironmentVariable("USE_EMBEDDINGS")?.ToLower() == "true";
var usePgVector = Environment.GetEnvironmentVariable("USE_PGVECTOR")?.ToLower() == "true";

if (!string.IsNullOrWhiteSpace(apiKey) && useEmbeddings)
{
    embeddingGenerator = new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey).AsIEmbeddingGenerator();

    // Use pgvector for production (requires PostgreSQL with pgvector extension)
    // Set USE_PGVECTOR=true and PGVECTOR_CONNECTION_STRING environment variables
    if (usePgVector)
    {
        var connString = Environment.GetEnvironmentVariable("PGVECTOR_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connString))
        {
            vectorStore = new PgVectorStore(connString, dimensions: 1536);
            Console.WriteLine("(using pgvector for semantic memory)");
        }
    }
    else
    {
        // Use in-memory vector store for development/testing
        vectorStore = new InMemoryVectorStore(dimensions: 1536);
    }
}

var builder = new AgentBuilder()
    .WithChatClient(new DemoChatClient())
    .WithMemory(new InMemoryMemoryService())
    .WithContextFactory(new DemoContextFactory())
    .WithMiddleware(new ToneMiddleware());

if (embeddingGenerator != null)
{
    builder = builder
        .WithEmbeddingGenerator(embeddingGenerator!)
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

public sealed class DemoChatClient : MEAI.IChatClient
{
    public MEAI.ChatClientMetadata Metadata => new("demo", null, null);

    public Task<MEAI.ChatResponse> GetResponseAsync(
        IEnumerable<MEAI.ChatMessage> messages,
        MEAI.ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var last = list.LastOrDefault(m => m.Role == MEAI.ChatRole.User)?.Text ?? "";
        var systemCtx = list.Where(m => m.Role == MEAI.ChatRole.System).Select(m => m.Text ?? "").ToList();
        string content;
        if (last.Contains("favorite language", StringComparison.OrdinalIgnoreCase)
            && systemCtx.Any(ctx => ctx.Contains("C#", StringComparison.OrdinalIgnoreCase)))
            content = "Your favorite language is C#.";
        else
            content = $"I noted: {last}";
        return Task.FromResult(new MEAI.ChatResponse(new MEAI.ChatMessage(MEAI.ChatRole.Assistant, content)));
    }

    public async IAsyncEnumerable<MEAI.ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<MEAI.ChatMessage> messages,
        MEAI.ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new MEAI.ChatResponseUpdate(MEAI.ChatRole.Assistant, response.Text);
        yield return new MEAI.ChatResponseUpdate { FinishReason = MEAI.ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

public sealed class ToneMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        context.WorkingMessages.Insert(0, new Agentic.Core.ChatMessage(Agentic.Core.ChatRole.System, "Keep responses concise and friendly."));
        return await next(context, cancellationToken);
    }
}

public sealed class DemoContextFactory : IAssistantContextFactory
{
    public AgentContext Create(string input, IReadOnlyList<Agentic.Core.ChatMessage> history)
    {
        var context = new AgentContext(input, history);
        context.WorkingMessages.Insert(0, new Agentic.Core.ChatMessage(Agentic.Core.ChatRole.System, "ContextFactory: include memory-aware behavior."));
        return context;
    }
}
