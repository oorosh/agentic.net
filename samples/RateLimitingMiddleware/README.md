# RateLimitingMiddleware Sample

This sample demonstrates how to implement **RateLimitingMiddleware** that prevents abuse by limiting the number of requests per time period.

## Overview

RateLimitingMiddleware implements the **short-circuit pattern** - it checks the request before allowing it through and may reject requests that exceed the rate limit.

## Key Features

- **Token Bucket Algorithm**: Fair, predictable rate limiting
- **Per-Client Limiting**: Track different limits for different clients
- **Automatic Refill**: Tokens reset after the time window
- **Clear Feedback**: Users know how many requests remain

## How It Works

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    if (!bucket.TryConsumeToken())
    {
        // Rate limit exceeded - short-circuit without calling next()
        throw new InvalidOperationException("Rate limit exceeded");
    }

    return await next(context, cancellationToken);
}
```

## Token Bucket Algorithm

The token bucket is a classic rate limiting algorithm:
- Start with N tokens (capacity)
- Each request consumes 1 token
- Tokens refill at a fixed rate (e.g., per minute)
- When no tokens remain, requests are rejected until refill

Benefits:
- Handles bursts gracefully
- Predictable reset time
- Fair to all clients
- Prevents thundering herd

## Use Cases

- **API Protection**: Prevent abuse and DDoS attacks
- **Cost Control**: Limit LLM API calls and costs
- **Fair Sharing**: Ensure equitable resource distribution
- **Capacity Management**: Maintain predictable performance
- **User Quotas**: Different limits for different tiers

## Running the Sample

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project samples/RateLimitingMiddleware/RateLimitingMiddleware.csproj
```

## Sample Output

```
📊 Rate Limit: 5/5 requests remaining (resets in 60.0s)
Response: ...

📊 Rate Limit: 4/5 requests remaining (resets in 59.8s)
Response: ...

❌ Rate limit exceeded! Try again in 45 seconds.
```

## Middleware Pattern: Short-Circuit

This middleware uses the **short-circuit pattern** because it:
1. Checks if the request should proceed
2. Throws an exception if limit exceeded (no `await next()`)
3. Only calls next() if request passes the check

Perfect for policies/gatekeeping that should block requests.

## Advanced Enhancements

Extend this middleware to:
- Use per-user rate limits from authentication
- Support multiple rate limit windows (per hour, per day)
- Integrate with distributed caching (Redis, Memcached)
- Provide graceful degradation (prioritize important users)
- Track rate limit metrics in monitoring system
- Return HTTP 429 responses in web contexts
- Implement exponential backoff suggestions

## Production Considerations

For multi-server deployments:
- Use **distributed cache** (Redis) instead of in-memory
- Sync rate limit state across servers
- Use centralized authentication for user IDs
- Implement circuit breaker pattern for cache failures

## Related Samples

- **SafeguardMiddleware**: Content filtering with short-circuit
- **LoggingMiddleware**: Request/response logging
- **ErrorHandlingMiddleware**: Error recovery
