# ErrorHandlingMiddleware Sample

This sample demonstrates how to implement **error handling and recovery middleware** to make agents resilient and user-friendly.

## Overview

This sample shows two complementary middlewares:
- **ErrorHandlingMiddleware**: Retries failed requests with exponential backoff
- **TimeoutMiddleware**: Prevents requests from hanging indefinitely

Together they create a robust error recovery strategy.

## Key Features

- **Automatic Retries**: Retry failed requests intelligently
- **Exponential Backoff**: Increase wait time between retries
- **Request Timeouts**: Prevent indefinite hangs
- **Meaningful Error Messages**: Tell users what went wrong
- **Graceful Degradation**: Fail safely with user-friendly responses

## How It Works

### ErrorHandlingMiddleware

```csharp
try
{
    return await next(context, cancellationToken);
}
catch (HttpRequestException)
{
    // Retry with exponential backoff
    // 100ms, 200ms, 400ms, then fail
    await Task.Delay(backoff);
    return await RetryAsync();
}
```

### TimeoutMiddleware

```csharp
using var timeoutCts = new CancellationTokenSource(timeout);
try
{
    return await next(context, timeoutCts.Token);
}
catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
{
    // Timeout occurred
    return new AgentResponse("Request timed out. Try a simpler question.");
}
```

## Use Cases

- **Network Failures**: Auto-retry transient errors
- **Service Degradation**: Handle slow/overloaded services
- **Hanging Requests**: Prevent indefinite waits
- **Cascading Failures**: Fail fast with timeouts
- **User Experience**: Provide helpful error messages
- **Resilience**: Increase system reliability

## Running the Sample

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project samples/ErrorHandlingMiddleware/ErrorHandlingMiddleware.csproj
```

## Sample Output

### Normal Request
```
Test 1: Normal Request

Response: 2+2 equals 4. This is a simple arithmetic...
```

### Network Error with Retry
```
⚠️  Network error (attempt 1/3). Retrying in 100ms...
⚠️  Network error (attempt 2/3). Retrying in 200ms...
✅ Success on attempt 3

Response: ...
```

### Timeout
```
⏱️  Request timed out after 30.0s
Response: Request timed out after 30.0 seconds. Please try a simpler question.
```

## Middleware Pattern: Decorator with Error Handling

These middlewares use the **decorator pattern** with error handling:
- ErrorHandlingMiddleware wraps the next handler with retry logic
- TimeoutMiddleware adds cancellation token with timeout
- Both pass through on success
- Both handle errors gracefully

Perfect for cross-cutting concerns like resilience.

## Advanced Enhancements

Extend these middlewares to:
- Use circuit breaker pattern (stop retrying after many failures)
- Implement bulkhead pattern (isolate failures)
- Add metrics/monitoring for failures and retries
- Use adaptive timeouts (shorter for cached responses)
- Implement jitter to prevent thundering herd
- Log errors for debugging and alerting
- Provide detailed error codes/messages
- Support different retry strategies per error type

## Production Considerations

For production deployments:
- **Circuit Breaker**: Stop retrying when service is down
- **Adaptive Timeouts**: Adjust based on performance
- **Jitter**: Add randomness to retry delays
- **Dead Letter Queue**: Store permanently failed requests
- **Metrics**: Track retry rates, timeout rates
- **Alerting**: Alert on high error rates
- **Graceful Shutdown**: Allow in-flight requests to complete

## Retry Strategy

### Exponential Backoff
```
Attempt 1: Immediate
Attempt 2: 100ms later
Attempt 3: 200ms later  (100 * 2)
Attempt 4: 400ms later  (100 * 2^2)

Total: Up to ~700ms of retry time
```

### With Jitter (Better)
```
Add random delay to prevent thundering herd
Delay = baseDelay * 2^attempt + random(0, baseDelay)
```

## Error Types Handled

| Error | Handled | Action |
|-------|---------|--------|
| HttpRequestException | ✅ Yes | Retry with backoff |
| OperationCanceledException | ✅ Yes | Return timeout message |
| Other Exceptions | ✅ Yes | Return error message |

## Related Samples

- **LoggingMiddleware**: Log errors for debugging
- **RateLimitingMiddleware**: Rate limit + error handling
- **CachingMiddleware**: Improve success rate with caching
- **AuthenticationMiddleware**: Handle auth failures
