using Agentic.Core;
using Agentic.Middleware;

namespace CachingMiddlewareSample;

/// <summary>
/// CachingMiddleware demonstrates how to cache agent responses for improved performance.
/// This reduces LLM API calls and improves response times.
/// </summary>
sealed class CachingMiddleware : IAssistantMiddleware
{
    private readonly Dictionary<string, CacheEntry> _cache = [];
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GenerateCacheKey(context);

        // Check cache
        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            if (DateTime.UtcNow - entry.CreatedAt < _cacheExpiration)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"💾 Cache Hit! (age: {(DateTime.UtcNow - entry.CreatedAt).TotalSeconds:F1}s)");
                Console.ResetColor();
                return entry.Response;
            }
            else
            {
                _cache.Remove(cacheKey);
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("🔄 Cache Miss - Calling agent");
        Console.ResetColor();

        // Not in cache, call the agent
        var response = await next(context, cancellationToken);

        // Store in cache
        _cache[cacheKey] = new CacheEntry
        {
            Response = response,
            CreatedAt = DateTime.UtcNow
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Cached response ({_cache.Count} items in cache)");
        Console.ResetColor();

        return response;
    }

    private static string GenerateCacheKey(AgentContext context)
    {
        // Simple cache key based on user input
        // In a real app, you might also include:
        // - User ID
        // - Model configuration
        // - Temperature settings
        return context.Input;
    }

    private sealed class CacheEntry
    {
        public required AgentResponse Response { get; set; }
        public required DateTime CreatedAt { get; set; }
    }
}
