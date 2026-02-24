# Middleware Design Patterns

This guide explains the common patterns used when building middleware for Agentic.NET, with examples from the included middleware samples.

## Overview

Middleware in Agentic.NET follows a pipeline architecture where each middleware wraps the next handler in a chain. Understanding these patterns helps you build robust, reusable middleware.

## Core Concepts

### Middleware Signature

```csharp
public interface IAssistantMiddleware
{
    Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default);
}

public delegate Task<AgentResponse> AgentHandler(
    AgentContext context,
    CancellationToken cancellationToken);
```

Every middleware must:
1. Accept `AgentContext` (request), `AgentHandler` (next middleware), and `CancellationToken`
2. Either call `next()` to pass through to the next middleware, or handle the request itself
3. Return an `AgentResponse` object

---

## Pattern 1: Short-Circuit (Gating/Filtering)

**Purpose**: Block requests that don't meet certain criteria, preventing them from reaching the LLM.

**Key Behavior**: Return early without calling `next()` when conditions fail.

### Example: RateLimitingMiddleware

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    if (!bucket.TryConsumeToken())
    {
        // Short-circuit: don't call next()
        return new AgentResponse("Rate limit exceeded");
    }

    // Only proceed if check passed
    return await next(context, cancellationToken);
}
```

### Use Cases

- **Rate Limiting**: Prevent too many requests
- **Authentication**: Verify credentials before processing
- **Input Validation**: Block malicious or invalid input
- **Safeguards**: Screen for prohibited content
- **Access Control**: Enforce permissions

### Benefits

- Prevents unnecessary LLM calls (saves money)
- Rejects requests early (faster failure)
- Clear pass/fail decision
- Perfect for policy enforcement

### Examples in Samples

- `SafeguardMiddleware`: PromptGuardMiddleware blocks bad input
- `RateLimitingMiddleware`: Blocks when rate limit exceeded
- `AuthenticationMiddleware`: Blocks without valid API key

---

## Pattern 2: Filter (Observation/Transformation)

**Purpose**: Observe and/or modify request or response without blocking.

**Key Behavior**: Call `next()` but may modify the input or output.

### Example A: LoggingMiddleware (Observe Request & Response)

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    // Observe request
    Console.WriteLine($"Request: {context.Input}");

    // Let it pass through
    var response = await next(context, cancellationToken);

    // Observe response
    Console.WriteLine($"Response: {response.Content}");

    return response;
}
```

### Example B: ResponseGuardMiddleware (Transform Response)

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    // Let request through
    var response = await next(context, cancellationToken);

    // Modify response before returning
    var filtered = FilterContent(response.Content);
    
    return new AgentResponse(filtered, response.ToolCalls);
}
```

### Use Cases

- **Logging/Monitoring**: Track all requests and responses
- **Caching**: Intercept requests and store/retrieve responses
- **Content Filtering**: Modify LLM output before returning
- **Metrics**: Collect timing and performance data
- **Audit Trails**: Record all interactions for compliance

### Benefits

- Non-blocking (request always proceeds)
- Can add cross-cutting concerns
- Observe system behavior
- Transform data transparently

### Examples in Samples

- `LoggingMiddleware`: Logs requests and responses
- `CachingMiddleware`: Intercepts requests, caches responses
- `SafeguardMiddleware`: ResponseGuardMiddleware transforms output
- `ErrorHandlingMiddleware`: Adds retry logic (with modification)

---

## Pattern 3: Decorator (Wrapping with Additional Logic)

**Purpose**: Add additional behavior around the next handler (e.g., retry logic, timeouts).

**Key Behavior**: Call `next()` but wrap it with additional logic before/after.

### Example: ErrorHandlingMiddleware (Retry with Backoff)

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    int attempt = 0;
    while (attempt < _maxRetries)
    {
        try
        {
            return await next(context, cancellationToken);
        }
        catch (HttpRequestException)
        {
            attempt++;
            if (attempt < _maxRetries)
            {
                // Exponential backoff
                var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
                await Task.Delay(delay);
            }
            else
            {
                throw;
            }
        }
    }

    return new AgentResponse("Failed after retries");
}
```

### Use Cases

- **Retry Logic**: Retry failed requests with backoff
- **Timeout Enforcement**: Add cancellation token with timeout
- **Circuit Breaker**: Stop calling if service is down
- **Bulkhead Pattern**: Isolate failures
- **Context Enrichment**: Add metadata before/after

### Benefits

- Transparently adds robustness
- Handles transient failures
- Non-intrusive to normal flow
- Composable with other patterns

### Examples in Samples

- `ErrorHandlingMiddleware`: Retry with exponential backoff
- `TimeoutMiddleware`: Wraps next() with timeout cancellation

---

## Pattern 4: Context Enrichment (Augmentation)

**Purpose**: Add data to the context before passing to next middleware/LLM.

**Key Behavior**: Modify `context.WorkingMessages` before calling `next()`.

### Example: Memory Middleware (Built-in)

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    // Retrieve relevant memories
    var memories = await _memoryService.RetrieveAsync(
        context.Input,
        cancellationToken);

    // Inject into context
    foreach (var memory in memories)
    {
        context.WorkingMessages.Insert(0, 
            new ChatMessage(ChatRole.Assistant, memory.Content));
    }

    // Call with enriched context
    return await next(context, cancellationToken);
}
```

### Use Cases

- **Memory Injection**: Add relevant history
- **Context Addition**: Add system information
- **User Information**: Add user preferences/settings
- **Configuration**: Add runtime configuration
- **State Tracking**: Track conversation state

### Benefits

- Enriches LLM context
- Improves response quality
- Transparent to other middleware
- Powers features like memory

---

## Combining Patterns

You can combine multiple patterns to build powerful middleware stacks:

### Example: Authentication + Logging + Rate Limiting

```
User Request
    ↓
[AuthenticationMiddleware - Short-Circuit]
    ├─ Invalid? → Return error (short-circuit)
    └─ Valid? → Continue
    ↓
[LoggingMiddleware - Filter]
    ├─ Log request
    ├─ Call next
    └─ Log response
    ↓
[RateLimitingMiddleware - Short-Circuit]
    ├─ Limit exceeded? → Return error (short-circuit)
    └─ Within limit? → Continue
    ↓
[CachingMiddleware - Filter]
    ├─ Cache hit? → Return cached response (early return)
    ├─ Cache miss? → Call next → Cache response
    ↓
[MemoryMiddleware - Context Enrichment]
    ├─ Retrieve memories
    ├─ Inject into context
    ├─ Call next
    ↓
[LLM]
    ↓
Response flows back up the stack
```

### Best Practices for Ordering

1. **Logging** → Early in stack (see all traffic)
2. **Authentication** → Near start (fail fast on invalid credentials)
3. **Rate Limiting** → After authentication (limit per user)
4. **Caching** → Before expensive operations (fast path)
5. **Memory/Context** → Before LLM (provide context)
6. **Error Handling** → Can be at start (catch all errors) or around specific layers

---

## Decision Tree: Which Pattern to Use?

```
Does the middleware BLOCK requests?
├─ YES → Use SHORT-CIRCUIT pattern
│   Examples: Rate limiting, authentication, validation
│
└─ NO → Does it modify request/response?
    ├─ YES, modify before LLM → Context enrichment
    │   Examples: Add memory, add user info
    │
    └─ YES, modify after LLM → FILTER pattern
        Examples: Cache, log, modify response
        
    └─ NO, just wrap for robustness? → DECORATOR pattern
        Examples: Retry, timeout, circuit breaker
```

---

## Anti-Patterns to Avoid

### ❌ Ignoring CancellationToken

```csharp
// BAD: Ignores cancellation
return await next(context);

// GOOD: Passes cancellation token
return await next(context, cancellationToken);
```

### ❌ Not Returning AgentResponse

```csharp
// BAD: Returns wrong type
return new ChatMessage(...);

// GOOD: Return AgentResponse
return new AgentResponse(content);
```

### ❌ Calling next() Multiple Times

```csharp
// BAD: This processes the request twice
var r1 = await next(context, cancellationToken);
var r2 = await next(context, cancellationToken);

// GOOD: Call once and reuse
var response = await next(context, cancellationToken);
```

### ❌ Catching and Ignoring All Exceptions

```csharp
// BAD: Hides actual problems
try { return await next(...); }
catch { return new AgentResponse("Error"); }

// GOOD: Handle specific exceptions
try { return await next(...); }
catch (OperationCanceledException) { throw; }
catch (TimeoutException ex) { return handle_timeout(ex); }
```

---

## Performance Considerations

### Short-Circuit Efficiency

Short-circuit middlewares should fail fast:
```csharp
// GOOD: Quick validation
if (cache.TryGetValue(key, out var value))
    return value;  // Fast path

// Call LLM only if necessary
return await next(context, cancellationToken);
```

### Avoid Blocking Operations

```csharp
// BAD: Blocks thread
Thread.Sleep(1000);

// GOOD: Async
await Task.Delay(1000);
```

### Consider Middleware Ordering

```
❌ Slow to fast (wasted work)
[ErrorHandling] → [RateLimit] → [Caching] → [LLM]

✅ Fast to slow (early exit)
[RateLimit] → [Caching] → [ErrorHandling] → [LLM]
```

---

## Testing Middleware

### Example Unit Test

```csharp
[Fact]
public async Task LoggingMiddleware_LogsRequests()
{
    var middleware = new LoggingMiddleware();
    var called = false;
    
    AgentHandler mockNext = async (ctx, ct) =>
    {
        called = true;
        return new AgentResponse("test");
    };

    var context = new AgentContext("hello", []);
    var response = await middleware.InvokeAsync(
        context,
        mockNext,
        CancellationToken.None);

    Assert.True(called);  // Verify next() was called
    Assert.Equal("test", response.Content);
}
```

---

## Related Documentation

- [Middleware Execution Order](middleware-execution-order.md)
- [Middleware Best Practices](middleware-best-practices.md)
- [Middleware Composition](middleware-composition.md)
- [Sample: SafeguardMiddleware](../samples/SafeguardMiddleware/README.md)
- [Sample: LoggingMiddleware](../samples/LoggingMiddleware/README.md)
- [Sample: RateLimitingMiddleware](../samples/RateLimitingMiddleware/README.md)
