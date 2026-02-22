using Agentic.Abstractions;

namespace Agentic.Stores;

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly Dictionary<string, float[]> _vectors = new();
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

    public Task UpsertAsync(string id, float[] vector, CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector dimension must be {_dimensions}, got {vector.Length}.");

        _vectors[id] = vector.ToArray();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Id, float[] Vector, float Score)>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        if (queryVector.Length != _dimensions)
            throw new ArgumentException($"Query vector dimension must be {_dimensions}, got {queryVector.Length}.");

        var results = _vectors
            .Select(kvp => (kvp.Key, kvp.Value, Score: CosineSimilarity(queryVector, kvp.Value)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<(string Id, float[] Vector, float Score)>>(results);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        _vectors.Remove(id);
        return Task.CompletedTask;
    }

    public Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized) throw new InvalidOperationException("Vector store not initialized.");
        _vectors.Clear();
        return Task.CompletedTask;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vector dimensions must match.");

        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
