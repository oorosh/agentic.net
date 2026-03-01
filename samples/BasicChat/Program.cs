using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

var assistant = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
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

public sealed class DemoModelProvider : IModelProvider
{
    public IAgentModel CreateModel() => new EchoModel();
}

public sealed class EchoModel : IAgentModel
{
    public Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;
        return Task.FromResult(new AgentResponse($"Echo: {lastUserMessage}"));
    }

    public async IAsyncEnumerable<StreamingToken> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await CompleteAsync(messages, cancellationToken);
        if (!string.IsNullOrEmpty(response.Content))
            yield return new StreamingToken(response.Content, IsComplete: false);
        yield return new StreamingToken(string.Empty, IsComplete: true);
    }
}
