# Tool Calling Sample

This sample demonstrates how to add tool-calling capabilities to an Agentic.NET assistant. It shows how models can invoke external functions during conversations.

## Key Features Demonstrated

- **MEAI Integration**: Uses any `IChatClient` from Microsoft.Extensions.AI
- **Tool Registration**: Registers tools using `WithTool()` and property-based parameter definitions
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

Register tools with `WithTool()`. Tool parameters are declared as properties decorated with `[ToolParameter]` — Agentic.NET automatically generates the function schema for the model.

```csharp
using Microsoft.Extensions.AI;
using OpenAI;

var chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");

var assistant = new AgentBuilder()
    .WithChatClient(chatClient)
    .WithTool(new GetWeatherTool())
    .Build();
```

### Tool Implementation

```csharp
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
```

## How It Works

1. **Tool Declaration**: Properties decorated with `[ToolParameter]` define the function schema sent to the model
2. **Tool Registration**: `WithTool()` registers the tool with the agent
3. **Function Calling**: When the user asks about weather, the model responds with a function call
4. **Execution**: Agentic.NET deserializes the arguments into the tool's properties and invokes `InvokeAsync`

This sample shows how to extend AI assistants with external capabilities, enabling more powerful and interactive applications.