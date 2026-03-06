using Agentic.Builder;
using Microsoft.Extensions.AI;

var assistant = new AgentBuilder()
    .WithChatClient(new DemoChatClient())
    .Build();

Console.WriteLine("== Basic Chat Sample ==");
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
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Echo: {last}")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        yield return new ChatResponseUpdate(ChatRole.Assistant, $"Echo: {last}");
        yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
