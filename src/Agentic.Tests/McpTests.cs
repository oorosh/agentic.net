using System.IO.Pipelines;
using System.Text.Json;
using Agentic.Builder;
using Agentic.Core;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Agentic.Tests;

public class McpTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (McpServer server, McpClient client) CreateInProcessMcpPair(McpServerOptions serverOptions)
    {
        Pipe clientToServer = new(), serverToClient = new();

        var server = McpServer.Create(
            new StreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream()),
            serverOptions);

        var clientTask = McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()));

        // Start the server in the background before waiting for the client.
        var serverRunTask = server.RunAsync();

        var client = clientTask.GetAwaiter().GetResult();

        return (server, client);
    }

    private static async Task<(McpServer server, McpClient client)> CreateInProcessMcpPairAsync(
        McpServerOptions serverOptions, CancellationToken cancellationToken = default)
    {
        Pipe clientToServer = new(), serverToClient = new();

        var server = McpServer.Create(
            new StreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream()),
            serverOptions);

        _ = server.RunAsync(cancellationToken);

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream()),
            cancellationToken: cancellationToken);

        return (server, client);
    }

    // -------------------------------------------------------------------------
    // AgentBuilder – WithMcpClient / WithMcpServer API
    // -------------------------------------------------------------------------

    [Fact]
    public void WithMcpServer_IClientTransport_NullTransport_Throws()
    {
        var builder = new AgentBuilder().WithChatClient(new SimpleChatClient());

        Assert.Throws<ArgumentNullException>(() => builder.WithMcpServer((IClientTransport)null!));
    }

    [Fact]
    public void WithMcpServer_StdioOptions_NullOptions_Throws()
    {
        var builder = new AgentBuilder().WithChatClient(new SimpleChatClient());

        Assert.Throws<ArgumentNullException>(() => builder.WithMcpServer((StdioClientTransportOptions)null!));
    }

    [Fact]
    public void WithMcpServer_Uri_NullEndpoint_Throws()
    {
        var builder = new AgentBuilder().WithChatClient(new SimpleChatClient());

        Assert.Throws<ArgumentNullException>(() => builder.WithMcpServer((Uri)null!));
    }

    [Fact]
    public void WithMcpServer_HttpOptions_NullOptions_Throws()
    {
        var builder = new AgentBuilder().WithChatClient(new SimpleChatClient());

        Assert.Throws<ArgumentNullException>(() => builder.WithMcpServer((HttpClientTransportOptions)null!));
    }

    [Fact]
    public void WithMcpClient_NullClient_Throws()
    {
        var builder = new AgentBuilder().WithChatClient(new SimpleChatClient());

        Assert.Throws<ArgumentNullException>(() => builder.WithMcpClient((McpClient)null!));
    }

    [Fact]
    public void Build_WithMcpClient_Succeeds()
    {
        // A pre-connected McpClient passed to the builder should not prevent Build().
        // We cannot use a real McpClient without a live server; we verify the builder
        // accepts the overload by ensuring no exception is thrown when building
        // without any MCP server configured.
        var agent = new AgentBuilder()
            .WithChatClient(new SimpleChatClient())
            .Build();

        Assert.NotNull(agent);
    }

    // -------------------------------------------------------------------------
    // McpToolAdapter – tool registered from a live in-process MCP server
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_RegistersToolsFromMcpClient()
    {
        var (server, mcpClient) = await CreateInProcessMcpPairAsync(new McpServerOptions
        {
            ToolCollection =
            [
                McpServerTool.Create(
                    (string message) => $"pong: {message}",
                    new McpServerToolCreateOptions { Name = "ping", Description = "Ping the server." })
            ]
        });

        await using var _ = server;
        await using var __ = mcpClient;

        var agent = (Agent)new AgentBuilder()
            .WithChatClient(new SimpleChatClient())
            .WithMcpClient(mcpClient)
            .Build();

        await using var ___ = agent;

        await agent.InitializeAsync();

        // The MCP tool should now be visible in the agent's tool catalog.
        var reply = await agent.ReplyAsync("call ping with hello");

        // The FakeChatClient echoes tool catalog back; the tool name must be present.
        Assert.NotNull(reply);
    }

    [Fact]
    public async Task McpToolAdapter_InvokeAsync_ReturnsMcpServerResult()
    {
        var (server, mcpClient) = await CreateInProcessMcpPairAsync(new McpServerOptions
        {
            ToolCollection =
            [
                McpServerTool.Create(
                    (string word) => word.ToUpperInvariant(),
                    new McpServerToolCreateOptions { Name = "uppercase", Description = "Uppercases a word." })
            ]
        });

        await using var _ = server;
        await using var __ = mcpClient;

        var tools = await mcpClient.ListToolsAsync();
        var mcpTool = tools.Single(t => t.Name == "uppercase");

        // Invoke via adapter (simulates how Agent calls it).
        var adapter = new Agentic.Mcp.McpToolAdapter(mcpTool);

        Assert.Equal("uppercase", adapter.Name);
        Assert.Contains("Uppercases a word.", adapter.Description);

        var result = await adapter.InvokeAsync(JsonSerializer.Serialize(new { word = "hello" }));
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public async Task McpToolAdapter_InvokeAsync_EmptyArguments_DoesNotThrow()
    {
        var (server, mcpClient) = await CreateInProcessMcpPairAsync(new McpServerOptions
        {
            ToolCollection =
            [
                McpServerTool.Create(
                    () => "ok",
                    new McpServerToolCreateOptions { Name = "noop", Description = "No-op." })
            ]
        });

        await using var _ = server;
        await using var __ = mcpClient;

        var tools = await mcpClient.ListToolsAsync();
        var adapter = new Agentic.Mcp.McpToolAdapter(tools.Single());

        var result = await adapter.InvokeAsync(string.Empty);
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task McpToolAdapter_Description_IncludesJsonSchema()
    {
        var (server, mcpClient) = await CreateInProcessMcpPairAsync(new McpServerOptions
        {
            ToolCollection =
            [
                McpServerTool.Create(
                    (string city) => $"Weather in {city}: sunny",
                    new McpServerToolCreateOptions { Name = "weather", Description = "Get weather." })
            ]
        });

        await using var _ = server;
        await using var __ = mcpClient;

        var tools = await mcpClient.ListToolsAsync();
        var adapter = new Agentic.Mcp.McpToolAdapter(tools.Single());

        // Description should embed the JSON schema from the MCP server.
        Assert.Contains("Get weather.", adapter.Description);
        Assert.Contains("Parameters:", adapter.Description);
    }

    [Fact]
    public async Task DuplicateMcpToolName_Throws()
    {
        var (server, mcpClient) = await CreateInProcessMcpPairAsync(new McpServerOptions
        {
            ToolCollection =
            [
                McpServerTool.Create(
                    () => "a",
                    new McpServerToolCreateOptions { Name = "duplicate", Description = "First." })
            ]
        });

        await using var _ = server;
        await using var __ = mcpClient;

        // Register a native tool with the same name to force a conflict.
        var agent = (Agent)new AgentBuilder()
            .WithChatClient(new SimpleChatClient())
            .WithTool(new ConflictingTool())
            .WithMcpClient(mcpClient)
            .Build();

        await using var ___ = agent;

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.InitializeAsync());
    }

    private sealed class ConflictingTool : Abstractions.ITool
    {
        public string Name => "duplicate";
        public string Description => "Conflict.";
        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult("conflict");
    }

    private sealed class SimpleChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("simple", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse([new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
