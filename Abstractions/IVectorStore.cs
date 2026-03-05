namespace Agentic.Abstractions;

public interface IVectorStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(string id, ReadOnlyMemory<float> vector, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string Id, ReadOnlyMemory<float> Vector, float Score)>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK = 5,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task DeleteAllAsync(CancellationToken cancellationToken = default);

    int Dimensions { get; }
}
