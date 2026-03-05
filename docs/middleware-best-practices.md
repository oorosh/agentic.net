# Middleware Best Practices

This guide provides practical best practices for building, testing, and deploying middleware in Agentic.NET.

## Table of Contents

1. [Design Best Practices](#design-best-practices)
2. [Implementation Best Practices](#implementation-best-practices)
3. [Performance Best Practices](#performance-best-practices)
4. [Testing Best Practices](#testing-best-practices)
5. [Monitoring and Debugging](#monitoring-and-debugging)
6. [Common Pitfalls](#common-pitfalls)

---

## Design Best Practices

### 1. Single Responsibility Principle

Each middleware should have ONE reason to change.

```csharp
// ❌ BAD: Too many responsibilities
sealed class SuperMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Handles auth, logging, caching, rate limiting, etc.
        // 200+ lines of code
        // Hard to test, modify, reuse
    }
}

// ✅ GOOD: Single responsibility
sealed class AuthenticationMiddleware : IAssistantMiddleware
{
    // Only validates credentials - can be understood in 30 seconds
}

sealed class RateLimitingMiddleware : IAssistantMiddleware
{
    // Only enforces rate limits - orthogonal to authentication
}
```

### 2. Design for Composability

Middleware should work well with other middleware, not assume it's alone.

```csharp
// ❌ BAD: Assumes exclusive behavior
sealed class CacheMiddleware : IAssistantMiddleware
{
    private static Dictionary<string, Response> _cache;  // Shared state!
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Cached responses are returned for ANY user
        // But next middleware might be authentication!
    }
}

// ✅ GOOD: Respects other middleware
sealed class CacheMiddleware : IAssistantMiddleware
{
    private readonly Dictionary<string, Response> _cache;
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Authentication runs first, so we know user is verified
        // Rate limiting runs first, so we know they have quota
        // Memory runs after cache miss, so response includes context
    }
}
```

### 3. Clear Interfaces and Dependencies

Make middleware's requirements explicit.

```csharp
// ❌ BAD: Hidden dependencies
sealed class MyMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Assumes some context property exists
        var user = (User)context.Items["user"];  // Throws if missing
        
        // Depends on environment variable
        var apiKey = Environment.GetEnvironmentVariable("SECRET_API_KEY");
    }
}

// ✅ GOOD: Dependencies explicit
sealed class MyMiddleware : IAssistantMiddleware
{
    private readonly IUserService _userService;
    private readonly ISecretManager _secretManager;
    
    public MyMiddleware(IUserService userService, ISecretManager secretManager)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _secretManager = secretManager ?? throw new ArgumentNullException(nameof(secretManager));
    }
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Dependencies are clear and safe
    }
}
```

### 4. Provide Configuration Options

Don't hardcode values - allow customization.

```csharp
// ❌ BAD: Hardcoded values
sealed class RateLimitingMiddleware : IAssistantMiddleware
{
    private const int MaxRequestsPerMinute = 10;      // What if you need 100?
    private const int RetryCount = 3;                 // What if you need 1?
    private const int TimeoutSeconds = 30;            // What if you need 60?
}

// ✅ GOOD: Configurable
sealed class RateLimitingMiddleware : IAssistantMiddleware
{
    private readonly int _maxRequestsPerMinute;
    private readonly int _retryCount;
    private readonly TimeSpan _timeout;
    
    public RateLimitingMiddleware(
        int maxRequestsPerMinute = 10,
        int retryCount = 3,
        TimeSpan? timeout = null)
    {
        _maxRequestsPerMinute = maxRequestsPerMinute;
        _retryCount = retryCount;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }
}
```

### 5. Document Middleware Order Dependencies

Be explicit about required middleware ordering.

```csharp
/// <summary>
/// Caches agent responses for improved performance.
/// 
/// Requirements:
/// - Should be placed AFTER authentication middleware (to avoid caching unauthenticated responses)
/// - Should be placed BEFORE expensive middleware like error handling
/// 
/// Order Example:
/// 1. AuthenticationMiddleware
/// 2. RateLimitingMiddleware
/// 3. CachingMiddleware ← This middleware
/// 4. ErrorHandlingMiddleware
/// </summary>
sealed class CachingMiddleware : IAssistantMiddleware
{
    // ...
}
```

---

## Implementation Best Practices

### 1. Always Handle Cancellation

Respect the CancellationToken to allow graceful shutdown.

```csharp
// ❌ BAD: Ignores cancellation
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    // Missing cancellationToken parameter here!
    var response = await next(context);
    return response;
}

// ✅ GOOD: Respects cancellation
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    // Passes token to allow cancellation
    var response = await next(context, cancellationToken);
    
    // Respects cancellation in custom logic
    while (retries < maxRetries)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // ...
    }
    
    return response;
}
```

### 2. Fail Safely with Clear Error Messages

Users should understand why a request was rejected.

```csharp
// ❌ BAD: Cryptic error
if (!IsValid(context.Input))
    return new AgentResponse("Invalid input");  // What's invalid?

// ✅ GOOD: Clear error message
if (!IsValid(context.Input))
    return new AgentResponse(
        "Invalid input: Message contains prohibited keywords. " +
        "Prohibited words: bad, evil, dangerous. " +
        "Please rephrase your request.");
```

### 3. Use Defensive Programming

Validate assumptions and provide helpful errors.

```csharp
// ❌ BAD: Assumes everything exists
public async Task<AgentResponse> InvokeAsync(...)
{
    var user = (User)context.Items["user"];      // Could be null!
    var response = await next(context);           // Could throw!
    return new AgentResponse(response.Content);   // What if null?
}

// ✅ GOOD: Validates assumptions
public async Task<AgentResponse> InvokeAsync(...)
{
    if (!context.Items.TryGetValue("user", out var userObj) || userObj is not User user)
    {
        return new AgentResponse("User context missing. This middleware requires authentication middleware to run first.");
    }
    
    if (next == null)
    {
        throw new ArgumentNullException(nameof(next));
    }
    
    try
    {
        var response = await next(context, cancellationToken);
        
        if (response == null)
        {
            return new AgentResponse("Unexpected: LLM returned null response");
        }
        
        return response;
    }
    catch (OperationCanceledException)
    {
        throw;  // Don't swallow cancellation
    }
    catch (Exception ex)
    {
        return new AgentResponse($"Error: {ex.Message}");
    }
}
```

### 4. Make Middleware Sealed

Prevent accidental inheritance bugs.

```csharp
// ❌ BAD: Can be inherited (could be modified unsafely)
class LoggingMiddleware : IAssistantMiddleware
{
    // ...
}

// ✅ GOOD: Sealed, can't be inherited
sealed class LoggingMiddleware : IAssistantMiddleware
{
    // ...
}
```

### 5. Use Readonly Fields

Prevent accidental state mutations.

```csharp
// ❌ BAD: Mutable state
sealed class MyMiddleware : IAssistantMiddleware
{
    private Dictionary<string, int> _counts;  // Can be reassigned
    
    public MyMiddleware()
    {
        _counts = new();
    }
}

// ✅ GOOD: Readonly reference
sealed class MyMiddleware : IAssistantMiddleware
{
    private readonly Dictionary<string, int> _counts;
    
    public MyMiddleware()
    {
        _counts = new();
    }
    
    // _counts reference can't be reassigned (but contents can be modified safely)
}
```

---

## Performance Best Practices

### 1. Minimize Allocations

Every allocation has a cost.

```csharp
// ❌ BAD: Allocates on every request
public async Task<AgentResponse> InvokeAsync(...)
{
    var list = new List<string>();        // Allocation
    var dict = new Dictionary<string, int>(); // Allocation
    var sb = new StringBuilder();           // Allocation
    
    // ... lots of processing ...
    
    return new AgentResponse(sb.ToString()); // String allocation
}

// ✅ GOOD: Minimizes allocations
sealed class EfficientMiddleware : IAssistantMiddleware
{
    private readonly StringBuilder _sb = new();  // Reuse
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        _sb.Clear();
        
        // Use reused StringBuilder
        _sb.Append("result");
        
        return new AgentResponse(_sb.ToString());
    }
}
```

### 2. Early Exit for Short-Circuits

Return early if rejecting the request.

```csharp
// ❌ BAD: Does work even if will reject
public async Task<AgentResponse> InvokeAsync(...)
{
    LogDetails(context);              // Unnecessary logging
    CheckPermissions(context);        // Unnecessary permission check
    FetchUserProfile(context);        // Unnecessary fetch
    
    // Then checks rate limit
    if (!CheckRateLimit(context))
        return new AgentResponse("Rate limit exceeded");
    
    return await next(context);
}

// ✅ GOOD: Exits early
public async Task<AgentResponse> InvokeAsync(...)
{
    // Check rate limit first (cheapest operation)
    if (!CheckRateLimit(context))
        return new AgentResponse("Rate limit exceeded");
    
    // Only then do expensive operations
    LogDetails(context);
    CheckPermissions(context);
    
    return await next(context);
}
```

### 3. Use Async Appropriately

Don't block threads.

```csharp
// ❌ BAD: Blocks thread
public async Task<AgentResponse> InvokeAsync(...)
{
    Thread.Sleep(1000);  // Blocks entire thread pool thread!
    
    return await next(context);
}

// ✅ GOOD: Async delay
public async Task<AgentResponse> InvokeAsync(...)
{
    await Task.Delay(1000);  // Releases thread back to pool
    
    return await next(context);
}
```

### 4. Cache Expensive Operations

Compute once, reuse many times.

```csharp
// ❌ BAD: Recalcutes every time
sealed class MyMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        var hasPermission = ExpensivePermissionCheck(context);  // Every request!
        
        if (!hasPermission)
            return new AgentResponse("Unauthorized");
        
        return await next(context);
    }
}

// ✅ GOOD: Cache computation result
sealed class MyMiddleware : IAssistantMiddleware
{
    private readonly MemoryCache _permissionCache = new();
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        var userId = ExtractUserId(context);
        
        if (!_permissionCache.TryGetValue(userId, out var hasPermission))
        {
            hasPermission = await ExpensivePermissionCheckAsync(context);
            _permissionCache.Set(userId, hasPermission, TimeSpan.FromMinutes(5));
        }
        
        if (!hasPermission)
            return new AgentResponse("Unauthorized");
        
        return await next(context);
    }
}
```

---

## Testing Best Practices

### 1. Test Middleware Isolation

Test each middleware independently.

```csharp
[Fact]
public async Task RateLimitingMiddleware_AllowsRequestsWithinLimit()
{
    var middleware = new RateLimitingMiddleware(maxRequests: 2);
    var called = 0;
    
    AgentHandler mockNext = async (ctx, ct) =>
    {
        called++;
        return new AgentResponse("ok");
    };
    
    var context = new AgentContext("test", []);
    
    // First two requests should succeed
    await middleware.InvokeAsync(context, mockNext);
    await middleware.InvokeAsync(context, mockNext);
    
    Assert.Equal(2, called);
    
    // Third should fail
    var response = await middleware.InvokeAsync(context, mockNext);
    Assert.Equal(2, called);  // mockNext not called
    Assert.Contains("rate limit", response.Content, StringComparison.OrdinalIgnoreCase);
}
```

### 2. Test Exception Handling

Verify middleware handles errors gracefully.

```csharp
[Fact]
public async Task ErrorHandlingMiddleware_RetriesTorTransientError()
{
    var middleware = new ErrorHandlingMiddleware(maxRetries: 3);
    var attempts = 0;
    
    AgentHandler failingNext = async (ctx, ct) =>
    {
        attempts++;
        if (attempts < 3)
            throw new HttpRequestException("Connection timeout");
        return new AgentResponse("ok");
    };
    
    var context = new AgentContext("test", []);
    var response = await middleware.InvokeAsync(context, failingNext);
    
    Assert.Equal(3, attempts);  // Retried twice, succeeded on 3rd
    Assert.Equal("ok", response.Content);
}
```

### 3. Test Composition

Verify middleware work together correctly.

```csharp
[Fact]
public async Task MiddlewareComposition_ExecutesInOrder()
{
    var log = new List<string>();
    
    var agent = new AgentBuilder()
        .WithChatClient(new DemoChatClient())
        .UseMiddleware(new TestMiddleware("A", log))
        .UseMiddleware(new TestMiddleware("B", log))
        .UseMiddleware(new TestMiddleware("C", log))
        .Build();
    
    await agent.ChatAsync("test");
    
    // Should execute: A-before, B-before, C-before, LLM, C-after, B-after, A-after
    Assert.Equal(new[] { "A-before", "B-before", "C-before", "C-after", "B-after", "A-after" }, log);
}

sealed class TestMiddleware(string name, List<string> log) : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        log.Add($"{name}-before");
        var response = await next(context, cancellationToken);
        log.Add($"{name}-after");
        return response;
    }
}
```

### 4. Use Fixtures for Complex Setups

Share common test setup.

```csharp
public class AuthenticationMiddlewareTests : IAsyncLifetime
{
    private IUserService _userService;
    private AuthenticationMiddleware _middleware;
    
    public async Task InitializeAsync()
    {
        _userService = new MockUserService();
        _middleware = new AuthenticationMiddleware(_userService);
        
        // Other setup
        await _userService.AddUserAsync(new User { Id = "user1", ApiKey = "key1" });
    }
    
    public Task DisposeAsync() => Task.CompletedTask;
    
    [Fact]
    public async Task ValidateCredentials_WithValidKey()
    {
        // Test uses initialized userService and middleware
    }
}
```

---

## Monitoring and Debugging

### 1. Add Structured Logging

Use proper logging, not Console.WriteLine.

```csharp
// ❌ BAD: Console output
Console.WriteLine($"Request: {context.Input}");  // Lost in production

// ✅ GOOD: Structured logging
sealed class LoggingMiddleware : IAssistantMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;
    
    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        using (_logger.BeginScope(new { CorrelationId = Guid.NewGuid() }))
        {
            _logger.LogInformation("Processing request {Input}", context.Input);
            
            var response = await next(context, cancellationToken);
            
            _logger.LogInformation("Response: {Content}", response.Content);
            
            return response;
        }
    }
}
```

### 2. Include Timing Information

Track performance.

```csharp
sealed class PerformanceMiddleware : IAssistantMiddleware
{
    private readonly ILogger<PerformanceMiddleware> _logger;
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            var response = await next(context, cancellationToken);
            
            sw.Stop();
            _logger.LogInformation(
                "Request completed in {ElapsedMilliseconds}ms",
                sw.ElapsedMilliseconds);
            
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "Request failed after {ElapsedMilliseconds}ms",
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

### 3. Provide Metrics

Expose metrics for monitoring systems.

```csharp
sealed class MetricsMiddleware : IAssistantMiddleware
{
    private readonly Counter<long> _requestCounter;
    private readonly Histogram<double> _durationHistogram;
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        _requestCounter.Add(1);
        
        var sw = Stopwatch.StartNew();
        
        try
        {
            return await next(context, cancellationToken);
        }
        finally
        {
            sw.Stop();
            _durationHistogram.Record(sw.ElapsedMilliseconds);
        }
    }
}
```

---

## Common Pitfalls

### Pitfall 1: Assuming Execution Order

```csharp
// ❌ BAD: Assumes previous middleware ran
sealed class AuthRequiredMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Crashes if authentication middleware didn't run first
        var user = (User)context.Items["user"];
    }
}

// ✅ GOOD: Verify or provide default
sealed class AuthRequiredMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        if (!context.Items.TryGetValue("user", out var userObj))
        {
            return new AgentResponse(
                "This middleware requires authentication middleware to run first");
        }
        
        var user = (User)userObj;
    }
}
```

### Pitfall 2: Modifying Shared State Unsafely

```csharp
// ❌ BAD: Race condition
sealed class CounterMiddleware : IAssistantMiddleware
{
    private int _count;
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        _count++;  // Race condition in concurrent requests!
        
        return await next(context, cancellationToken);
    }
}

// ✅ GOOD: Thread-safe
sealed class CounterMiddleware : IAssistantMiddleware
{
    private readonly Counter<long> _counter = new();
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        _counter.Add(1);  // Atomic operation
        
        return await next(context, cancellationToken);
    }
}
```

### Pitfall 3: Losing Exceptions

```csharp
// ❌ BAD: Swallows all exceptions
public async Task<AgentResponse> InvokeAsync(...)
{
    try
    {
        return await next(context, cancellationToken);
    }
    catch
    {
        return new AgentResponse("Error");  // Lost the actual error!
    }
}

// ✅ GOOD: Log or rethrow important exceptions
public async Task<AgentResponse> InvokeAsync(...)
{
    try
    {
        return await next(context, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        throw;  // Always rethrow cancellation
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Middleware failed");  // Log the error
        return new AgentResponse("Error: " + ex.Message);
    }
}
```

---

## Checklist: Before Shipping Middleware

- [ ] Has single responsibility
- [ ] Thoroughly documented (requirements, order dependencies)
- [ ] Configured via constructor, not hardcoded
- [ ] Has unit tests with good coverage
- [ ] Has integration tests with other middleware
- [ ] Handles edge cases (null, empty, invalid)
- [ ] Handles cancellation gracefully
- [ ] Uses async properly (no blocking)
- [ ] Has structured logging
- [ ] Performance-optimized (no unnecessary allocations)
- [ ] Error messages are user-friendly
- [ ] Thread-safe (if used concurrently)
- [ ] Works independently and composably

---

## Related Documentation

- [Middleware Design Patterns](middleware-design-patterns.md)
- [Middleware Composition](middleware-composition.md)
- [Middleware Execution Order](middleware-execution-order.md)
