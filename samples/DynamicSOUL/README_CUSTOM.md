# Dynamic SOUL.md Sample

This sample demonstrates how to use dynamic SOUL.md support in Agentic.NET - enabling agents to learn and adapt their personality based on conversations, plus implementing custom SOUL loaders for various sources.

## Features

- **Load SOUL.md**: Initialize agent with personality definition
- **Update Personality**: Modify agent personality during runtime  
- **Persist Changes**: Save updated personality back to SOUL.md
- **Reload Personality**: Reload personality from disk
- **Custom Loaders**: Implement your own SOUL loading logic from any source

## How It Works

### Initial Agent Creation

```csharp
var soulLoader = new FileSystemSoulLoader(Directory.GetCurrentDirectory());

var agent = new AgentBuilder()
    .WithChatClient(new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini"))
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

## Implementing Custom SOUL Loaders

Users can implement their own `ISoulLoader` to load SOUL from any source. Here are examples:

### Read-Only Loader (ISoulLoader)

```csharp
public sealed class DatabaseSoulLoader : ISoulLoader
{
    private readonly string _connectionString;

    public async Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var command = new SqlCommand(
            "SELECT Name, Role, Personality, Rules FROM Souls LIMIT 1", 
            connection);
        
        var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new SoulDocument
        {
            Name = reader.GetString(0),
            Role = reader.GetString(1),
            Personality = reader.GetString(2),
            Rules = reader.GetString(3)
        };
    }

    public async Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default)
    {
        return await LoadSoulAsync(cancellationToken);
    }
}

// Usage
var loader = new DatabaseSoulLoader("Server=localhost;Database=agents");
var agent = new AgentBuilder()
    .WithChatClient(new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini"))
    .WithSoul(loader)
    .Build();
```

### Read-Write Loader (IPersistentSoulLoader)

For persistent storage with write support:

```csharp
public sealed class PersistentDatabaseSoulLoader : IPersistentSoulLoader
{
    private readonly string _connectionString;

    public async Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default)
    {
        // Load implementation (same as above)
    }

    public async Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default)
    {
        return await LoadSoulAsync(cancellationToken);
    }

    public async Task UpdateSoulAsync(SoulDocument soul, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var command = new SqlCommand(
            "UPDATE Souls SET Personality = @p, Rules = @r WHERE Name = @n",
            connection);
        
        command.Parameters.AddWithValue("@p", soul.Personality ?? string.Empty);
        command.Parameters.AddWithValue("@r", soul.Rules ?? string.Empty);
        command.Parameters.AddWithValue("@n", soul.Name);
        
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

// Update persists to database
var updated = agent.Soul with { Personality = "More friendly" };
await agent.UpdateSoulAsync(updated);
```

### API-Based Loader

```csharp
public sealed class ApiSoulLoader : ISoulLoader
{
    private readonly HttpClient _http;
    private readonly string _apiUrl;

    public async Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"{_apiUrl}/soul", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSoul(content);
    }

    public async Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default)
    {
        return await LoadSoulAsync(cancellationToken);
    }

    private SoulDocument ParseSoul(string content)
    {
        // Parse your format...
        return new SoulDocument { /* ... */ };
    }
}

var loader = new ApiSoulLoader("https://api.example.com");
var agent = new AgentBuilder()
    .WithChatClient(new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini"))
    .WithSoul(loader)
    .Build();
```

## Custom Loader Templates

See `Loaders/CustomSoulLoaders.cs` in the main library for implementation templates and ideas.

The library provides interfaces for you to implement custom loaders for any data source. Common implementation patterns include:

### In-Memory Loader

Store SOUL in memory - useful for testing:

```csharp
public sealed class InMemorySoulLoader : ISoulLoader
{
    private readonly SoulDocument _soul;

    public InMemorySoulLoader(SoulDocument soul) => _soul = soul;

    public Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<SoulDocument?>(_soul);
    
    public Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default)
        => LoadSoulAsync(cancellationToken);
}

var soul = new SoulDocument
{
    Name = "TestBot",
    Role = "Test Assistant",
    Personality = "Precise and thorough"
};

var loader = new InMemorySoulLoader(soul);
```

### HTTP/API Loader

Load SOUL from a REST API:

```csharp
public sealed class HttpSoulLoader : ISoulLoader
{
    private readonly HttpClient _http;
    private readonly string _apiUrl;
    
    // Implementation...
}

var loader = new HttpSoulLoader("https://api.example.com/soul.md");
```

### Lazy-Loading Wrapper

Defer SOUL loading until first use:

```csharp
public sealed class LazySoulLoader : ISoulLoader
{
    private readonly ISoulLoader _inner;
    private SoulDocument? _cached;
    private bool _loaded;
    
    // Implementation...
}

var fileLoader = new FileSystemSoulLoader("./SOUL.md");
var lazyLoader = new LazySoulLoader(fileLoader);
// SOUL loads on first agent call, not initialization
```

### Fallback Pattern

Try multiple loaders in sequence:

```csharp
public sealed class FallbackSoulLoader : ISoulLoader
{
    private readonly ISoulLoader[] _loaders;
    
    // Try each loader until one succeeds
}

var httpLoader = new HttpSoulLoader("https://api.example.com/soul.md");
var fileLoader = new FileSystemSoulLoader("./SOUL.md");
var fallbackLoader = new FallbackSoulLoader(httpLoader, fileLoader);

// Tries HTTP first, falls back to file if HTTP fails
```

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

## Running the Samples

### Main Demo (File-based SOUL)

```bash
export OPENAI_API_KEY="your-api-key"
dotnet run --project samples/DynamicSOUL/DynamicSOUL.csproj
```

### Custom Loaders Demo

```bash
export OPENAI_API_KEY="your-api-key"
dotnet run --project samples/DynamicSOUL/DynamicSOUL.csproj CustomLoaderDemo
```

## Use Cases

1. **User Preference Learning**: Adapt agent tone based on user feedback
2. **Multi-Tenancy**: Load custom personality per user/organization
3. **Multi-Source Loading**: Database, API, or file with fallback
4. **A/B Testing**: Load different personalities and measure performance
5. **Skill Development**: Update capabilities as agent learns
6. **Dynamic Generation**: Generate SOUL based on prompts
7. **Caching**: Cache in Redis or memory for performance
8. **Versioning**: Track personality changes over time

## Key APIs

### ISoulLoader (Read-Only)

```csharp
public interface ISoulLoader
{
    Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default);
    Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default);
}
```

Implement this for read-only sources (files, APIs, databases).

### IPersistentSoulLoader (Read-Write)

```csharp
public interface IPersistentSoulLoader : ISoulLoader
{
    Task UpdateSoulAsync(SoulDocument soul, CancellationToken cancellationToken = default);
}
```

Implement this for writeable sources (files, writable databases).

### Agent Methods

```csharp
// Reload soul from current source
async Task UpdateSoulAsync(CancellationToken cancellationToken)

// Update and persist soul (requires IPersistentSoulLoader)
async Task UpdateSoulAsync(SoulDocument updatedSoul, CancellationToken cancellationToken)
```

## Implementation Checklist

When implementing a custom soul loader:

- [ ] Inherit from `ISoulLoader` (or `IPersistentSoulLoader` for write support)
- [ ] Implement `LoadSoulAsync()` to retrieve SoulDocument
- [ ] Implement `ReloadSoulAsync()` for cache invalidation
- [ ] Handle exceptions gracefully
- [ ] Add proper cancellation token support
- [ ] Consider caching for performance
- [ ] Format/parse SOUL in your chosen format
- [ ] Write unit tests
- [ ] Pass to `AgentBuilder.WithSoul()`

## Popular Custom Loader Ideas

- **DatabaseSoulLoader** - PostgreSQL, SQLite, SQL Server
- **CosmosDbSoulLoader** - Azure Cosmos DB
- **S3SoulLoader** - AWS S3 buckets
- **AzureBlobSoulLoader** - Azure Blob Storage  
- **ConfigServerSoulLoader** - Spring Cloud Config
- **GitHubSoulLoader** - GitHub repository
- **FirestoreSoulLoader** - Google Cloud Firestore
- **MongoDbSoulLoader** - MongoDB collections
- **RedisSoulLoader** - Redis caching
- **DynamicSoulLoader** - LLM-generated personalities

## Next Steps

- Implement personality metrics extraction
- Build personality recommendation engine
- Add conversation analysis for automatic personality updates
- Create personality versioning system
- Implement personality templates for common agent types
- Build UI for personality management
