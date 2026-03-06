namespace Agentic.Abstractions;

public interface IMemoryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StoreMessageAsync(string id, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default);
    Task StoreEmbeddingAsync(string id, ReadOnlyMemory<float> embedding, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Content, float Score)>> RetrieveSimilarAsync(ReadOnlyMemory<float> queryEmbedding, int topK = 5, CancellationToken cancellationToken = default);
    Task DeleteMessageAsync(string id, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
