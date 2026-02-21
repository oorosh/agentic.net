using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Please set the OPENAI_API_KEY environment variable.");
    return;
}

var assistant = new AgentBuilder()
    .WithModelProvider(new OpenAiChatModelProvider(
        apiKey,
        tools:
        [
            new OpenAiFunctionToolDefinition(
                "get_weather",
                "Get weather for a city.",
                [new OpenAiFunctionToolParameter("city", "string", "City name")])
        ]))
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
