namespace Agentic.Abstractions;

public interface IMemoryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StoreMessageAsync(string id, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default);
}
