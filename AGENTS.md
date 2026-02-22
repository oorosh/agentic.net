# AGENTS.md - Agentic.NET Development Guide

This file provides essential information for agents working on this codebase.

## Project Overview

Agentic.NET is a .NET library for creating AI assistants with pluggable models, memory, middleware, and tools. The solution contains:
- **Agentic.NET** (main library) - `Agentic.NET.csproj`
- **Agentic.Tests** (unit tests) - `tests/Agentic.Tests/Agentic.Tests.csproj`
- **Samples** - `samples/*/` (BasicChat, MemoryAndMiddleware, ToolCalling, PersonalAssistant)

## Build Commands

```bash
# Restore dependencies
dotnet restore Agentic.NET.sln

# Build the library (Debug)
dotnet build Agentic.NET.csproj

# Build in Release mode
dotnet build Agentic.NET.csproj -c Release

# Pack NuGet package
dotnet pack Agentic.NET.csproj -c Release -o artifacts

# Build a sample
dotnet run --project samples/BasicChat/BasicChat.csproj
```

## Test Commands

```bash
# Run all tests
dotnet test tests/Agentic.Tests/Agentic.Tests.csproj -c Release

# Run a single test by name
dotnet test tests/Agentic.Tests/Agentic.Tests.csproj --filter "FullyQualifiedName~AgentBuilderTests.Build_throws_when_model_provider_missing"

# Run tests with verbose output
dotnet test tests/Agentic.Tests/Agentic.Tests.csproj -c Release -v n
```

## Code Style Guidelines

### Language Features
- **Target Framework**: .NET 10.0 (preview)
- **LangVersion**: preview (enables latest C# features)
- **ImplicitUsings**: enabled
- **Nullable**: enabled

### Formatting Conventions
- Use **file-scoped namespaces**: `namespace Agentic.Core;` (no braces)
- Use **collection expressions**: `var list = new List<T>();` -> `List<T> list = [];`
- Use **target-typed new**: `new AgentBuilder()` instead of `new AgentBuilder()`
- Use **raw string literals** for multi-line strings
- Use **pattern matching**: `if (x is not null)` instead of `if (x != null)`

### Class Design
- Mark classes as **`sealed`** by default unless inheritance is explicitly needed
- Use **primary constructors** where appropriate
- Place `internal` constructors before `public` ones
- Use `private readonly` for fields that are set once

### Naming Conventions
- **Interfaces**: Prefix with `I` (e.g., `IAgentModel`, `ITool`)
- **Types**: PascalCase for all type names
- **Fields**: `_camelCase` (underscore prefix, camelCase)
- **Private fields**: `_fieldName`
- **Constants**: `PascalCase`
- **Parameters**: `camelCase`

### Import Organization
- Use **implicit usings** (enabled in csproj)
- Explicitly import namespaces only when needed to avoid ambiguity
- Group related imports together (Abstractions, Core, Middleware, Providers)

### Error Handling
- Throw **`InvalidOperationException`** for precondition failures
- Throw **`ArgumentNullException`** / **`ArgumentException`** for invalid arguments
- Use meaningful error messages: `"Tool '{name}' is not registered."`
- Avoid empty catch blocks; log or rethrow

### Async Patterns
- Use **`CancellationToken`** as the last parameter in async methods
- Always default to `= default` for CancellationToken parameters
- Return `Task` or `Task<T>`, avoid `void` except for event handlers

### Testing (xUnit)
- Use **`[Fact]`** attribute for test methods
- Use **`Assert.Throws<T>()`** for exception testing
- Use **`await`** for async test methods
- Create helper classes in the same test file as `private sealed class`
- Use descriptive test names: `Method_Scenario_ExpectedResult`

### Project Structure

```
Agentic.NET/
├── Abstractions/     # Interfaces and contracts (IAgentModel, ITool, etc.)
├── Builder/           # AgentBuilder fluent API
├── Core/             # Runtime types (Agent, AgentContext, ChatMessage)
├── Loaders/          # Skill and SOUL document loaders
├── Middleware/       # Middleware contracts and implementations
├── Providers/        # Model provider implementations (OpenAI)
├── Stores/           # Vector store implementations (PgVector, InMemory)
├── samples/          # Usage examples
└── tests/            # Unit tests
```

### Key Interfaces
- **`IAgentModel`**: Underlying LLM/chat model abstraction
- **`IMemoryService`**: Memory storage/retrieval with optional embeddings
- **`IEmbeddingProvider`** (Vector Provider): Generates embeddings/vectors for semantic memory search
- **`IVectorStore`**: Pluggable storage for embeddings (pgvector, in-memory, etc.)
- **`ISkillLoader`**: Loads agent skills from filesystem
- **`ISoulLoader`**: Loads agent identity from SOUL.md
- **`IAssistantMiddleware`**: Pre/post-process conversation
- **`ITool`**: Executable function the model can invoke
- **`IModelProvider`**: Factory for creating model instances

### Configuration
- Solution file: `Agentic.NET.sln`
- Main project: `Agentic.NET.csproj`
- Test project: `tests/Agentic.Tests/Agentic.Tests.csproj`

### Environment Variables (for samples)
- `OPENAI_API_KEY`: Required for OpenAI samples
- `OPENAI_MODEL`: Optional, defaults to `gpt-4o-mini`
- `USE_EMBEDDINGS`: Optional, set to `true` to enable semantic embeddings in memory samples
- `USE_PGVECTOR`: Optional, set to `true` to use PostgreSQL pgvector instead of in-memory
- `PGVECTOR_CONNECTION_STRING`: PostgreSQL connection string (required when USE_PGVECTOR=true)

## Embeddings and Semantic Memory

Agentic.NET supports semantic memory through embeddings for improved context relevance. The architecture uses two key components:

### Components

| Component | Purpose | Examples |
|-----------|---------|----------|
| **IEmbeddingProvider** (Vector Provider) | Generates embeddings from text | OpenAiEmbeddingProvider |
| **IVectorStore** | Stores and searches embeddings | PgVectorStore, InMemoryVectorStore |

### Adding Embeddings
1. Add an **embedding provider**: `IEmbeddingProvider` generates vectors from text
2. Configure in `AgentBuilder`: `.WithEmbeddingProvider(provider)`
3. Optionally add a **vector store**: `.WithVectorStore(vectorStore)` for production-scale search
4. Embeddings are automatically generated and stored for each message
5. Retrieval uses cosine similarity (or HNSW index with pgvector) for semantic matching

### Vector Storage Options

#### In-Memory (Development)
```csharp
var embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
await embeddingProvider.InitializeAsync();

var vectorStore = new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions);

var assistant = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithEmbeddingProvider(embeddingProvider)
    .WithVectorStore(vectorStore)
    .WithMemory(new SqliteMemoryService())
    .Build();
```

#### PgVector (Production)
Requires PostgreSQL with pgvector extension installed:
```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

```csharp
var embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
await embeddingProvider.InitializeAsync();

var vectorStore = new PgVectorStore(
    "Host=localhost;Database=memory",
    dimensions: embeddingProvider.Dimensions
);

var assistant = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithEmbeddingProvider(embeddingProvider)
    .WithVectorStore(vectorStore)
    .WithMemory(new SqliteMemoryService(vectorStore))
    .Build();
```

#### Adding Custom Vector Stores
Implement `IVectorStore` interface to add support for other vector databases:
- Azure AI Search
- Qudrant
- Pinecone
- Milvus
- Weaviate

### Benefits
- Better recall of semantically similar conversations
- Reduced false positives from keyword matching
- Pluggable providers for different embedding models
- Pluggable vector storage for production scalability

## Common Tasks

### Adding a new model provider
1. Create provider class in `Providers/` directory
2. Implement `IModelProvider` interface
3. Add `WithXxx()` method to `AgentBuilder`
4. Add tests for the new provider

### Adding a new tool
1. Implement `ITool` interface with `Name`, `Description`, and `InvokeAsync`
2. Register via `AgentBuilder.WithTool()` or `WithTools()`

### Running a specific sample
```bash
dotnet run --project samples/BasicChat/BasicChat.csproj
dotnet run --project samples/ToolCalling/ToolCalling.csproj
dotnet run --project samples/MemoryAndMiddleware/MemoryAndMiddleware.csproj
dotnet run --project samples/PersonalAssistant/PersonalAssistant.csproj
```

## Agent Skills

Agentic.NET supports [Agent Skills](https://agentskills.io) - an open standard for giving agents new capabilities.

### Overview
Skills are folders containing a `SKILL.md` file with YAML frontmatter and markdown instructions. They can include optional `scripts/`, `references/`, and `assets/` directories.

### SKILL.md Format
```yaml
---
name: pdf-processing
description: Extract text and tables from PDF files, fill forms, merge documents.
license: MIT
---
# Instructions
Step-by-step instructions for the agent...
```

### Loading Skills
```csharp
// From skills directory
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSkills("./skills")  // Load all skills from directory
    .Build();

// Or use WithAgent to load both skills and SOUL.md from same folder
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithAgent("./my-agent")  // loads skills from ./my-agent/*/ and SOUL.md from ./my-agent/
    .Build();

await agent.InitializeAsync();
// agent.Skills contains loaded skill metadata
```

### Generating Skill Prompt XML
```csharp
var xml = FileSystemSkillLoader.ToPromptXml(agent.Skills);
// Generates <available_skills> XML for system prompt
```

### Adding Custom Skill Loaders
Implement `ISkillLoader` interface to load skills from other sources (embedded resources, databases, etc.)

## SOUL.md - Agent Identity

Agentic.NET supports [SOUL.md](https://soul.md) - a format for defining agent identity, personality, and behavior.

### Overview
SOUL.md is a single markdown file that defines who the agent is - its role, personality, rules, tools, and handoffs.

### SOUL.md Format
```markdown
# AgentName

## Role
You are a content marketing specialist for a SaaS company.

## Personality
- Tone: Professional but approachable
- Style: Clear, concise, scannable

## Rules
- ALWAYS respond in English
- NEVER use clickbait

## Tools
- Use Browser to research topics
- Use WordPress API to publish

## Handoffs
- Ask @SEOAgent for keyword research
- Hand off to @Publisher when ready
```

### Loading SOUL.md
```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithSoul("./SOUL.md")  // or directory (looks for SOUL.md)
    .Build();

await agent.InitializeAsync();
// agent.Soul contains parsed identity
```

### Generating System Prompt
```csharp
var systemPrompt = FileSystemSoulLoader.ToSystemPrompt(agent.Soul);
// Generates a system prompt from SOUL.md sections
```

### Adding Custom Soul Loaders
Implement `ISoulLoader` interface to load SOUL.md from other sources

## Complete Example

```csharp
var embeddingProvider = new OpenAiEmbeddingProvider(apiKey);
await embeddingProvider.InitializeAsync();

var vectorStore = new PgVectorStore(connectionString, dimensions: embeddingProvider.Dimensions);

var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMemory(new SqliteMemoryService(vectorStore))
    .WithEmbeddingProvider(embeddingProvider)
    .WithVectorStore(vectorStore)
    .WithAgent("./my-agent")     // Load skills and SOUL.md from folder
    .WithTool(new MyCustomTool())
    .Build();

await agent.InitializeAsync();

// Use skills
if (agent.Skills is { } skills)
{
    var skillXml = FileSystemSkillLoader.ToPromptXml(skills);
    // Add to system prompt
}

// Use soul
if (agent.Soul is { } soul)
{
    var systemPrompt = FileSystemSoulLoader.ToSystemPrompt(soul);
    // Add to system prompt
}
```
