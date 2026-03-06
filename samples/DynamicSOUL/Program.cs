using Agentic.Builder;
using Agentic.Loaders;
using Microsoft.Extensions.AI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable not set");

// Create an agent with initial SOUL.md
Console.WriteLine("=== Dynamic SOUL.md Demo ===\n");

var soulLoader = new FileSystemSoulLoader(Directory.GetCurrentDirectory());

var agent = new AgentBuilder()
    .WithChatClient(new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey).AsIChatClient())
    .WithSoul(soulLoader)
    .Build();

await agent.InitializeAsync();

// Display initial personality
if (agent.Soul is { } initialSoul)
{
    Console.WriteLine($"Initial Agent: {initialSoul.Name}");
    Console.WriteLine($"Personality: {initialSoul.Personality}\n");
}

// Simulate conversation
Console.WriteLine("Turn 1: Initial Response");
var response1 = await agent.ReplyAsync("What is .NET?");
Console.WriteLine($"Agent: {response1}\n");

// Update personality based on feedback
Console.WriteLine("Updating personality to be more casual...\n");

if (agent.Soul is { } soul)
{
    var updatedSoul = soul with
    {
        Personality = "- Tone: Friendly and approachable\n" +
                     "- Style: Casual, conversational, less formal\n" +
                     "- Approach: Keep it simple and relatable",
        Rules = "- Always be friendly and approachable\n" +
               "- Use casual language\n" +
               "- Make complex topics easy to understand"
    };

    // Save the updated personality
    await agent.UpdateSoulAsync(updatedSoul);
    Console.WriteLine("✓ Personality updated and saved to SOUL.md\n");

    if (agent.Soul is { } updatedAgentSoul)
    {
        Console.WriteLine($"Updated Personality: {updatedAgentSoul.Personality}\n");
    }
}

// Continue conversation with new personality
Console.WriteLine("Turn 2: After Personality Update");
var response2 = await agent.ReplyAsync("Can you explain async/await?");
Console.WriteLine($"Agent: {response2}\n");

// Reload soul from disk to verify persistence
Console.WriteLine("Reloading SOUL.md from disk...");
await agent.UpdateSoulAsync();

if (agent.Soul is { } reloadedSoul)
{
    Console.WriteLine($"✓ Reloaded: {reloadedSoul.Name}");
    Console.WriteLine($"Personality: {reloadedSoul.Personality}\n");
}

// Final response with reloaded personality
Console.WriteLine("Turn 3: After Reloading from Disk");
var response3 = await agent.ReplyAsync("What are generics?");
Console.WriteLine($"Agent: {response3}\n");

Console.WriteLine("=== Demo Complete ===");
Console.WriteLine("Note: Check SOUL.md in the sample directory to see the persisted changes.");
