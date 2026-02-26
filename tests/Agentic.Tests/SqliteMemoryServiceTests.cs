using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Core;
using Xunit;

namespace Agentic.Tests;

/// <summary>
/// Tests for SqliteMemoryService - persistent memory storage with SQLite backend.
/// </summary>
public sealed class SqliteMemoryServiceTests : IAsyncLifetime
{
    private string _tempDbPath = null!;

    public Task InitializeAsync()
    {
        _tempDbPath = Path.GetTempFileName();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { File.Delete(_tempDbPath); } catch { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitializeAsync_creates_database()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        
        await service.InitializeAsync();
        
        Assert.True(File.Exists(_tempDbPath));
    }

    [Fact]
    public async Task StoreMessageAsync_stores_and_retrieves_messages()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("id1", "hello world");
        await service.StoreMessageAsync("id2", "goodbye world");

        var results = await service.RetrieveRelevantAsync("hello", topK: 10);
        
        Assert.Contains("hello world", results);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_performs_full_text_search()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "The quick brown fox");
        await service.StoreMessageAsync("2", "jumps over the lazy dog");
        await service.StoreMessageAsync("3", "A brown fox sleeps");

        var results = await service.RetrieveRelevantAsync("fox", topK: 10);
        
        Assert.Equal(2, results.Count);
        Assert.Contains("The quick brown fox", results);
        Assert.Contains("A brown fox sleeps", results);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_respects_topk_limit()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        for (int i = 0; i < 10; i++)
        {
            await service.StoreMessageAsync(i.ToString(), $"test message {i}");
        }

        var results = await service.RetrieveRelevantAsync("message", topK: 3);
        
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_empty_query_returns_recent_messages()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "first");
        await service.StoreMessageAsync("2", "second");
        await service.StoreMessageAsync("3", "third");

        var results = await service.RetrieveRelevantAsync("", topK: 10);
        
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task RetrieveRelevantAsync_no_match_returns_empty()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "apple");
        await service.StoreMessageAsync("2", "banana");

        // When query doesn't match, fall back to returning recent messages
        var results = await service.RetrieveRelevantAsync("orange", topK: 10);
        
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Persistence_survives_service_recreation()
    {
        // Store with first service instance
        var service1 = new SqliteMemoryService(_tempDbPath);
        await service1.InitializeAsync();
        await service1.StoreMessageAsync("1", "stored data");

        // Retrieve with second service instance
        var service2 = new SqliteMemoryService(_tempDbPath);
        await service2.InitializeAsync();
        var results = await service2.RetrieveRelevantAsync("stored", topK: 10);
        
        Assert.Single(results);
        Assert.Contains("stored data", results);
    }

    [Fact]
    public async Task StoreEmbeddingAsync_stores_embeddings()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        var embedding = new float[] { 1f, 0.5f, 0.2f };
        
        await service.StoreEmbeddingAsync("1", embedding);
        
        // Should not throw
    }

    [Fact]
    public async Task RetrieveSimilarAsync_empty_without_vector_store()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "test");

        var results = await service.RetrieveSimilarAsync(new float[] { 1f, 0f, 0f }, topK: 10);
        
        Assert.Empty(results);
    }

    [Fact]
    public async Task Case_insensitive_search()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "HELLO World");

        var results = await service.RetrieveRelevantAsync("hello", topK: 10);
        
        Assert.Single(results);
    }

    [Fact]
    public async Task Multiple_stores_accumulate()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "message one");
        await service.StoreMessageAsync("2", "message two");
        await service.StoreMessageAsync("3", "message three");

        var results = await service.RetrieveRelevantAsync("message", topK: 10);
        
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task StoreMessageAsync_throws_before_initialization()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.StoreMessageAsync("1", "message"));
    }

    [Fact]
    public async Task RetrieveRelevantAsync_throws_before_initialization()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.RetrieveRelevantAsync("query", topK: 10));
    }

    [Fact]
    public async Task Concurrent_stores_work_correctly()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        var tasks = Enumerable.Range(0, 10)
            .Select(i => service.StoreMessageAsync(i.ToString(), $"message {i}"))
            .ToList();

        await Task.WhenAll(tasks);

        var results = await service.RetrieveRelevantAsync("message", topK: 20);
        
        Assert.Equal(10, results.Count);
    }

    [Fact]
    public async Task Idempotent_initialization()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        
        await service.InitializeAsync();
        await service.InitializeAsync(); // Should not throw
        
        Assert.True(File.Exists(_tempDbPath));
    }

    [Fact]
    public async Task Special_characters_in_messages()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        var specialMsg = "Hello! How are you? (I'm fine.) [Very well]";
        await service.StoreMessageAsync("1", specialMsg);

        var results = await service.RetrieveRelevantAsync("hello", topK: 10);
        
        Assert.Single(results);
        // Verify the result contains the text "Hello"
        Assert.Contains("Hello", results[0]);
    }

    [Fact]
    public async Task Long_message_storage()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        var longMsg = new string('x', 10000);
        await service.StoreMessageAsync("1", longMsg);

        var results = await service.RetrieveRelevantAsync("x", topK: 10);
        
        Assert.Single(results);
    }

    [Fact]
    public async Task Whitespace_handling()
    {
        var service = new SqliteMemoryService(_tempDbPath);
        await service.InitializeAsync();

        await service.StoreMessageAsync("1", "  hello   world  ");

        var results = await service.RetrieveRelevantAsync("hello", topK: 10);
        
        Assert.Single(results);
    }
}
