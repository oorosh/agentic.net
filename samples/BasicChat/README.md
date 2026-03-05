# Basic Chat Sample

This sample demonstrates the most basic usage of Agentic.NET. It shows how to:

- Create an `Agent` using the `AgentBuilder` with a custom chat client
- Implement a simple echo `IChatClient` that repeats the user's input
- Run an interactive chat loop where the user can type messages and receive responses
- Handle exit commands gracefully

## Key Features Demonstrated

- **Minimal Agent Setup**: Uses `AgentBuilder` with only a chat client
- **Custom Chat Client**: Implements `IChatClient` (from `Microsoft.Extensions.AI`) for a demo echo model
- **Interactive Chat Loop**: Reads user input from console and responds in real-time
- **Basic Error Handling**: Continues on empty input, exits on 'exit' command

## Running the Sample

```bash
dotnet run --project samples/BasicChat/BasicChat.csproj
```

The sample will start an interactive session:

```
== Basic Chat Sample ==
Type a prompt and press Enter. Type 'exit' to quit.

> Hello, world!
Assistant: Echo: Hello, world!

> How are you?
Assistant: Echo: How are you?

> exit
```

## Code Highlights

```csharp
// Create agent with custom chat client
var assistant = new AgentBuilder()
    .WithChatClient(new DemoChatClient())
    .Build();

// Interactive chat loop
while (true)
{
    var input = Console.ReadLine();
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;

    var reply = await assistant.ReplyAsync(input);
    Console.WriteLine($"Assistant: {reply}");
}
```

This sample serves as the foundation for understanding how Agentic.NET works without external dependencies.