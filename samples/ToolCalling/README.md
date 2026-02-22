# Tool Calling Sample

This sample demonstrates how to add tool-calling capabilities to an Agentic.NET assistant using OpenAI's function calling feature. It shows how models can invoke external functions during conversations.

## Key Features Demonstrated

- **OpenAI Integration**: Uses the OpenAI provider with function calling support
- **Tool Registration**: Registers tools using `WithTool()` and OpenAI function definitions
- **Function Calling**: Implements `ITool` interface for executable functions
- **Interactive Tool Use**: Shows how the assistant can call tools based on user input
- **Mock Implementation**: Uses a simulated weather tool for demonstration

## Prerequisites

Set the `OPENAI_API_KEY` environment variable:

```bash
export OPENAI_API_KEY=your_openai_api_key
```

## Running the Sample

```bash
dotnet run --project samples/ToolCalling/ToolCalling.csproj
```

The sample starts an interactive session where you can ask about weather:

```
== Tool Calling Sample ==
Type: what's the weather in <city> (or 'exit')

> What's the weather in Tokyo?
Assistant: The weather in Tokyo is 5°C with clear skies.

> How about Paris?
Assistant: Paris: 5°C, clear sky
```

## Code Highlights

### Tool Registration

```csharp
var assistant = new AgentBuilder()
    .WithOpenAi(
        apiKey,
        model,
        tools: [
            new OpenAiFunctionToolDefinition(
                "get_weather",
                "Get weather for a city.",
                [new OpenAiFunctionToolParameter("city", "string", "City name")])
        ])
    .WithTool(new GetWeatherTool())
    .Build();
```

### Tool Implementation

```csharp
public sealed class GetWeatherTool : ITool
{
    public string Name => "get_weather";
    public string Description => "Returns a mocked weather report for a city.";

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var city = string.IsNullOrWhiteSpace(arguments) ? "Belgrade" : arguments;
        var report = $"{city}: 5°C, clear sky";
        return Task.FromResult(report);
    }
}
```

## How It Works

1. **Tool Definition**: The `OpenAiFunctionToolDefinition` tells OpenAI about available functions
2. **Tool Implementation**: The `ITool` class provides the actual function logic
3. **Function Calling**: When the user asks about weather, OpenAI responds with a function call
4. **Execution**: Agentic.NET executes the tool and includes the result in the conversation

This sample shows how to extend AI assistants with external capabilities, enabling more powerful and interactive applications.