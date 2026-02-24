using Agentic.Core;
using Agentic.Middleware;

namespace ErrorHandlingMiddlewareSample;

/// <summary>
/// ErrorHandlingMiddleware demonstrates how to handle errors gracefully,
/// recover from failures, and provide meaningful error responses to users.
/// </summary>
sealed class ErrorHandlingMiddleware : IAssistantMiddleware
{
    private readonly int _maxRetries = 3;
    private readonly TimeSpan _initialBackoff = TimeSpan.FromMilliseconds(100);

    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < _maxRetries)
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                attempt++;

                if (attempt < _maxRetries)
                {
                    var backoff = TimeSpan.FromMilliseconds(
                        _initialBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(
                        $"⚠️  Network error (attempt {attempt}/{_maxRetries}). " +
                        $"Retrying in {backoff.TotalMilliseconds:F0}ms...");
                    Console.ResetColor();

                    await Task.Delay(backoff, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Request was cancelled");
                Console.ResetColor();

                return new AgentResponse("Request was cancelled. Please try again.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Unexpected error: {ex.GetType().Name}");
                Console.ResetColor();

                return new AgentResponse(
                    $"An unexpected error occurred: {ex.Message}");
            }
        }

        // All retries exhausted
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ Request failed after {_maxRetries} attempts");
        Console.ResetColor();

        return new AgentResponse(
            "The service is temporarily unavailable. Please try again later.");
    }
}

/// <summary>
/// TimeoutMiddleware demonstrates how to handle requests that take too long.
/// </summary>
sealed class TimeoutMiddleware(TimeSpan timeout) : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            return await next(context, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"⏱️  Request timed out after {timeout.TotalSeconds:F1}s");
            Console.ResetColor();

            return new AgentResponse(
                $"Request timed out after {timeout.TotalSeconds:F1} seconds. Please try a simpler question.");
        }
    }
}
