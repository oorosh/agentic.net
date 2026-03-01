using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Stores;
using Agentic.Tests.Fakes;
using Xunit;

namespace Agentic.Tests;

/// <summary>
/// Tests for embedding provider integration and vector store functionality.
/// </summary>
public sealed class EmbeddingProviderTests
{
    [Fact]
    public void OpenAiEmbeddingProvider_has_correct_dimensions()
    {
        var provider = new Agentic.Providers.OpenAi.OpenAiEmbeddingProvider("test-key");
        
        Assert.Equal(1536, provider.Dimensions);
    }

    [Fact]
    public async Task Agent_with_embedding_provider_initializes()
    {
        var embeddingProvider = new Agentic.Providers.OpenAi.OpenAiEmbeddingProvider("test-key");
        var vectorStore = new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions);

        var agent = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new EchoModel()))
            .WithMemory(new InMemoryMemoryService())
            .WithEmbeddingProvider(embeddingProvider)
            .WithVectorStore(vectorStore)
            .Build();

        await agent.InitializeAsync();
        
        Assert.NotNull(agent);
    }

    [Fact]
    public async Task InMemoryVectorStore_cosine_similarity_calculation()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        // Perfect match
        await store.UpsertAsync("exact", [1f, 0f, 0f]);
        
        // 45 degrees apart (higher similarity than perpendicular)
        await store.UpsertAsync("partial", [0.707f, 0.707f, 0f]);
        
        // 90 degrees apart (perpendicular, zero similarity)
        await store.UpsertAsync("perpendicular", [0f, 1f, 0f]);

        var results = await store.SearchAsync([1f, 0f, 0f], topK: 3);

        Assert.Equal(3, results.Count);
        Assert.Equal("exact", results[0].Id);
        Assert.Equal(1f, results[0].Score, precision: 2);
        
        // Results should be sorted by similarity: exact (1.0) > partial (0.707) > perpendicular (0)
        Assert.Equal("partial", results[1].Id);
        Assert.True(results[1].Score > 0, "Partial similarity should be positive");
        
        Assert.Equal("perpendicular", results[2].Id);
        Assert.Equal(0f, results[2].Score, precision: 1);
        
        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    [Fact]
    public async Task InMemoryVectorStore_handles_zero_vector()
    {
        var store = new InMemoryVectorStore(dimensions: 2);
        await store.InitializeAsync();

        // This might be an edge case depending on implementation
        await store.UpsertAsync("normal", [1f, 0f]);
        
        var results = await store.SearchAsync([0f, 0f], topK: 5);
        
        // Should return results (behavior depends on implementation)
        Assert.NotNull(results);
    }

    [Fact]
    public async Task VectorStore_upsert_updates_existing()
    {
        var store = new InMemoryVectorStore(dimensions: 2);
        await store.InitializeAsync();

        await store.UpsertAsync("id1", [1f, 0f]);
        var results1 = await store.SearchAsync([1f, 0f], topK: 5);
        Assert.Single(results1);

        // Upsert same ID with different vector
        await store.UpsertAsync("id1", [0f, 1f]);
        var results2 = await store.SearchAsync([0f, 1f], topK: 5);
        
        Assert.Single(results2);
        Assert.Equal("id1", results2[0].Id);
    }

    [Fact]
    public async Task VectorStore_delete_removes_specific_vector()
    {
        var store = new InMemoryVectorStore(dimensions: 2);
        await store.InitializeAsync();

        await store.UpsertAsync("vec1", [1f, 0f]);
        await store.UpsertAsync("vec2", [0f, 1f]);

        await store.DeleteAsync("vec1");

        var results = await store.SearchAsync([1f, 0f], topK: 5);
        
        Assert.Single(results);
        Assert.Equal("vec2", results[0].Id);
    }

    [Fact]
    public async Task VectorStore_empty_search_returns_empty()
    {
        var store = new InMemoryVectorStore(dimensions: 2);
        await store.InitializeAsync();

        var results = await store.SearchAsync([1f, 0f], topK: 5);
        
        Assert.Empty(results);
    }

    [Fact]
    public async Task VectorStore_dimension_validation()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        // Correct dimensions - should succeed
        await store.UpsertAsync("correct", [1f, 0f, 0f]);

        // Wrong dimensions - should fail
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync("wrong", [1f, 0f]));
    }

    [Fact]
    public async Task InMemoryVectorStore_search_query_dimension_validation()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        await store.UpsertAsync("vec", [1f, 0f, 0f]);

        // Wrong query dimensions - should fail
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SearchAsync([1f, 0f], topK: 5));
    }

    [Fact]
    public async Task InMemoryVectorStore_large_topk()
    {
        var store = new InMemoryVectorStore(dimensions: 2);
        await store.InitializeAsync();

        for (int i = 0; i < 5; i++)
        {
            await store.UpsertAsync($"vec{i}", [1f, 0f]);
        }

        var results = await store.SearchAsync([1f, 0f], topK: 100);
        
        // Should only return available results
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task InMemoryVectorStore_topk_zero()
    {
        var store = new InMemoryVectorStore(dimensions: 2);
        await store.InitializeAsync();

        await store.UpsertAsync("vec", [1f, 0f]);

        var results = await store.SearchAsync([1f, 0f], topK: 0);
        
        Assert.Empty(results);
    }

    [Fact]
    public async Task SqliteMemoryService_with_vector_store_integration()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var vectorStore = new InMemoryVectorStore(dimensions: 2);
            var memory = new SqliteMemoryService(tempFile, vectorStore);
            await memory.InitializeAsync();

            await memory.StoreMessageAsync("1", "hello");
            await memory.StoreEmbeddingAsync("1", [1f, 0f]);

            await memory.StoreMessageAsync("2", "world");
            await memory.StoreEmbeddingAsync("2", [0f, 1f]);

            var results = await memory.RetrieveSimilarAsync([1f, 0f], topK: 2);

            Assert.Equal(2, results.Count);
            Assert.Equal("hello", results[0].Content);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private sealed class EchoModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentResponse("echo"));
        }

        public IAsyncEnumerable<StreamingToken> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            System.Threading.CancellationToken cancellationToken = default) =>
            FakeModelStreamHelper.StreamFromCompleteAsync(this, messages, cancellationToken);
    }
}

