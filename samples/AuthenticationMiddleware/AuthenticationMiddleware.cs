using Agentic.Core;
using Agentic.Middleware;

namespace AuthenticationMiddlewareSample;

/// <summary>
/// AuthenticationMiddleware demonstrates how to implement authentication/authorization.
/// In a real application, this would validate JWT tokens, API keys, or other credentials.
/// </summary>
sealed class AuthenticationMiddleware : IAssistantMiddleware
{
    private readonly HashSet<string> _validApiKeys = ["test-key-123", "demo-key-456"];

    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        // In a real app, get the API key from:
        // - HTTP headers (Authorization: Bearer ...)
        // - Query parameters
        // - Middleware context
        var apiKey = GetApiKey(context);

        if (string.IsNullOrEmpty(apiKey) || !_validApiKeys.Contains(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("❌ Authentication Failed: Invalid or missing API key");
            Console.ResetColor();

            return new AgentResponse(
                "Authentication failed. Please provide a valid API key.");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✅ Authentication Successful");
        Console.ResetColor();

        // Continue processing
        return await next(context, cancellationToken);
    }

    private static string? GetApiKey(AgentContext context)
    {
        // Extract API key from user input or context
        // Format: "API_KEY:<key> <actual_request>"
        // Example: "API_KEY:test-key-123 Hello, world!"
        
        if (context.Input.StartsWith("API_KEY:"))
        {
            var parts = context.Input.Split(' ', 2);
            if (parts.Length >= 1)
            {
                var keyPart = parts[0].Replace("API_KEY:", "");
                return keyPart;
            }
        }

        return null;
    }
}

/// <summary>
/// AuthorizationMiddleware demonstrates how to implement authorization
/// based on user roles or permissions.
/// </summary>
sealed class AuthorizationMiddleware(Dictionary<string, string[]> userRoles) : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(context);

        if (string.IsNullOrEmpty(userId) || !userRoles.ContainsKey(userId))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("❌ Authorization Failed: User not authorized");
            Console.ResetColor();

            return new AgentResponse(
                "Authorization failed. You don't have permission to use this agent.");
        }

        var roles = userRoles[userId];
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"👤 User: {userId}");
        Console.WriteLine($"📋 Roles: {string.Join(", ", roles)}");
        Console.ResetColor();

        return await next(context, cancellationToken);
    }

    private static string? GetUserId(AgentContext context)
    {
        // Extract user ID from input
        // Format: "USER:<user_id> <actual_request>"
        if (context.Input.StartsWith("USER:"))
        {
            var parts = context.Input.Split(' ', 2);
            if (parts.Length >= 1)
            {
                var userPart = parts[0].Replace("USER:", "");
                return userPart;
            }
        }

        return null;
    }
}
