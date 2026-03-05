using System.Collections.Concurrent;
using Agentic.Abstractions;

namespace Agentic.Stores;

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, float[]> _vectors = new();
    private readonly int _dimensions;
    private bool _initialized;

    public int Dimensions => _dimensions;

    public InMemoryVectorStore(int dimensions = 1536)
    {
        _dimensions = dimensions;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = true;
        return Task.CompletedTask;
    }

    public Task UpsertAsync(string id, ReadOnlyMemory<float> vector, CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector dimension must be {_dimensions}, got {vector.Length}.");

        _vectors[id] = vector.ToArray();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Id, ReadOnlyMemory<float> Vector, float Score)>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        if (queryVector.Length != _dimensions)
            throw new ArgumentException($"Query vector dimension must be {_dimensions}, got {queryVector.Length}.");

        var results = _vectors
            .Select(kvp => (kvp.Key, new ReadOnlyMemory<float>(kvp.Value), Score: CosineSimilarity(queryVector.Span, kvp.Value)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList<(string Id, ReadOnlyMemory<float> Vector, float Score)>();

        return Task.FromResult<IReadOnlyList<(string Id, ReadOnlyMemory<float> Vector, float Score)>>(results);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        _vectors.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        _vectors.Clear();
        return Task.CompletedTask;
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
}
