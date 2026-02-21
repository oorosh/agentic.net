using Agentic.Abstractions;

namespace Agentic.Core;

public sealed class InMemoryMemoryService : IMemoryService
{
    private readonly List<(string Id, string Content)> _store = [];
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

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matches = _store
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
}
