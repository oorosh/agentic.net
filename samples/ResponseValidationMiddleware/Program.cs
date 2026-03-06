using Agentic.Builder;
using Agentic.Core;
using Microsoft.Extensions.AI;
using ResponseValidationMiddlewareSample;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
    Environment.Exit(1);
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   ResponseValidationMiddleware Sample     ║");
Console.WriteLine("║  Validates LLM output for quality         ║");
Console.WriteLine("╚══════════════════════════════════════════╝\n");

// Create an agent with response validation
var agent = new AgentBuilder()
    .WithChatClient(new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey).AsIChatClient())
    .WithMiddleware(new ResponseValidationMiddleware())
    .Build();

await agent.InitializeAsync();

Console.WriteLine("📝 Middleware: ResponseValidationMiddleware");
Console.WriteLine("   - Validates LLM response quality");
Console.WriteLine("   - Retries up to 2 times on validation failure");
Console.WriteLine("   - Checks for empty, short, incomplete responses");
Console.WriteLine("   - Detects common error patterns");
Console.WriteLine("   - Validates sentence structure\n");
Console.WriteLine(new string('─', 50) + "\n");

// Example 1: Normal good response
Console.WriteLine("Test 1: Normal Question\n");
try
{
    var response1 = await agent.ReplyAsync("What is photosynthesis? Explain in 2-3 sentences.");
    Console.WriteLine($"Response: {response1}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}
Console.WriteLine(new string('─', 50) + "\n");

// Example 2: Another normal question
Console.WriteLine("Test 2: Another Question\n");
try
{
    var response2 = await agent.ReplyAsync("Name 3 capital cities and their countries.");
    Console.WriteLine($"Response: {response2}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}
Console.WriteLine(new string('─', 50) + "\n");

// Example 3: Request that might get a weak response
Console.WriteLine("Test 3: Edge Case Question\n");
try
{
    var response3 = await agent.ReplyAsync("What is the meaning of xyz123?");
    Console.WriteLine($"Response: {response3}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}\n");
}

Console.WriteLine("\n✅ Response validation middleware example completed!");
Console.WriteLine("💡 Validation Checks Performed:");
Console.WriteLine("   - Response length (min 5 chars, max 10000)");
Console.WriteLine("   - Detects empty/incomplete responses");
Console.WriteLine("   - Finds error patterns and hallucinations");
Console.WriteLine("   - Validates sentence structure");
Console.WriteLine("   - Detects excessive word repetition");
Console.WriteLine("   - Checks for basic coherence");
Console.WriteLine("   - Retries on validation failure (up to 2 times)");
