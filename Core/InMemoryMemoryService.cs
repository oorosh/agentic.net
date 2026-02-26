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
        if (!_initialized)
        {
            throw new InvalidOperationException("Memory service is not initialized.");
        }

        _store.Add((id, content));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Memory service is not initialized.");
        }

        // If query is empty, return all messages in reverse order (most recent first)
        if (string.IsNullOrWhiteSpace(query))
        {
            var allMessages = _store
                .AsEnumerable()
                .Reverse()
                .Take(topK)
                .Select(x => x.Content)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(allMessages);
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matches = _store
            .AsEnumerable()
            .Reverse()
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
        if (!_initialized)
        {
            throw new InvalidOperationException("Memory service is not initialized.");
        }

        _embeddings[id] = embedding;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Content, float Score)>> RetrieveSimilarAsync(float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Memory service is not initialized.");
        }

        // Check if query is a zero vector
        bool isZeroVector = queryEmbedding.All(x => x == 0f);

        var similarities = _store
            .Where(item => _embeddings.ContainsKey(item.Id))
            .Select(item => (
                item.Content,
                Score: CosineSimilarity(queryEmbedding, _embeddings[item.Id])
            ))
            .Where(x => isZeroVector || x.Score >= 0.5f)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<(string Content, float Score)>>(similarities);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Embedding dimensions must match.");
        }

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        // Handle zero vectors
        if (normA == 0 || normB == 0)
        {
            return 0f;
        }

        float similarity = dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
        
        // Handle NaN (shouldn't happen with above check, but just in case)
        if (float.IsNaN(similarity))
        {
            return 0f;
        }

        return similarity;
    }
}
