using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;

var assistant = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
    .WithTool(new GetWeatherTool())
    .Build();

Console.WriteLine("== Tool Calling Sample ==");
Console.WriteLine("Type: what's the weather in <city> (or 'exit')\n");

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
    public IAgentModel CreateModel() => new ToolAwareModel();
}

public sealed class ToolAwareModel : IAgentModel
{
    public Task<AgentResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var lastToolMessage = messages.LastOrDefault(m => m.Role == ChatRole.Tool);
        if (lastToolMessage is not null)
        {
            return Task.FromResult(new AgentResponse($"Here you go: {lastToolMessage.Content.Split(':', 2)[1].Trim()}"));
        }

        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? string.Empty;
        if (lastUser.Contains("weather", StringComparison.OrdinalIgnoreCase))
        {
            var city = ExtractCity(lastUser);
            var toolCalls = new List<AgentToolCall>
            {
                new("get_weather", city)
            };

            return Task.FromResult(new AgentResponse("I will check the weather tool.", toolCalls));
        }

        return Task.FromResult(new AgentResponse("Ask me about weather, for example: what's the weather in Belgrade?"));
    }

    private static string ExtractCity(string input)
    {
        const string marker = "in ";
        var idx = input.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return "Belgrade";
        }

        return input[(idx + marker.Length)..].Trim(' ', '?', '.', '!');
    }
}

public sealed class GetWeatherTool : ITool
{
    public string Name => "get_weather";
    public string Description => "Returns a mocked weather report for a city.";

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var city = string.IsNullOrWhiteSpace(arguments) ? "Belgrade" : arguments;
        var report = $"{city}: 18°C, clear sky";
        return Task.FromResult(report);
    }
}
