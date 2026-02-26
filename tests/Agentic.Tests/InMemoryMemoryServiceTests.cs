using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Core;
using Xunit;
using System.Diagnostics.CodeAnalysis;

namespace Agentic.Tests;

/// <summary>
/// Tests for InMemoryMemoryService - transient in-memory memory storage with full-text and semantic search.
/// </summary>
public sealed class InMemoryMemoryServiceTests
{
    [Fact]
    public async Task InitializeAsync_completes_successfully()
    {
        var service = new InMemoryMemoryService();
        
        await service.InitializeAsync();
        
        // Should not throw
    }

    [Fact]
    public async Task StoreMessageAsync_stores_messages()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "hello world");
        await service.StoreMessageAsync("2", "goodbye");

        var results = await service.RetrieveRelevantAsync("hello", topK: 10);
        Assert.Contains("hello world", results);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_performs_keyword_search()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "The cat sat on the mat");
        await service.StoreMessageAsync("2", "The dog ran in the park");
        await service.StoreMessageAsync("3", "Cats and dogs are pets");

        var results = await service.RetrieveRelevantAsync("cat", topK: 10);
        
        Assert.Contains("The cat sat on the mat", results);
        Assert.Contains("Cats and dogs are pets", results);
        Assert.DoesNotContain("The dog ran in the park", results);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_empty_query_returns_all_messages()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "first");
        await service.StoreMessageAsync("2", "second");
        await service.StoreMessageAsync("3", "third");

        var results = await service.RetrieveRelevantAsync("", topK: 10);
        
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_respects_topk_limit()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        for (int i = 0; i < 10; i++)
        {
            await service.StoreMessageAsync(i.ToString(), $"message {i}");
        }

        var results = await service.RetrieveRelevantAsync("message", topK: 3);
        
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_returns_most_recent_first()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "old message about topic");
        await Task.Delay(10); // Small delay to ensure ordering
        await service.StoreMessageAsync("2", "new message about topic");

        var results = await service.RetrieveRelevantAsync("topic", topK: 10);
        
        Assert.Equal(2, results.Count);
        // Most recent should be first (LIFO)
        Assert.Equal("new message about topic", results[0]);
        Assert.Equal("old message about topic", results[1]);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_no_matches_returns_empty()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "apple");
        await service.StoreMessageAsync("2", "banana");

        var results = await service.RetrieveRelevantAsync("xyz", topK: 10);
        
        Assert.Empty(results);
    }

    [Fact]
    public async Task StoreEmbeddingAsync_stores_embeddings()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        var embedding = new float[] { 1f, 0.5f, 0.2f };
        
        await service.StoreEmbeddingAsync("1", embedding);
        
        // Should not throw
    }

    [Fact]
    public async Task RetrieveSimilarAsync_finds_similar_embeddings()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "programming in C#");
        await service.StoreEmbeddingAsync("1", new float[] { 1f, 0.5f, 0.2f });

        await service.StoreMessageAsync("2", "weather today");
        await service.StoreEmbeddingAsync("2", new float[] { 0f, 0f, 1f });

        // Query similar to embedding 1
        var results = await service.RetrieveSimilarAsync(new float[] { 0.9f, 0.5f, 0.2f }, topK: 2);
        
        Assert.Single(results);
        Assert.Contains("C#", results[0].Content);
    }

    [Fact]
    public async Task RetrieveSimilarAsync_respects_topk()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        for (int i = 0; i < 5; i++)
        {
            await service.StoreMessageAsync(i.ToString(), $"message {i}");
            await service.StoreEmbeddingAsync(i.ToString(), new float[] { (float)i, 0f, 0f });
        }

        var results = await service.RetrieveSimilarAsync(new float[] { 0f, 0f, 0f }, topK: 2);
        
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task RetrieveSimilarAsync_sorted_by_similarity_score()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "message one");
        await service.StoreEmbeddingAsync("1", new float[] { 1f, 0f, 0f });

        await service.StoreMessageAsync("2", "message two");
        await service.StoreEmbeddingAsync("2", new float[] { 0.5f, 0.5f, 0f });

        // Query closer to message 1
        var results = await service.RetrieveSimilarAsync(new float[] { 1f, 0f, 0f }, topK: 2);
        
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score >= results[1].Score, "Results should be sorted by score descending");
    }

    [Fact]
    public async Task RetrieveSimilarAsync_returns_empty_when_no_embeddings()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "message without embedding");

        var results = await service.RetrieveSimilarAsync(new float[] { 1f, 0f, 0f }, topK: 10);
        
        Assert.Empty(results);
    }

    [Fact]
    public async Task Multiple_calls_accumulate_messages()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "first");
        var results1 = await service.RetrieveRelevantAsync("", topK: 10);
        Assert.Single(results1);

        await service.StoreMessageAsync("2", "second");
        var results2 = await service.RetrieveRelevantAsync("", topK: 10);
        Assert.Equal(2, results2.Count);
    }

    [Fact]
    public async Task Case_insensitive_search()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "Hello World");

        var results = await service.RetrieveRelevantAsync("hello", topK: 10);
        
        Assert.Single(results);
        Assert.Contains("Hello World", results);
    }

    [Fact]
    public async Task Partial_word_matching()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "programming");
        await service.StoreMessageAsync("2", "programmer");

        var results = await service.RetrieveRelevantAsync("program", topK: 10);
        
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Special_characters_handled()
    {
        var service = new InMemoryMemoryService();
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "Hello! How are you?");
        await service.StoreMessageAsync("2", "C# is great!");

        var results = await service.RetrieveRelevantAsync("hello", topK: 10);
        
        Assert.Single(results);
        Assert.Contains("Hello", results);
    }
}
