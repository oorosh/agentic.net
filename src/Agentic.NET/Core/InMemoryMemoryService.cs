using Agentic.Abstractions;

namespace Agentic.Core;

public sealed class InMemoryMemoryService : IMemoryService
{
    private readonly Dictionary<string, string> _store = new();
    private readonly Dictionary<string, ReadOnlyMemory<float>> _embeddings = new();
    private bool _initialized;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task StoreMessageAsync(string id, string content, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _store[id] = content;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // If query is empty, return all messages in reverse order (most recent first)
        if (string.IsNullOrWhiteSpace(query))
        {
            var allMessages = _store.Values
                .Reverse()
                .Take(topK)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(allMessages);
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matches = _store.Values
            .Reverse()
            .Select(content => new
            {
                Content = content,
                Score = tokens.Count(token => content.Contains(token, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Content)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(matches);
    }

    public Task StoreEmbeddingAsync(string id, ReadOnlyMemory<float> embedding, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _embeddings[id] = embedding;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Content, float Score)>> RetrieveSimilarAsync(ReadOnlyMemory<float> queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var similarities = _store
            .Where(kvp => _embeddings.ContainsKey(kvp.Key))
            .Select(kvp => (
                Content: kvp.Value,
                Score: CosineSimilarity(queryEmbedding.Span, _embeddings[kvp.Key].Span)
            ))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<(string Content, float Score)>>(similarities);
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        float denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denom == 0f ? 0f : dot / denom;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Memory service not initialized. Call InitializeAsync first.");
        }
    }

    public Task DeleteMessageAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _store.Remove(id);
        _embeddings.Remove(id);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _store.Clear();
        _embeddings.Clear();
        return Task.CompletedTask;
    }
}
