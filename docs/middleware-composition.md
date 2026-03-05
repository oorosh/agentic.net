# Middleware Composition Guide

This guide explains how to compose and combine multiple middlewares effectively in Agentic.NET.

## What is Middleware Composition?

Middleware composition is the art of combining multiple independent middlewares into a cohesive pipeline that works together to achieve complex goals.

Instead of building one large middleware that does everything, you build small, focused middlewares that each do one thing well, then combine them.

## Core Principle: Single Responsibility

Each middleware should do ONE thing:

```csharp
// ❌ BAD: Does too much
sealed class SuperMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Authentication
        if (!ValidateApiKey(...)) return Error();
        
        // Rate limiting
        if (!CheckRateLimit(...)) return Error();
        
        // Logging
        LogRequest(...);
        
        var response = await next(...);
        
        // Caching
        CacheResponse(...);
        
        // Response filtering
        FilterContent(...);
        
        // Monitoring
        RecordMetrics(...);
        
        LogResponse(...);
        
        return response;
    }
}

// ✅ GOOD: Each middleware has one job
.UseMiddleware(new AuthenticationMiddleware())
.UseMiddleware(new RateLimitingMiddleware())
.UseMiddleware(new LoggingMiddleware())
.UseMiddleware(new CachingMiddleware())
.UseMiddleware(new ResponseFilterMiddleware())
.UseMiddleware(new MonitoringMiddleware())
```

## Building Blocks: Middleware Types

Agentic.NET provides these middleware building blocks:

| Type | Purpose | Pattern | Example |
|------|---------|---------|---------|
| **Gating** | Control access | Short-circuit | Authentication, Rate Limiting |
| **Transformation** | Modify data | Filter | Response filtering, caching |
| **Observation** | Monitor behavior | Filter | Logging, metrics |
| **Enrichment** | Add context | Augmentation | Memory injection, user info |
| **Resilience** | Handle failures | Decorator | Retry, timeout, circuit breaker |

## Real-World Composition Example: Secure Agent API

### Scenario

You're building an API for your agent that needs:
1. Authentication (identify users)
2. Authorization (check permissions)
3. Rate limiting (prevent abuse)
4. Logging (audit trail)
5. Caching (performance)
6. Error handling (resilience)
7. Metrics (monitoring)

### Solution: Middleware Stack

```csharp
var agent = new AgentBuilder()
    .WithChatClient(chatClient)
    
    // Layer 1: Security & Control
    .UseMiddleware(new AuthenticationMiddleware())
    .UseMiddleware(new AuthorizationMiddleware(roles))
    .UseMiddleware(new RateLimitingMiddleware())
    
    // Layer 2: Performance
    .UseMiddleware(new CachingMiddleware())
    
    // Layer 3: Observability
    .UseMiddleware(new LoggingMiddleware())
    .UseMiddleware(new MetricsMiddleware())
    
    // Layer 4: Resilience
    .UseMiddleware(new ErrorHandlingMiddleware())
    .UseMiddleware(new TimeoutMiddleware(TimeSpan.FromSeconds(30)))
    
    // Layer 5: Business Logic (built-in or custom)
    .WithMemory(memoryService)
    .WithTool(tool1)
    
    .Build();
```

### Execution Flow

```
Request
   ↓
[AuthenticationMiddleware] → Verify credentials
   ├─ Invalid? ✋ Return error
   └─ Valid? ↓
   ↓
[AuthorizationMiddleware] → Check permissions
   ├─ Unauthorized? ✋ Return error
   └─ Authorized? ↓
   ↓
[RateLimitingMiddleware] → Check rate limit
   ├─ Exceeded? ✋ Return error  
   └─ Within limit? ↓
   ↓
[CachingMiddleware] → Check cache
   ├─ Hit? ⚡ Return cached response
   └─ Miss? ↓
   ↓
[LoggingMiddleware] → Log start
   ↓ Call next ↓
   ↓
[MetricsMiddleware] → Record start time
   ↓ Call next ↓
   ↓
[ErrorHandlingMiddleware] → Retry wrapper
   ↓ Call next ↓
   ↓
[TimeoutMiddleware] → Timeout wrapper
   ↓ Call next ↓
   ↓
[LLM] → Process request
   ↓
Response flows back up (with caching, logging, metrics, etc.)
```

## Composition Patterns

### Pattern 1: Funnel (Fast Fail)

Order middlewares from cheapest to most expensive checks:

```csharp
.UseMiddleware(new RateLimitingMiddleware())      // ⚡ Fast
.UseMiddleware(new AuthenticationMiddleware())    // ⚡ Fast
.UseMiddleware(new AuthorizationMiddleware())     // ⚡ Fast
.UseMiddleware(new CachingMiddleware())           // ⚡ Fast
.UseMiddleware(new LoggingMiddleware())           // ⚡ Fast
.UseMiddleware(new ErrorHandlingMiddleware())     // 💧 Slower
```

**Benefit**: Expensive operations like LLM calls are only reached if earlier checks pass.

### Pattern 2: Layered (By Concern)

Group middlewares by their responsibility:

```csharp
// Security Layer
.UseMiddleware(new AuthenticationMiddleware())
.UseMiddleware(new AuthorizationMiddleware())

// Performance Layer
.UseMiddleware(new CachingMiddleware())

// Observability Layer
.UseMiddleware(new LoggingMiddleware())
.UseMiddleware(new MetricsMiddleware())

// Resilience Layer
.UseMiddleware(new ErrorHandlingMiddleware())
```

**Benefit**: Easy to understand and modify by concern.

### Pattern 3: Wrapper (Core First)

Put core business logic in the middle, with cross-cutting concerns around it:

```csharp
// Outer rings: Generic concerns
.UseMiddleware(new ErrorHandlingMiddleware())
.UseMiddleware(new TimeoutMiddleware(...))

// Inner rings: Business concerns
.UseMiddleware(new AuthenticationMiddleware())
.UseMiddleware(new RateLimitingMiddleware())

// Core: Memory and tools
.WithMemory(memoryService)
.WithTool(tool1)
```

**Benefit**: Like an onion - peel off layers to understand the system.

## Composition Recipes

### Recipe 1: High-Performance API

Goal: Maximize throughput, minimize latency

```csharp
.UseMiddleware(new RateLimitingMiddleware(perUser: true))
.UseMiddleware(new CachingMiddleware(ttl: TimeSpan.FromHours(1)))
.UseMiddleware(new TimeoutMiddleware(TimeSpan.FromSeconds(5)))
.UseMiddleware(new ErrorHandlingMiddleware(retries: 2))
```

### Recipe 2: Compliance & Security

Goal: Track everything, enforce policies

```csharp
.UseMiddleware(new AuthenticationMiddleware())
.UseMiddleware(new AuthorizationMiddleware())
.UseMiddleware(new LoggingMiddleware(toDatabase: true))
.UseMiddleware(new AuditMiddleware())
.UseMiddleware(new DataMaskingMiddleware(piiFields: ["ssn", "cc"]))
```

### Recipe 3: User-Facing Chatbot

Goal: Good UX, graceful degradation

```csharp
.UseMiddleware(new LoggingMiddleware())
.UseMiddleware(new CachingMiddleware())
.UseMiddleware(new ErrorHandlingMiddleware(retries: 3))
.UseMiddleware(new TimeoutMiddleware(TimeSpan.FromSeconds(30)))
.UseMiddleware(new FallbackMiddleware(fallbackText))
```

### Recipe 4: Enterprise Agent

Goal: Reliability, observability, security

```csharp
.UseMiddleware(new AuthenticationMiddleware())
.UseMiddleware(new RateLimitingMiddleware())
.UseMiddleware(new LoggingMiddleware(level: LogLevel.Debug))
.UseMiddleware(new MetricsMiddleware())
.UseMiddleware(new CircuitBreakerMiddleware())
.UseMiddleware(new ErrorHandlingMiddleware(retries: 5))
.UseMiddleware(new TimeoutMiddleware(TimeSpan.FromSeconds(60)))
```

## Composition Best Practices

### 1. **Order by Execution Cost**

```csharp
// ✅ GOOD: Cheap checks first
.UseMiddleware(new RateLimitingMiddleware())          // O(1)
.UseMiddleware(new CachingMiddleware())               // O(log n)
.UseMiddleware(new LoggingMiddleware())               // O(1)
.UseMiddleware(new ErrorHandlingMiddleware())         // O(retry count)

// ❌ BAD: Expensive checks first
.UseMiddleware(new ErrorHandlingMiddleware())         // Retries everything
.UseMiddleware(new LoggingMiddleware())               // Logs retries
.UseMiddleware(new RateLimitingMiddleware())          // Counts retries
```

### 2. **Order by Specificity**

```csharp
// ✅ GOOD: Generic first, specific later
.UseMiddleware(new LoggingMiddleware())               // Generic
.UseMiddleware(new MetricsMiddleware())               // Generic
.UseMiddleware(new AuthenticationMiddleware())        // Specific
.UseMiddleware(new PaymentValidationMiddleware())     // Very specific
```

### 3. **Group Related Concerns**

```csharp
// ✅ GOOD: Grouped by concern
.UseMiddleware(new AuthenticationMiddleware())
.UseMiddleware(new AuthorizationMiddleware())
.UseMiddleware(new AuditMiddleware())
// ... all security together ...

.UseMiddleware(new CachingMiddleware())
.UseMiddleware(new CompressionMiddleware())
// ... all performance together ...
```

### 4. **Avoid Redundant Middleware**

```csharp
// ❌ BAD: Two loggers doing same thing
.UseMiddleware(new LoggingMiddleware())
.UseMiddleware(new RequestLoggerMiddleware())  // Redundant

// ✅ GOOD: One logger with configuration
.UseMiddleware(new LoggingMiddleware(
    logRequests: true,
    logResponses: true,
    logDuration: true))
```

### 5. **Make Middleware Composable**

Design middleware that work well together:

```csharp
// ❌ BAD: Middleware that assumes it's alone
sealed class SingletonMiddleware : IAssistantMiddleware
{
    private static StaticState _state;  // Won't work with multiple instances
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Modifies global state...
    }
}

// ✅ GOOD: Middleware that cooperates
sealed class ComposableMiddleware : IAssistantMiddleware
{
    private readonly ComposableMiddlewareOptions _options;
    
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Passes state via context, doesn't use globals
    }
}
```

## Testing Composed Middleware

### Unit Test Each Middleware

```csharp
[Fact]
public async Task RateLimitingMiddleware_BlocksExceededLimit()
{
    var middleware = new RateLimitingMiddleware(maxRequests: 1);
    var mockNext = async (ctx, ct) => new AgentResponse("ok");
    
    var context = new AgentContext("test", []);
    
    // First request should pass
    var response1 = await middleware.InvokeAsync(context, mockNext);
    Assert.NotNull(response1);
    
    // Second request should fail
    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        await middleware.InvokeAsync(context, mockNext));
}
```

### Integration Test Middleware Stack

```csharp
[Fact]
public async Task MiddlewareStack_ExecutesInOrder()
{
    var log = new List<string>();
    
    var middleware1 = new LoggingMiddleware("M1", log);
    var middleware2 = new LoggingMiddleware("M2", log);
    var middleware3 = new LoggingMiddleware("M3", log);
    
    var agent = new AgentBuilder()
        .WithChatClient(mockChatClient)
        .UseMiddleware(middleware1)
        .UseMiddleware(middleware2)
        .UseMiddleware(middleware3)
        .Build();
    
    await agent.ChatAsync("test");
    
    // Verify execution order
    Assert.Equal(["M1", "M2", "M3"], log);
}
```

## Common Composition Mistakes

### ❌ Mistake 1: Too Many Middlewares

```csharp
// ❌ BAD: 15+ middlewares - hard to debug
.UseMiddleware(new M1())
.UseMiddleware(new M2())
// ... 13 more ...
.UseMiddleware(new M15())
```

**Solution**: Combine related middleware or use middleware with multiple responsibilities.

### ❌ Mistake 2: Wrong Order Causes Bugs

```csharp
// ❌ BAD: Caching before authentication
.UseMiddleware(new CachingMiddleware())
.UseMiddleware(new AuthenticationMiddleware())  // Cache returns unauthenticated responses!

// ✅ GOOD: Authenticate first
.UseMiddleware(new AuthenticationMiddleware())
.UseMiddleware(new CachingMiddleware())  // Cache only authenticated responses
```

### ❌ Mistake 3: Middleware That Depend on Order

```csharp
// ❌ BAD: M2 assumes M1 ran first
sealed class M1 : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Set context.Items["user"] = ...
        return await next(...);
    }
}

sealed class M2 : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        // Assumes context.Items["user"] exists!
        var user = (User)context.Items["user"];  // Null if M1 not before us
    }
}
```

**Solution**: Make middlewares independent or document their dependencies clearly.

## Monitoring Composed Middleware

### Metrics to Track

```csharp
// Per middleware
- Execution time
- Call count
- Error rate
- Cache hit rate (for cache middleware)
- Block rate (for gating middleware)

// Per composition
- Total pipeline time
- Breakdown by middleware
- Bottlenecks
```

### Example: Metrics Middleware

```csharp
sealed class MetricsMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(...)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            var response = await next(context, cancellationToken);
            _metrics.RecordSuccess(sw.Elapsed);
            return response;
        }
        catch (Exception ex)
        {
            _metrics.RecordError(sw.Elapsed, ex);
            throw;
        }
    }
}
```

## When to Create New Middleware vs. Extend Existing

### Create New Middleware If:

- ✅ It has a single, well-defined responsibility
- ✅ It can be tested independently
- ✅ It might be reused in other projects
- ✅ It doesn't depend on specific other middleware

### Extend Existing Middleware If:

- ✅ It extends behavior of existing middleware
- ✅ It adds a minor feature to existing middleware
- ✅ It's only used in this project

---

## Related Documentation

- [Middleware Design Patterns](middleware-design-patterns.md)
- [Middleware Execution Order](middleware-execution-order.md)
- [Middleware Best Practices](middleware-best-practices.md)
