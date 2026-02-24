# Middleware Execution Order

This document explains how middleware is executed in Agentic.NET.

## Key Principle

**Middlewares execute in the order they are registered**, creating a pipeline where each middleware can process the request before and after the next layer.

## How It Works

### Registration

```csharp
var assistant = new AgentBuilder()
    .WithModelProvider(provider)
    .WithMemory(memory)              // MemoryMiddleware auto-inserted at position 0
    .UseMiddleware(new FirstMw())    // Registered 1st
    .UseMiddleware(new SecondMw())   // Registered 2nd
    .Build();
```

### Execution Order (First to Last)

```
User Input
    ↓
[FirstMw] (registered 1st)
    ↓ calls next() →
[SecondMw] (registered 2nd)
    ↓ calls next() →
[MemoryMiddleware] (auto-inserted at position 0)
    ↓ calls next() →
[LLM Model]
    ↓ returns response
[MemoryMiddleware] (post-processing continues)
    ↓ returns to SecondMw
[SecondMw] (post-processing continues)
    ↓ returns to FirstMw
[FirstMw] (post-processing continues)
    ↓ returns
Final Response to User
```

## Implementation Details

### Pipeline Construction (Agent.cs, lines 95-99)

The middleware pipeline is built using **reverse iteration**:

```csharp
AgentHandler handler = async (ctx, ct) => await _model.CompleteAsync(ctx.WorkingMessages.ToList(), ct);

foreach (var middleware in _middlewares.Reverse())
{
    var next = handler;
    handler = (ctx, ct) => middleware.InvokeAsync(ctx, next, ct);
}
```

This reverse iteration creates a wrapper chain where:
1. Start with the LLM as the innermost handler
2. For each middleware (in reverse order), wrap the current handler
3. Result: First-registered middleware becomes outermost

### Example with Two Middlewares

Given: `[FirstMw, SecondMw]`

After reverse iteration:
```
handler = FirstMw(SecondMw(LLM))
```

When executed:
1. `FirstMw.InvokeAsync()` is called with `next = SecondMw`
2. `FirstMw` can process pre-LLM (before calling `next()`)
3. When `FirstMw` calls `next()`, it invokes `SecondMw.InvokeAsync()` with `next = LLM`
4. `SecondMw` can process pre-LLM (before calling `next()`)
5. When `SecondMw` calls `next()`, it invokes the LLM
6. LLM returns response
7. `SecondMw` can process post-LLM (after `await next()` completes)
8. `SecondMw` returns to `FirstMw`
9. `FirstMw` can process post-LLM (after `await next()` completes)
10. `FirstMw` returns final response

## Middleware Capabilities

### Pre-LLM Processing (Before `await next()`)

Middlewares can:
- Validate and filter user input
- Inject context into working messages
- Short-circuit and return a response without calling the LLM
- Modify the conversation context

### Post-LLM Processing (After `await next()`)

Middlewares can:
- Filter or censor response content
- Store results in memory or databases
- Log or audit the exchange
- Modify response format

## MemoryMiddleware Auto-Insertion

If memory is configured and no custom `MemoryMiddleware` is explicitly added, it's automatically inserted at position 0:

```csharp
if (_memoryService is not null && pipeline.All(middleware => middleware is not MemoryMiddleware))
{
    pipeline.Insert(0, new MemoryMiddleware(_memoryService, _embeddingProvider));
}
```

This ensures:
- Memory context is always injected early in the pipeline
- User middlewares can override this by providing their own MemoryMiddleware

## Example: SafeguardMiddleware

From `samples/SafeguardMiddleware`:

```csharp
var assistant = new AgentBuilder()
    .WithModelProvider(provider)
    .UseMiddleware(new PromptGuardMiddleware())    // Executes 1st
    .UseMiddleware(new ResponseGuardMiddleware())  // Executes 2nd
    .Build();
```

### Execution Flow

1. **PromptGuardMiddleware** enters
   - Checks if input contains "bad"
   - If yes, returns safe response without calling next()
   - If no, calls next()

2. **ResponseGuardMiddleware** enters (if first allowed it)
   - Calls next() immediately
   - After await next(), filters response content (replaces "bad" with "[censored]")
   - Returns filtered response

3. **LLM** executes (if first two allowed it)

### Example Execution

Input: `"this is bad"`

```
PromptGuardMiddleware.InvokeAsync()
  → Detects "bad" in input
  → Returns immediately: "I'm sorry, but I cannot process..."
  → ResponseGuardMiddleware never executed
```

Input: `"test"`

```
PromptGuardMiddleware.InvokeAsync()
  → No "bad" detected
  → Calls next()
  
  ResponseGuardMiddleware.InvokeAsync()
    → Calls next()
    
    LLM
      → Returns "This contains bad content."
    
    → After next() completes
    → Filters: "This contains bad content." → "This contains [censored] content."
    → Returns filtered response
  
  → After next() completes
  → Returns filtered response to user
```

## Best Practices

1. **Order Matters**: Place more critical validations early
   - Security/safeguards before complex processing
   - Context injection before business logic

2. **Short-Circuit Early**: Return responses without calling next() to avoid unnecessary processing

3. **Post-Processing**: Modify responses after next() completes for filtering, logging, etc.

4. **Error Handling**: Middlewares should handle exceptions appropriately or let them propagate

5. **Performance**: Avoid expensive operations in the pre-LLM phase; they affect all requests
