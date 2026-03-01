using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;
using ErrorHandlingMiddlewareSample;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
    Environment.Exit(1);
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   ErrorHandlingMiddleware Sample          ║");
Console.WriteLine("║  Demonstrates error handling & recovery   ║");
Console.WriteLine("╚══════════════════════════════════════════╝\n");

// Create an agent with error handling and timeout middleware
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .UseMiddleware(new ErrorHandlingMiddleware())
    .UseMiddleware(new TimeoutMiddleware(TimeSpan.FromSeconds(30)))
    .Build();

await agent.InitializeAsync();

Console.WriteLine("📝 Middleware Stack:");
Console.WriteLine("  1. ErrorHandlingMiddleware (retry with backoff)");
Console.WriteLine("  2. TimeoutMiddleware (30 second timeout)\n");

// Test 1: Normal request
Console.WriteLine("Test 1: Normal Request\n");
try
{
    var response1 = await agent.ReplyAsync("What is 2+2?");
    { string r = response1; Console.WriteLine($"Response: {r[..Math.Min(80, r.Length)]}...\n"); }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}
Console.WriteLine(new string('─', 50) + "\n");

// Test 2: Another request
Console.WriteLine("Test 2: Another Question\n");
try
{
    var response2 = await agent.ReplyAsync("Tell me a short joke.");
    { string r = response2; Console.WriteLine($"Response: {r[..Math.Min(80, r.Length)]}...\n"); }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}
Console.WriteLine(new string('─', 50) + "\n");

Console.WriteLine("\n✅ Error handling middleware example completed!");
Console.WriteLine("💡 Middleware Features:");
Console.WriteLine("   - ErrorHandlingMiddleware:");
Console.WriteLine("     • Retries failed requests up to 3 times");
Console.WriteLine("     • Uses exponential backoff (100ms, 200ms, 400ms)");
Console.WriteLine("     • Catches HTTP errors and cancellation");
Console.WriteLine("   - TimeoutMiddleware:");
Console.WriteLine("     • Enforces 30 second timeout");
Console.WriteLine("     • Prevents hanging requests");
Console.WriteLine("     • Provides user-friendly error message");
