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

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? OpenAiModels.Gpt4oMini;

var assistant = new AgentBuilder()
    .WithOpenAi(apiKey, model)
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
    public string Description => "Get the current weather for a city.";

    [ToolParameter(Description = "The name of the city", Required = true)]
    public string City { get; set; } = "";

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var city = string.IsNullOrWhiteSpace(City) ? "Belgrade" : City;
        var report = $"{city}: 5°C, clear sky";
        return Task.FromResult(report);
    }
}
