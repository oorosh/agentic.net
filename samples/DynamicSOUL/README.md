# Dynamic SOUL.md Sample

This sample demonstrates how to use dynamic SOUL.md support in Agentic.NET - enabling agents to learn and adapt their personality based on conversations.

## Features

- **Load SOUL.md**: Initialize agent with personality definition
- **Update Personality**: Modify agent personality during runtime
- **Persist Changes**: Save updated personality back to SOUL.md
- **Reload Personality**: Reload personality from disk

## How It Works

### Initial Agent Creation

```csharp
var soulLoader = new FileSystemSoulLoader(Directory.GetCurrentDirectory());

var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul(soulLoader)
    .Build();

await agent.InitializeAsync();
```

### Updating Personality

Update the agent's personality based on user feedback or conversation insights:

```csharp
var updatedSoul = agent.Soul with
{
    Personality = "New personality traits...",
    Rules = "Updated rules..."
};

await agent.UpdateSoulAsync(updatedSoul);
```

This both updates the in-memory soul and persists it to `SOUL.md`.

### Reloading from Disk

Reload the soul from disk to get the latest version:

```csharp
await agent.UpdateSoulAsync();
```

This clears the cache and reloads from `SOUL.md`.

## SOUL.md Structure

```markdown
# AgentName

## Role
Agent's primary role and responsibilities

## Personality
- Tone and communication style
- Approach to interactions

## Rules
- Key principles to follow
- Behavioral constraints

## Tools
Available tools and capabilities

## Output Format
How the agent should format responses

## Handoffs
Other agents or systems to hand off to
```

## Running the Sample

```bash
export OPENAI_API_KEY="your-api-key"
dotnet run --project samples/DynamicSOUL/DynamicSOUL.csproj
```

## Use Cases

1. **User Preference Learning**: Adapt agent tone based on user feedback
2. **Skill Development**: Update capabilities as the agent learns new tools
3. **Behavior Refinement**: Adjust rules based on observed interactions
4. **A/B Testing**: Test different personalities and measure performance
5. **Multi-Tenancy**: Customize agent personality per user or organization

## Key APIs

### ISoulLoader
```csharp
Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken)
Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken)
```

### IPersistentSoulLoader
```csharp
Task UpdateSoulAsync(SoulDocument soul, CancellationToken cancellationToken)
```

### Agent
```csharp
async Task UpdateSoulAsync(CancellationToken cancellationToken)  // Reload from disk
async Task UpdateSoulAsync(SoulDocument updatedSoul, CancellationToken cancellationToken)  // Update and persist
```

## Next Steps

- Implement personality metrics extraction
- Build personality recommendation engine
- Add conversation analysis for automatic personality updates
- Create personality versioning system
- Implement personality templates for common agent types
