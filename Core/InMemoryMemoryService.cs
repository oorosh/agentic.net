using Agentic.Abstractions;

namespace Agentic.Core;

public sealed class InMemoryMemoryService : IMemoryService
{
    private readonly List<(string Id, string Content)> _store = [];
    private readonly Dictionary<string, float[]> _embeddings = new();
    private bool _initialized;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task StoreMessageAsync(string id, string content, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _store.Add((id, content));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // If query is empty, return all messages in reverse order (most recent first)
        if (string.IsNullOrWhiteSpace(query))
        {
            var allMessages = _store
                .Reverse<(string Id, string Content)>()
                .Take(topK)
                .Select(x => x.Content)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(allMessages);
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matches = _store
            .Reverse<(string Id, string Content)>()
            .Select(item => new
            {
                item.Content,
                Score = tokens.Count(token => item.Content.Contains(token, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Content)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(matches);
    }

    public Task StoreEmbeddingAsync(string id, float[] embedding, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        _embeddings[id] = embedding;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Content, float Score)>> RetrieveSimilarAsync(float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // Check if query is a zero vector
        bool isZeroVector = queryEmbedding.All(x => x == 0f);

        var similarities = _store
            .Where(item => _embeddings.ContainsKey(item.Id))
            .Select(item => (
                item.Content,
                Score: VectorMath.CosineSimilarity(queryEmbedding, _embeddings[item.Id])
            ))
            .Where(x => isZeroVector || x.Score >= 0.5f)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<(string Content, float Score)>>(similarities);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Memory service not initialized. Call InitializeAsync first.");
        }
    }
}
