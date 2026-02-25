using Agentic.Builder;

namespace DynamicSOUL;

/// <summary>
/// This file demonstrates how to implement custom SOUL loaders for different data sources.
/// To run this demo, create a Main() method and call CustomLoaderDemo.Run()
/// </summary>
internal static class CustomLoaderDemo
{
    public static async Task Run()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable not set");

        Console.WriteLine("=== How to Implement Custom SOUL Loaders ===\n");

        // This demo shows templates for implementing custom soul loaders.
        // Users can implement ISoulLoader or IPersistentSoulLoader for any data source.

        Console.WriteLine("1. READ-ONLY LOADER (Load from database, API, etc.)\n");
        var dbTemplate = """
public sealed class DatabaseSoulLoader : ISoulLoader
{
    private readonly string _connectionString;
    
    public DatabaseSoulLoader(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var command = new SqlCommand("SELECT Name, Role, Personality FROM Souls LIMIT 1", connection);
        var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new SoulDocument
        {
            Name = reader.GetString(0),
            Role = reader.GetString(1),
            Personality = reader.GetString(2)
        };
    }
    
    public async Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default)
    {
        return await LoadSoulAsync(cancellationToken);
    }
}

// Usage:
var loader = new DatabaseSoulLoader("Server=localhost");
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul(loader)
    .Build();
""";
        Console.WriteLine(dbTemplate);

        Console.WriteLine("\n\n2. READ-WRITE LOADER (Save personality changes)\n");
        var persistTemplate = """
public sealed class PersistentSoulLoader : IPersistentSoulLoader
{
    public async Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default)
    {
        // Load from your source
        return soul;
    }
    
    public async Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default)
    {
        return await LoadSoulAsync(cancellationToken);
    }
    
    public async Task UpdateSoulAsync(SoulDocument soul, CancellationToken cancellationToken = default)
    {
        // Save to your source
        await SaveToDatabase(soul, cancellationToken);
    }
}

// Update persists changes:
var updatedSoul = agent.Soul with { Personality = "More friendly" };
await agent.UpdateSoulAsync(updatedSoul);  // Triggers UpdateSoulAsync() in your loader
""";
        Console.WriteLine(persistTemplate);

        Console.WriteLine("\n\n3. COMMON DATA SOURCES\n");
        var sources = new[]
        {
            "- PostgreSQL / SQLite / SQL Server (traditional databases)",
            "- MongoDB / Cosmos DB (document databases)",
            "- REST API endpoints (external services)",
            "- AWS S3 / Azure Blob Storage (cloud storage)",
            "- Google Firestore / Firebase (cloud databases)",
            "- Redis / Memcached (cached layers)",
            "- Configuration servers (Spring Cloud Config, Consul)",
            "- GitHub repository files",
            "- Local file system (FileSystemSoulLoader - built-in)",
            "- In-memory storage (testing)"
        };

        foreach (var source in sources)
        {
            Console.WriteLine(source);
        }

        Console.WriteLine("\n\n4. INTEGRATION CHECKLIST\n");
        var checklist = new[]
        {
            "☐ Inherit from ISoulLoader (read-only) or IPersistentSoulLoader (read-write)",
            "☐ Implement LoadSoulAsync() to retrieve SoulDocument",
            "☐ Implement ReloadSoulAsync() for cache invalidation",
            "☐ Add proper exception handling",
            "☐ Support CancellationToken",
            "☐ Consider caching for performance",
            "☐ Write unit tests",
            "☐ Pass to AgentBuilder.WithSoul()"
        };

        foreach (var item in checklist)
        {
            Console.WriteLine(item);
        }

        Console.WriteLine("\n\n5. EXAMPLE: AGENT WITH CUSTOM LOADER\n");
        var exampleUsage = """
// Assuming you've implemented DatabaseSoulLoader
public class MyApplication
{
    public static async Task Main()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION");
        
        var soulLoader = new DatabaseSoulLoader(connectionString);
        
        var agent = new AgentBuilder()
            .WithOpenAi(apiKey)
            .WithSoul(soulLoader)
            .Build();
        
        await agent.InitializeAsync();
        
        // Agent uses personality from database
        var response = await agent.ReplyAsync("What's your approach?");
        Console.WriteLine(response);
        
        // Update personality and persist to database
        var updated = agent.Soul with 
        { 
            Personality = "More helpful, less formal" 
        };
        await agent.UpdateSoulAsync(updated);
    }
}
""";
        Console.WriteLine(exampleUsage);

        Console.WriteLine("\n\n=== Key Interfaces ===\n");
        Console.WriteLine("ISoulLoader (read-only)");
        Console.WriteLine("- Implement for loading from any source");
        Console.WriteLine("- Methods: LoadSoulAsync(), ReloadSoulAsync()\n");

        Console.WriteLine("IPersistentSoulLoader (read-write)");
        Console.WriteLine("- Extends ISoulLoader with persistence");
        Console.WriteLine("- Additional method: UpdateSoulAsync()\n");

        Console.WriteLine("FileSystemSoulLoader (built-in)");
        Console.WriteLine("- Loads SOUL.md from disk");
        Console.WriteLine("- Supports saving changes back to SOUL.md\n");

        Console.WriteLine("\n=== Popular Custom Loader Ideas ===\n");
        var ideas = new[]
        {
            "• DatabaseSoulLoader - PostgreSQL/SQLite/SQL Server",
            "• MongoDbSoulLoader - MongoDB collections",
            "• CosmosDbSoulLoader - Azure Cosmos DB",
            "• S3SoulLoader - AWS S3 buckets",
            "• AzureBlobSoulLoader - Azure Blob Storage",
            "• FirestoreSoulLoader - Google Cloud Firestore",
            "• GitHubSoulLoader - GitHub repository files",
            "• ConfigServerSoulLoader - Spring Cloud Config",
            "• RedisSoulLoader - Redis cache layer",
            "• HttpSoulLoader - REST API endpoints",
            "• InMemorySoulLoader - In-memory (testing)",
            "• DynamicSoulLoader - LLM-generated personalities"
        };

        foreach (var idea in ideas)
        {
            Console.WriteLine(idea);
        }

        Console.WriteLine("\n\n=== Next Steps ===\n");
        Console.WriteLine("1. Review CustomSoulLoaders.cs in the main library for documentation templates");
        Console.WriteLine("2. Check README_CUSTOM.md for detailed implementation guide");
        Console.WriteLine("3. Implement ISoulLoader in your application");
        Console.WriteLine("4. Pass your loader to AgentBuilder.WithSoul()");
        Console.WriteLine("\n=== Learn More ===\n");
        Console.WriteLine("See README_CUSTOM.md in this sample directory for comprehensive guide");
    }
}
