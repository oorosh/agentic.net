using Agentic.Builder;
using Agentic.Middleware;
using Microsoft.Extensions.AI;

var assistant = new AgentBuilder()
    .WithChatClient(new DemoChatClient())
    .WithMiddleware(new SafeguardMiddleware.PromptGuardMiddleware())
    .WithMiddleware(new SafeguardMiddleware.ResponseGuardMiddleware())
    .Build();

Console.WriteLine("== Safeguard Middleware Sample ==");
Console.WriteLine("This sample demonstrates prompt and response safeguards.");
Console.WriteLine("Try prompts with 'bad' to see blocking/censorship.\n");
Console.WriteLine("Type a prompt and press Enter. Type 'exit' to quit.\n");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var reply = await assistant.ReplyAsync(input);
    Console.WriteLine($"Assistant: {reply}\n");
}

public sealed class DemoChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("demo", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var content = last.Contains("test") ? "This contains bad content." : $"Echo: {last}";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, content)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var content = last.Contains("test") ? "This contains bad content." : $"Echo: {last}";
        yield return new ChatResponseUpdate(ChatRole.Assistant, content);
        yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}