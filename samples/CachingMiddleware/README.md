# CachingMiddleware Sample

This sample demonstrates how to implement **response caching** to improve performance and reduce API costs.

## Overview

CachingMiddleware implements the **filter pattern** with caching - it checks cache before calling the agent and stores responses for future reuse.

## Key Features

- **In-Memory Caching**: Fast responses for repeated queries
- **Cache Expiration**: Automatic cleanup of stale entries
- **Cache Hit/Miss Tracking**: Know when cache is being used
- **Simple Cache Key**: Based on user input (can be enhanced)

## How It Works

```csharp
public async Task<AgentResponse> InvokeAsync(...)
{
    var cacheKey = GenerateCacheKey(context);
    
    // Check cache first
    if (_cache.TryGetValue(cacheKey, out var entry))
    {
        if (!IsExpired(entry))
        {
            Console.WriteLine("💾 Cache Hit!");
            return entry.Response;  // No LLM call needed
        }
    }
    
    // Not cached, call agent
    var response = await next(context, cancellationToken);
    
    // Store for future use
    _cache[cacheKey] = response;
    return response;
}
```

## Use Cases

- **Reduce API Costs**: Fewer LLM calls = lower bills
- **Improve Response Time**: Cached responses are instant
- **Handle Repeated Questions**: Users often ask similar things
- **Decrease Latency**: No network round-trip for cached queries
- **Reduce Load**: Fewer requests to LLM API

## Running the Sample

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project samples/CachingMiddleware/CachingMiddleware.csproj
```

## Sample Output

```
📝 Request 1: 'Tell me about cats'
🔄 Cache Miss - Calling agent
✅ Cached response (1 items in cache)
Response: Cats are...

📝 Request 2: Same question (should hit cache)
💾 Cache Hit! (age: 0.5s)
Response: Cats are...

📝 Request 3: Different question
🔄 Cache Miss - Calling agent
✅ Cached response (2 items in cache)
Response: Dogs are...

📝 Request 4: Back to cats (cache hit again)
💾 Cache Hit! (age: 5.2s)
Response: Cats are...
```

## Middleware Pattern: Filter with State

This middleware uses the **filter pattern** with added state management:
1. Checks cache before calling next()
2. Returns cached response if available
3. Calls next() only if not cached
4. Stores response for future use
5. Returns response to caller

Perfect for performance optimization with side effects.

## Advanced Enhancements

Extend this middleware to:
- Use distributed cache (Redis) for multi-server setups
- Add cache warming (pre-populate with common queries)
- Implement partial matching (similar queries → fuzzy cache hit)
- Use semantic similarity (embedding-based cache keys)
- Add cache metrics and monitoring
- Implement cache invalidation triggers
- Support cache versioning (invalidate on model updates)
- Add cache compression for large responses

## Production Considerations

For production deployments:
- **Use distributed cache** (Redis, Memcached) instead of in-memory
- **Add cache layer metrics** (hit rate, size, evictions)
- **Implement TTL strategies** (different times for different queries)
- **Monitor cache performance** (hit rate, latency improvement)
- **Add cache warming** for frequently asked questions
- **Handle stale data** with versioning or invalidation
- **Use cache keys** that include model, version, temperature

## Cache Key Strategy

Current implementation:
```csharp
// Simple: Just the user input
var cacheKey = context.Input;
```

Better implementation:
```csharp
// Include context
var cacheKey = $"{userId}:{modelId}:{context.Input}";
```

Advanced implementation:
```csharp
// Semantic similarity
var embedding = await embeddingProvider.EmbedAsync(context.Input);
var cacheKey = NearestCachedEmbedding(embedding);
```

## Related Samples

- **LoggingMiddleware**: Monitor cache performance
- **RateLimitingMiddleware**: Rate limit + caching
- **ErrorHandlingMiddleware**: Handle cache failures
