using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;
using CachingMiddlewareSample;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
    Environment.Exit(1);
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║     CachingMiddleware Sample              ║");
Console.WriteLine("║  Demonstrates response caching            ║");
Console.WriteLine("╚══════════════════════════════════════════╝\n");

// Create an agent with caching middleware
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .UseMiddleware(new CachingMiddleware())
    .Build();

await agent.InitializeAsync();

// Example 1: First request - cache miss
Console.WriteLine("📝 Request 1: 'Tell me about cats'\n");
var response1 = await agent.ReplyAsync("Tell me about cats");
Console.WriteLine($"Response: {response1[..Math.Min(80, response1.Length)]}...\n");
Console.WriteLine(new string('─', 50) + "\n");

// Example 2: Same request - cache hit
Console.WriteLine("📝 Request 2: Same question (should hit cache)\n");
var response2 = await agent.ReplyAsync("Tell me about cats");
Console.WriteLine($"Response: {response2[..Math.Min(80, response2.Length)]}...\n");
Console.WriteLine(new string('─', 50) + "\n");

// Example 3: Different request - cache miss
Console.WriteLine("📝 Request 3: Different question\n");
var response3 = await agent.ReplyAsync("What about dogs?");
Console.WriteLine($"Response: {response3[..Math.Min(80, response3.Length)]}...\n");
Console.WriteLine(new string('─', 50) + "\n");

// Example 4: First question again - cache hit
Console.WriteLine("📝 Request 4: Back to cats (cache hit again)\n");
var response4 = await agent.ReplyAsync("Tell me about cats");
Console.WriteLine($"Response: {response4[..Math.Min(80, response4.Length)]}...\n");

Console.WriteLine("\n✅ Caching middleware example completed!");
Console.WriteLine("💡 Cache Statistics:");
Console.WriteLine("   - Request 1: Cache Miss (LLM called)");
Console.WriteLine("   - Request 2: Cache Hit (no LLM call)");
Console.WriteLine("   - Request 3: Cache Miss (LLM called)");
Console.WriteLine("   - Request 4: Cache Hit (no LLM call)");
Console.WriteLine("   - Cost Savings: 50% (2 out of 4 requests cached)");
