using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;
using AuthenticationMiddlewareSample;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
    Environment.Exit(1);
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║ AuthenticationMiddleware Sample           ║");
Console.WriteLine("║  Demonstrates authentication & authz      ║");
Console.WriteLine("╚══════════════════════════════════════════╝\n");

// Define user roles for authorization
var userRoles = new Dictionary<string, string[]>
{
    { "alice", ["admin", "user"] },
    { "bob", ["user"] },
    { "charlie", [] }
};

// Create an agent with authentication and authorization middlewares
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .UseMiddleware(new AuthenticationMiddleware())
    .UseMiddleware(new AuthorizationMiddleware(userRoles))
    .Build();

await agent.InitializeAsync();

Console.WriteLine("📝 Middleware Stack:");
Console.WriteLine("  1. AuthenticationMiddleware (API key validation)");
Console.WriteLine("  2. AuthorizationMiddleware (Role-based access)\n");

// Test 1: No authentication
Console.WriteLine("Test 1: Request without API key\n");
try
{
    var response1 = await agent.ReplyAsync("Hello!");
    Console.WriteLine($"Response: {response1}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}
Console.WriteLine(new string('─', 50) + "\n");

// Test 2: Valid authentication but unknown user
Console.WriteLine("Test 2: Valid API key but unauthorized user\n");
try
{
    var response2 = await agent.ReplyAsync("API_KEY:test-key-123 USER:unknown Tell me something");
    Console.WriteLine($"Response: {response2}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}
Console.WriteLine(new string('─', 50) + "\n");

// Test 3: Valid authentication and authorization
Console.WriteLine("Test 3: Valid API key and authorized user (alice)\n");
try
{
    var response3 = await agent.ReplyAsync("API_KEY:test-key-123 USER:alice What's the weather?");
    { string r = response3; Console.WriteLine($"Response: {r[..Math.Min(100, r.Length)]}...\n"); }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}
Console.WriteLine(new string('─', 50) + "\n");

Console.WriteLine("\n✅ Authentication/Authorization middleware example completed!");
Console.WriteLine("💡 In a real application, you would:");
Console.WriteLine("   - Validate JWT tokens from HTTP headers");
Console.WriteLine("   - Check user roles from identity provider");
Console.WriteLine("   - Enforce role-based access policies");
Console.WriteLine("   - Log authentication attempts for audit trails");
