using Agentic.Core;
using Agentic.Middleware;

namespace LoggingMiddlewareSample;

/// <summary>
/// LoggingMiddleware demonstrates how to log incoming requests and outgoing responses.
/// This is useful for:
/// - Debugging agent behavior
/// - Audit trails
/// - Performance monitoring
/// - Understanding agent decision-making
/// </summary>
sealed class LoggingMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        var requestTime = DateTime.UtcNow;

        // Log incoming request
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("┌─ INCOMING REQUEST ─────────────────────────");
        Console.WriteLine($"│ Timestamp: {requestTime:yyyy-MM-dd HH:mm:ss.fff}");
        Console.WriteLine($"│ History Size: {context.History.Count}");
        Console.WriteLine($"│ User Input:");
        Console.WriteLine($"│   {context.Input[..Math.Min(60, context.Input.Length)]}");
        if (context.Input.Length > 60)
            Console.WriteLine($"│   (... {context.Input.Length - 60} more characters)");
        Console.WriteLine("└─────────────────────────────────────────────");
        Console.ResetColor();

        // Process through the agent
        var response = await next(context, cancellationToken);

        // Log outgoing response
        var responseTime = DateTime.UtcNow;
        var duration = responseTime - requestTime;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("┌─ OUTGOING RESPONSE ────────────────────────");
        Console.WriteLine($"│ Timestamp: {responseTime:yyyy-MM-dd HH:mm:ss.fff}");
        Console.WriteLine($"│ Duration: {duration.TotalMilliseconds:F0}ms");
        Console.WriteLine($"│ Response:");
        Console.WriteLine($"│   {response.Content[..Math.Min(60, response.Content.Length)]}");
        if (response.Content.Length > 60)
            Console.WriteLine($"│   (... {response.Content.Length - 60} more characters)");
        Console.WriteLine("└─────────────────────────────────────────────");
        Console.ResetColor();
        Console.WriteLine();

        return response;
    }
}
