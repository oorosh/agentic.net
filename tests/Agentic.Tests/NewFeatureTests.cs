using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;
using Agentic.Tests.Fakes;
using Xunit;

namespace Agentic.Tests;

/// <summary>
/// Tests for features added in the most recent batch of improvements:
/// - OpenAiEmbeddingProvider custom-dimension constructor and unknown-model guard
/// - AgentBuilder.WithOpenAiEmbeddings convenience method
/// - IMemoryService.DeleteMessageAsync and ClearAsync (InMemory + Sqlite)
/// - ToolParameterBinder string-array round-trip
/// - IAgent interface is implemented by Agent
/// - AgentReply implicit string conversion
/// - Concurrent SqliteMemoryService.InitializeAsync is idempotent
/// </summary>
public sealed class NewFeatureTests
{
    // -----------------------------------------------------------------------
    // OpenAiEmbeddingProvider
    // -----------------------------------------------------------------------

    [Fact]
    public void OpenAiEmbeddingProvider_custom_dimensions_constructor_sets_dimensions()
    {
        var provider = new OpenAiEmbeddingProvider("key", "my-custom-model", 768);
        Assert.Equal(768, provider.Dimensions);
    }

    [Fact]
    public void OpenAiEmbeddingProvider_unknown_model_throws_InvalidOperationException()
    {
        var provider = new OpenAiEmbeddingProvider("key", "unknown-embedding-model-xyz");
        Assert.Throws<InvalidOperationException>(() => _ = provider.Dimensions);
    }

    [Fact]
    public void OpenAiEmbeddingProvider_known_model_small_returns_1536()
    {
        var provider = new OpenAiEmbeddingProvider("key", "text-embedding-3-small");
        Assert.Equal(1536, provider.Dimensions);
    }

    [Fact]
    public void OpenAiEmbeddingProvider_known_model_large_returns_3072()
    {
        var provider = new OpenAiEmbeddingProvider("key", "text-embedding-3-large");
        Assert.Equal(3072, provider.Dimensions);
    }

    [Fact]
    public void OpenAiEmbeddingProvider_custom_constructor_validates_empty_apiKey()
    {
        Assert.Throws<ArgumentException>(() =>
            new OpenAiEmbeddingProvider("", "model", 512));
    }

    [Fact]
    public void OpenAiEmbeddingProvider_custom_constructor_validates_negative_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OpenAiEmbeddingProvider("key", "model", -1));
    }

    [Fact]
    public void OpenAiEmbeddingProvider_custom_constructor_validates_zero_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OpenAiEmbeddingProvider("key", "model", 0));
    }

    // -----------------------------------------------------------------------
    // AgentBuilder.WithOpenAiEmbeddings
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AgentBuilder_WithOpenAiEmbeddings_registers_embedding_provider()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new EchoAgentModel()))
            .WithOpenAiEmbeddings("test-key")
            .Build();

        // Agent builds and initializes without error
        await agent.InitializeAsync();
        Assert.NotNull(agent);
    }

    [Fact]
    public async Task AgentBuilder_WithOpenAiEmbeddings_custom_model()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new EchoAgentModel()))
            .WithOpenAiEmbeddings("test-key", "text-embedding-ada-002")
            .Build();

        await agent.InitializeAsync();
        Assert.NotNull(agent);
    }

    // -----------------------------------------------------------------------
    // InMemoryMemoryService — DeleteMessageAsync / ClearAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InMemoryMemoryService_DeleteMessageAsync_removes_message_and_embedding()
    {
        var svc = new InMemoryMemoryService();
        await svc.InitializeAsync();

        await svc.StoreMessageAsync("a", "apple");
        await svc.StoreMessageAsync("b", "banana");
        await svc.StoreEmbeddingAsync("a", [1f, 0f]);
        await svc.StoreEmbeddingAsync("b", [0f, 1f]);

        await svc.DeleteMessageAsync("a");

        var results = await svc.RetrieveRelevantAsync("", topK: 10);
        Assert.Single(results);
        Assert.Equal("banana", results[0]);

        // Embedding for "a" should no longer participate in similarity search
        var similar = await svc.RetrieveSimilarAsync([1f, 0f], topK: 10);
        // Only "banana" (id "b") remains; it has near-zero cosine similarity to [1,0] but is still returned
        Assert.DoesNotContain(similar, r => r.Content == "apple");
    }

    [Fact]
    public async Task InMemoryMemoryService_ClearAsync_removes_all_messages_and_embeddings()
    {
        var svc = new InMemoryMemoryService();
        await svc.InitializeAsync();

        await svc.StoreMessageAsync("1", "first");
        await svc.StoreMessageAsync("2", "second");
        await svc.StoreEmbeddingAsync("1", [1f, 0f]);

        await svc.ClearAsync();

        var results = await svc.RetrieveRelevantAsync("", topK: 10);
        Assert.Empty(results);

        var similar = await svc.RetrieveSimilarAsync([1f, 0f], topK: 10);
        Assert.Empty(similar);
    }

    [Fact]
    public async Task InMemoryMemoryService_DeleteMessageAsync_nonexistent_id_is_noop()
    {
        var svc = new InMemoryMemoryService();
        await svc.InitializeAsync();

        await svc.StoreMessageAsync("a", "apple");

        // Deleting a non-existent ID should not throw
        await svc.DeleteMessageAsync("does-not-exist");

        var results = await svc.RetrieveRelevantAsync("", topK: 10);
        Assert.Single(results);
    }

    // -----------------------------------------------------------------------
    // SqliteMemoryService — DeleteMessageAsync / ClearAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SqliteMemoryService_DeleteMessageAsync_removes_message()
    {
        var path = Path.GetTempFileName();
        try
        {
            var svc = new SqliteMemoryService(path);
            await svc.InitializeAsync();

            await svc.StoreMessageAsync("a", "apple");
            await svc.StoreMessageAsync("b", "banana");

            await svc.DeleteMessageAsync("a");

            var results = await svc.RetrieveRelevantAsync("", topK: 10);
            Assert.Single(results);
            Assert.Equal("banana", results[0]);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task SqliteMemoryService_ClearAsync_removes_all_messages()
    {
        var path = Path.GetTempFileName();
        try
        {
            var svc = new SqliteMemoryService(path);
            await svc.InitializeAsync();

            await svc.StoreMessageAsync("1", "first");
            await svc.StoreMessageAsync("2", "second");

            await svc.ClearAsync();

            var results = await svc.RetrieveRelevantAsync("", topK: 10);
            Assert.Empty(results);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task SqliteMemoryService_concurrent_InitializeAsync_is_idempotent()
    {
        var path = Path.GetTempFileName();
        try
        {
            var svc = new SqliteMemoryService(path);

            // Fire many concurrent InitializeAsync calls — only one should win the lock
            var tasks = Enumerable.Range(0, 20)
                .Select(_ => svc.InitializeAsync())
                .ToList();

            await Task.WhenAll(tasks);

            // Service should still function correctly
            await svc.StoreMessageAsync("1", "hello");
            var results = await svc.RetrieveRelevantAsync("hello", topK: 5);
            Assert.Single(results);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    // -----------------------------------------------------------------------
    // ToolParameterBinder — string array
    // -----------------------------------------------------------------------

    [Fact]
    public void ToolParameterBinder_binds_string_array_correctly()
    {
        var tool = new StringArrayTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool.GetType());
        var arguments = """{"Tags": ["alpha", "beta", "gamma"]}""";

        ToolParameterBinder.BindParameters(tool, arguments, metadata);

        Assert.Equal(3, tool.Tags.Length);
        Assert.Equal("alpha", tool.Tags[0]);
        Assert.Equal("beta", tool.Tags[1]);
        Assert.Equal("gamma", tool.Tags[2]);
    }

    [Fact]
    public void ToolParameterBinder_binds_string_list_correctly()
    {
        var tool = new StringListTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool.GetType());
        var arguments = """{"Items": ["one", "two"]}""";

        ToolParameterBinder.BindParameters(tool, arguments, metadata);

        Assert.Equal(2, tool.Items.Count);
        Assert.Equal("one", tool.Items[0]);
        Assert.Equal("two", tool.Items[1]);
    }

    [Fact]
    public void ToolParameterBinder_string_array_values_have_no_json_quotes()
    {
        // Regression: fix #2 — GetRawText() was used for string items, wrapping
        // values in extra JSON quotes like "\"alpha\"" instead of "alpha".
        var tool = new StringArrayTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool.GetType());
        var arguments = """{"Tags": ["hello world"]}""";

        ToolParameterBinder.BindParameters(tool, arguments, metadata);

        Assert.Equal("hello world", tool.Tags[0]);
        Assert.DoesNotContain("\"", tool.Tags[0]);
    }

    // -----------------------------------------------------------------------
    // IAgent interface
    // -----------------------------------------------------------------------

    [Fact]
    public void Agent_implements_IAgent_interface()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new EchoAgentModel()))
            .Build();

        Assert.IsAssignableFrom<IAgent>(agent);
    }

    // -----------------------------------------------------------------------
    // AgentReply implicit string conversion
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AgentReply_implicitly_converts_to_string()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new EchoAgentModel()))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("ping");

        // Implicit conversion to string should work
        string text = reply;
        Assert.Equal("echo: ping", text);
    }

    [Fact]
    public async Task AgentReply_ToString_returns_content()
    {
        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new EchoAgentModel()))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("ping");

        Assert.Equal("echo: ping", reply.ToString());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private sealed class EchoAgentModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? "";
            return Task.FromResult(new AgentResponse($"echo: {last}"));
        }
    }

    private sealed class StringArrayTool : ITool
    {
        public string Name => "string-array";
        public string Description => "Tool with string array parameter";

        [ToolParameter(Description = "List of tags", Required = true)]
        public string[] Tags { get; set; } = [];

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Join(",", Tags));
    }

    private sealed class StringListTool : ITool
    {
        public string Name => "string-list";
        public string Description => "Tool with string list parameter";

        [ToolParameter(Description = "List of items", Required = true)]
        public List<string> Items { get; set; } = [];

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Join(",", Items));
    }
}
