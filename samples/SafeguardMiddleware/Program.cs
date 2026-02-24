using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Middleware;

var assistant = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
    .UseMiddleware(new SafeguardMiddleware.PromptGuardMiddleware())
    .UseMiddleware(new SafeguardMiddleware.ResponseGuardMiddleware())
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
        // Sometimes return content with "bad" to demonstrate response filtering
        var response = lastUserMessage.Contains("test") ? $"This contains bad content." : $"Echo: {lastUserMessage}";
        return Task.FromResult(new AgentResponse(response));
    }
}