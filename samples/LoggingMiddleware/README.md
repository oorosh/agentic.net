# LoggingMiddleware Sample

This sample demonstrates how to implement a **LoggingMiddleware** that tracks request/response cycles for debugging, auditing, and monitoring.

## Overview

LoggingMiddleware implements the **filter pattern** - it allows the request to pass through to the next handler and then logs both the request and response.

## Key Features

- **Request Logging**: Logs the timestamp, history size, and user input
- **Response Logging**: Logs the timestamp, response duration, and agent response
- **Performance Monitoring**: Tracks how long each request takes to process
- **Formatted Output**: Uses colored console output for easy readability

## How It Works

```csharp
public async Task<AgentResponse> InvokeAsync(
    AgentContext context,
    AgentHandler next,
    CancellationToken cancellationToken = default)
{
    var requestTime = DateTime.UtcNow;
    
    // Log incoming request
    LogRequest(context);
    
    // Let the request pass through
    var response = await next(context, cancellationToken);
    
    // Log outgoing response
    LogResponse(response, DateTime.UtcNow - requestTime);
    
    return response;
}
```

## Use Cases

- **Debugging**: Understand what inputs/outputs your agent is processing
- **Audit Trails**: Track all conversations for compliance
- **Performance Analysis**: Measure response times to identify bottlenecks
- **Monitoring**: Build dashboards with real-time agent activity
- **Testing**: Verify expected behavior during development

## Running the Sample

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project samples/LoggingMiddleware/LoggingMiddleware.csproj
```

## Sample Output

```
┌─ INCOMING REQUEST ─────────────────────────
│ Timestamp: 2024-02-24 15:30:45.123
│ History Size: 0
│ User Input:
│   Hello! What's your name?
└─────────────────────────────────────────────

┌─ OUTGOING RESPONSE ────────────────────────
│ Timestamp: 2024-02-24 15:30:46.456
│ Duration: 1333ms
│ Response:
│   I'm Claude, an AI assistant made by Anthropic.
└─────────────────────────────────────────────
```

## Middleware Pattern: Filter

This middleware uses the **filter pattern** because it:
1. Allows the request to pass through (`await next()`)
2. Observes/logs the response
3. Returns the response to the caller

Perfect for non-blocking concerns like logging, metrics, and monitoring.

## Advanced Enhancements

You can extend this middleware to:
- Write logs to a file or database
- Send metrics to monitoring systems (Prometheus, Application Insights)
- Implement structured logging (JSON format)
- Add filtering/sampling to reduce log volume
- Include token counts and cost calculations
- Track conversation patterns and trends

## Related Samples

- **SafeguardMiddleware**: Content filtering with short-circuit pattern
- **RateLimitingMiddleware**: Rate limiting with short-circuit pattern
- **ErrorHandlingMiddleware**: Error handling and recovery
