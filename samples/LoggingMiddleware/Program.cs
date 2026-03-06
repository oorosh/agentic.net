using Agentic.Builder;
using Agentic.Core;
using LoggingMiddlewareSample;
using Microsoft.Extensions.AI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
    Environment.Exit(1);
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║     LoggingMiddleware Sample              ║");
Console.WriteLine("║  Demonstrates request/response logging    ║");
Console.WriteLine("╚══════════════════════════════════════════╝\n");

// Create an agent with logging middleware
var agent = new AgentBuilder()
    .WithChatClient(new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey).AsIChatClient())
    .WithMiddleware(new LoggingMiddleware())
    .Build();

await agent.InitializeAsync();

// Example 1: Simple greeting
Console.WriteLine("📝 Example 1: Simple Greeting\n");
var response1 = await agent.ReplyAsync("Hello! What's your name?");
Console.WriteLine($"Response: {response1}\n");
Console.WriteLine(new string('─', 50) + "\n");

// Example 2: Question with context
Console.WriteLine("📝 Example 2: Mathematical Question\n");
var response2 = await agent.ReplyAsync("What is 25 * 4?");
Console.WriteLine($"Response: {response2}\n");
Console.WriteLine(new string('─', 50) + "\n");

// Example 3: Longer conversation
Console.WriteLine("📝 Example 3: Multi-turn Conversation\n");
var response3a = await agent.ReplyAsync("Tell me a joke about programming.");
Console.WriteLine($"Response: {response3a}\n");

var response3b = await agent.ReplyAsync("Can you make it shorter?");
Console.WriteLine($"Response: {response3b}\n");

Console.WriteLine("\n✅ Logging middleware example completed!");
