// McpServer sample — shows how to connect an Agentic.NET agent to an MCP server.
//
// This sample runs a lightweight in-process MCP server so it works out of the box
// without any external tools. Swap in the real-server examples below when you have
// a stdio or HTTP MCP server available.

using System.IO.Pipelines;
using Agentic.Builder;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// ---------------------------------------------------------------------------
// 1. Build an in-process MCP server with a few demo tools
// ---------------------------------------------------------------------------

Pipe clientToServer = new(), serverToClient = new();

await using var mcpServer = McpServer.Create(
    new StreamServerTransport(
        clientToServer.Reader.AsStream(),
        serverToClient.Writer.AsStream()),
    new McpServerOptions
    {
        ServerInfo = new() { Name = "Demo MCP Server", Version = "1.0" },
        ToolCollection =
        [
            // A simple echo tool
            McpServerTool.Create(
                (string text) => $"Echo: {text}",
                new McpServerToolCreateOptions
                {
                    Name = "echo",
                    Description = "Echoes the given text back to the caller."
                }),

            // A unit-converter tool
            McpServerTool.Create(
                (double celsius) => $"{celsius}°C = {celsius * 9 / 5 + 32:F1}°F",
                new McpServerToolCreateOptions
                {
                    Name = "celsius_to_fahrenheit",
                    Description = "Converts a temperature from Celsius to Fahrenheit."
                }),

            // A word-count tool
            McpServerTool.Create(
                (string sentence) => sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.ToString(),
                new McpServerToolCreateOptions
                {
                    Name = "word_count",
                    Description = "Counts the number of words in a sentence."
                }),
        ]
    });

// Start the server in the background
_ = mcpServer.RunAsync();

// ---------------------------------------------------------------------------
// 2. Connect a client to the in-process server via the matching stream transport
// ---------------------------------------------------------------------------

var transport = new StreamClientTransport(
    clientToServer.Writer.AsStream(),
    serverToClient.Reader.AsStream());

// ---------------------------------------------------------------------------
// 3. Build the agent and wire in the MCP server
//    The agent connects and discovers tools automatically on InitializeAsync().
// ---------------------------------------------------------------------------
//
// Real-world alternatives:
//
//   Stdio server (e.g. npx @modelcontextprotocol/server-filesystem):
//     .WithMcpServer(new StdioClientTransportOptions
//     {
//         Name = "filesystem",
//         Command = "npx",
//         Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "."]
//     })
//
//   HTTP / SSE server:
//     .WithMcpServer(new Uri("http://localhost:3001"))
//
//   Pre-connected client (you manage the lifetime):
//     await using var client = await McpClient.CreateAsync(transport);
//     builder.WithMcpClient(client)

var agent = new AgentBuilder()
    .WithChatClient(new DemoChatClient())
    .WithMcpServer(transport)          // transport → agent connects on InitializeAsync
    .Build();

await agent.InitializeAsync();

// ---------------------------------------------------------------------------
// 4. Chat loop
// ---------------------------------------------------------------------------

Console.WriteLine("== MCP Server Sample ==");
Console.WriteLine("Connected to in-process MCP server with 3 tools:");
Console.WriteLine("  • echo           — echoes text back");
Console.WriteLine("  • celsius_to_fahrenheit — converts temperature");
Console.WriteLine("  • word_count     — counts words in a sentence");
Console.WriteLine();
Console.WriteLine("Type a prompt and press Enter. Try:");
Console.WriteLine("  echo hello world");
Console.WriteLine("  convert 100 degrees celsius");
Console.WriteLine("  count words in 'the quick brown fox'\n");
Console.WriteLine("Type 'exit' to quit.\n");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) continue;
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase)) break;

    var reply = await agent.ReplyAsync(input);
    Console.WriteLine($"Assistant: {reply}\n");
}

// ---------------------------------------------------------------------------
// Demo chat client — echoes back a message referencing the available tools.
// Replace with a real IChatClient (OpenAI, Anthropic, Ollama, …) to get actual
// LLM-driven tool calling.
// ---------------------------------------------------------------------------

public sealed class DemoChatClient : IChatClient
{
    private static readonly string[] ToolKeywords = ["echo", "celsius", "fahrenheit", "count", "word"];

    public ChatClientMetadata Metadata => new("demo", null, null);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var userText = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";

        // If the system prompt lists any of our MCP tool names, acknowledge them.
        var systemMessages = messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? "")
            .ToList();

        var toolsListed = systemMessages.Any(s => ToolKeywords.Any(k =>
            s.Contains(k, StringComparison.OrdinalIgnoreCase)));

        var response = toolsListed
            ? $"[Demo] I can see the MCP tools in my context. You asked: \"{userText}\". " +
              "(Swap DemoChatClient with a real LLM to get actual tool invocation.)"
            : $"[Demo] Echo: {userText}";

        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
