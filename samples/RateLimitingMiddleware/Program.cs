using Agentic.Builder;
using Agentic.Core;
using Microsoft.Extensions.AI;
using RateLimitingMiddlewareSample;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
    Environment.Exit(1);
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   RateLimitingMiddleware Sample           ║");
Console.WriteLine("║  Demonstrates rate limiting with tokens   ║");
Console.WriteLine("╚══════════════════════════════════════════╝\n");

// Create an agent with rate limiting (5 requests per minute for demo)
var agent = new AgentBuilder()
    .WithChatClient(new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey).AsIChatClient())
    .WithMiddleware(new RateLimitingMiddleware(maxRequestsPerMinute: 5))
    .Build();

await agent.InitializeAsync();

// Try making multiple requests to demonstrate rate limiting
for (int i = 1; i <= 7; i++)
{
    try
    {
        Console.WriteLine($"📝 Request {i}/7\n");
        var response = await agent.ReplyAsync($"Tell me something interesting number {i}.");
        { string r = response; Console.WriteLine($"Response: {r[..Math.Min(80, r.Length)]}...\n"); }
        Console.WriteLine(new string('─', 50) + "\n");

        if (i < 7)
        {
            await Task.Delay(500); // Small delay between requests
        }
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"Request {i}: {ex.Message}\n");
        Console.WriteLine(new string('─', 50) + "\n");
    }
}

Console.WriteLine("\n✅ Rate limiting middleware example completed!");
Console.WriteLine("💡 In a real application, you would:");
Console.WriteLine("   - Use per-user rate limits from authentication");
Console.WriteLine("   - Use distributed caching for multi-server setups");
Console.WriteLine("   - Integrate with your monitoring system");
