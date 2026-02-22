using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;

// PersonalAssistant sample: uses the OpenAI Chat Completion API as the
// model provider and persists conversation memory into a simple SQLite
// database with semantic embeddings for better recall.  Set the environment
// variable OPENAI_API_KEY before running the program.

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Please set the OPENAI_API_KEY environment variable.");
    return;
}

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? OpenAiModels.Gpt4oMini;

var memoryPath = "memory.db";
using var memoryService = new SqliteMemoryService(memoryPath);
await memoryService.InitializeAsync();

IEmbeddingProvider? embeddingProvider = null;
var useEmbeddings = Environment.GetEnvironmentVariable("USE_EMBEDDINGS")?.ToLower() == "true";
if (useEmbeddings)
{
    embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
    await embeddingProvider.InitializeAsync();
}

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
    builder = builder.WithEmbeddingProvider(embeddingProvider);
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
