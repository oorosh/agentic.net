# Middleware Execution Order

This document explains how middleware is executed in Agentic.NET.

## Key Principle

**Middlewares execute in the order they are registered**, creating a pipeline where each middleware can process the request before and after the next layer.

## How It Works

### Registration

```csharp
var assistant = new AgentBuilder()
    .WithChatClient(chatClient)
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
    .WithChatClient(chatClient)
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

## Two Key Patterns

### Pattern 1: Short-Circuit (Input Validation/Gating)

Use this when you want to **prevent downstream layers from executing**:

```csharp
public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
{
    if (!IsValid(context.Input))
    {
        // Return early WITHOUT calling next()
        // This prevents all downstream layers (including LLM) from executing
        return Task.FromResult(new AgentResponse("Invalid input rejected"));
    }
    
    // Only called if validation passes
    return next(context, cancellationToken);
}
```

**Benefits:**
- Saves costs by avoiding LLM calls for invalid inputs
- Improves performance for gate-keeping logic
- Perfect for input validation and compliance checks

### Pattern 2: Filter (Output Validation/Modification)

Use this when you want to **allow downstream layers to execute, then modify the result**:

```csharp
public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
{
    // Always call next() to allow deeper layers to execute
    var response = await next(context, cancellationToken);
    
    // Post-processing: modify response after it comes back
    var filtered = ModifyContent(response.Content);
    
    // Return modified response
    return new AgentResponse(filtered, response.ToolCalls);
}
```

**Benefits:**
- Allows LLM to generate responses
- Filters or censors output before returning to user
- Perfect for content moderation and response filtering
- User never sees inappropriate content

## SafeguardMiddleware Example: Both Patterns

The SafeguardMiddleware sample demonstrates both patterns:

```csharp
// Pattern 1: Input validation with short-circuit
.UseMiddleware(new PromptGuardMiddleware())    // Blocks bad prompts
.UseMiddleware(new ResponseGuardMiddleware())  // Filters bad responses
```

### Execution Scenarios

**Scenario 1: Bad Prompt** → Short-circuits at PromptGuard
```
Input: "this is bad"
  → PromptGuardMiddleware detects "bad"
  → Returns immediately (does NOT call next())
  → LLM never called ✓
  → User sees: "I'm sorry, but I cannot process..."
```

**Scenario 2: Good Prompt, Good Response** → Normal flow
```
Input: "hello world"
  → PromptGuardMiddleware passes
  → ResponseGuardMiddleware gets LLM response
  → No filtering needed (no "bad" found)
  → User sees: "Echo: hello world"
```

**Scenario 3: Good Prompt, Bad Response** → Filtered at ResponseGuard
```
Input: "test"
  → PromptGuardMiddleware passes
  → LLM returns: "This contains bad content."
  → ResponseGuardMiddleware filters
  → User sees: "This contains [censored] content." ✓
```

## Best Practices

1. **Order Matters**: Place more critical validations early
   - Security/safeguards before complex processing
   - Context injection before business logic

2. **Short-Circuit Early**: Return responses without calling next() to avoid unnecessary processing
   - Use for input validation and gating
   - Saves costs by avoiding LLM calls

3. **Post-Processing**: Modify responses after next() completes for filtering, logging, etc.
   - Use for output validation
   - Ensures LLM gets executed before filtering

4. **Error Handling**: Middlewares should handle exceptions appropriately or let them propagate

5. **Performance**: Avoid expensive operations in the pre-LLM phase; they affect all requests

6. **Combine Patterns**: Use short-circuit for validation + filtering for sanitization
