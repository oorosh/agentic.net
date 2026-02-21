using System.Data;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

// PersonalAssistant sample: uses the OpenAI Chat Completion API as the
// model provider and persists conversation memory into a simple SQLite
// database.  Set the environment variable OPENAI_API_KEY before running
// the program.

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Please set the OPENAI_API_KEY environment variable.");
    return;
}

var memoryPath = "memory.db";
using var memoryService = new SqliteMemoryService(memoryPath);
await memoryService.InitializeAsync();

var restored = await memoryService.RetrieveRelevantAsync(string.Empty, topK: 100);
if (restored.Count > 0)
{
    Console.WriteLine($"(loaded {restored.Count} items from memory)");
}

var assistant = new AgentBuilder()
    .WithModelProvider(new OpenAiModelProvider(apiKey))
    .WithMemory(memoryService)
    .Build();

Console.WriteLine("== OpenAI + SQLite Memory Sample ==" +
                  "\nType a prompt and press Enter. Type 'exit' to quit.\n");

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


// implementation of model provider -------------------------------------------------------

public sealed class OpenAiModelProvider : IModelProvider
{
    private readonly string _apiKey;
    public OpenAiModelProvider(string apiKey) => _apiKey = apiKey;

    public IAgentModel CreateModel() => new OpenAiModel(_apiKey);
}

public sealed class OpenAiModel : IAgentModel
{
    private readonly string _apiKey;
    private readonly HttpClient _http = new HttpClient();

    public OpenAiModel(string apiKey)
    {
        _apiKey = apiKey;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = "gpt-4o-mini", // or whichever model you like
            messages = messages.Select(m => new { role = m.Role.ToString().ToLower(), content = m.Content })
        };

        var content = new StringContent(JsonSerializer.Serialize(payload));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var resp = await _http.PostAsync("https://api.openai.com/v1/chat/completions", content, cancellationToken);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return new AgentResponse(text ?? string.Empty);
    }
}
