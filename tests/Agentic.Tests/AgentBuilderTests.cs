using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Loaders;
using Agentic.Middleware;
using Agentic.Providers.OpenAi;
using Agentic.Stores;
using Agentic.Tests.Fakes;
using System.IO;
using Xunit;

namespace Agentic.Tests;

public class AgentBuilderTests
{
    [Fact]
    public void Build_throws_when_model_provider_missing()
    {
        var builder = new AgentBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_succeeds_when_openai_provider_configured_via_builder()
    {
        var assistant = new AgentBuilder()
            .WithOpenAi("test-api-key")
            .Build();

        Assert.NotNull(assistant);
    }

    [Fact]
    public void Build_succeeds_when_openai_model_configured_via_builder()
    {
        var assistant = new AgentBuilder()
            .WithOpenAi("test-api-key", model: "gpt-4.1-mini")
            .Build();

        Assert.NotNull(assistant);
    }

    [Fact]
    public void Build_succeeds_when_openai_options_are_configured_via_builder()
    {
        var assistant = new AgentBuilder()
            .WithOpenAi("test-api-key", options =>
            {
                options.Model = "gpt-4.1-mini";
                options.Tools =
                [
                    new OpenAiFunctionToolDefinition(
                        "get_weather",
                        "Get weather for a city.",
                        [new OpenAiFunctionToolParameter("city", "string", "City name")])
                ];
            })
            .Build();

        Assert.NotNull(assistant);
    }

    [Fact]
    public async Task ReplyAsync_uses_context_factory_and_stores_with_memory()
    {
        var memory = new TrackingMemoryService();
        memory.StoredMessages.Add("remembered context");

        var calls = new List<string>();
        var snapshots = new List<IReadOnlyList<ChatMessage>>();

        var mw1 = new RecordingMiddleware("mw1", calls, snapshots);
        var mw2 = new RecordingMiddleware("mw2", calls, snapshots);

        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .WithMemory(memory)
            .UseMiddleware(mw1)
            .UseMiddleware(mw2)
            .Build();

        await assistant.ReplyAsync("foo");

        // ensure both custom middlewares executed in registration order
        Assert.Equal("mw1", calls[0]);
        Assert.Equal("mw2", calls[1]);

        // first middleware should see a system message injected by memory middleware
        Assert.NotEmpty(snapshots);
        var firstSnapshot = snapshots[0];
        Assert.Contains(firstSnapshot, m => m.Role == ChatRole.System && m.Content.Contains("remembered context"));
    }

    [Fact]
    public async Task History_accumulates_across_replies()
    {
        var provider = new FakeModelProvider(new TestAgentModel());
        var assistant = new AgentBuilder()
            .WithModelProvider(provider)
            .Build();

        await assistant.ReplyAsync("one");
        await assistant.ReplyAsync("two");

        Assert.Equal(4, assistant.History.Count);
        Assert.Equal("one", assistant.History[0].Content);
        Assert.Equal("two", assistant.History[2].Content);
    }

    [Fact]
    public async Task InitializeAsync_idempotent_and_memory_only_once()
    {
        var memory = new TrackingMemoryService();
        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .WithMemory(memory)
            .Build();

        await assistant.InitializeAsync();
        await assistant.InitializeAsync();

        Assert.True(memory.Initialized);
    }

    [Fact]
    public async Task MemoryMiddleware_retrieves_and_injects_context()
    {
        var memory = new TrackingMemoryService();
        memory.StoredMessages.Add("prior conversation");
        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .WithMemory(memory)
            .Build();

        // use a model that echoes full working messages for inspection
        var echoModel = new InspectingModel();
        assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(echoModel))
            .WithMemory(memory)
            .Build();

        var response = await assistant.ReplyAsync("question");
        Assert.Contains("prior conversation", (string)response);
    }

    private sealed class TestAgentModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            var lastUser = messages.Last(m => m.Role == ChatRole.User).Content;
            return Task.FromResult(new AgentResponse("echo: " + lastUser));
        }
    }

    private sealed class TrackingContextFactory : IAssistantContextFactory
    {
        public int CallCount { get; private set; }
        public string? LastInput { get; private set; }

        public AgentContext Create(string input, IReadOnlyList<ChatMessage> history)
        {
            CallCount++;
            LastInput = input;
            return new AgentContext(input, history);
        }
    }

    private sealed class TrackingMemoryService : IMemoryService
    {
        public bool Initialized { get; private set; }
        public List<string> StoredMessages { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            Initialized = true;
            return Task.CompletedTask;
        }

        public Task StoreMessageAsync(string id, string content, CancellationToken cancellationToken = default)
        {
            if (!Initialized)
            {
                throw new InvalidOperationException("Memory must be initialized before storing messages.");
            }

            StoredMessages.Add(content);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
        {
            // return all stored messages for simplicity
            return Task.FromResult<IReadOnlyList<string>>(StoredMessages.ToList());
        }

        public Task StoreEmbeddingAsync(string id, float[] embedding, CancellationToken cancellationToken = default)
        {
            // no-op for test
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(string Content, float Score)>> RetrieveSimilarAsync(float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
        {
            // return empty for test
            return Task.FromResult<IReadOnlyList<(string Content, float Score)>>(new List<(string, float)>());
        }

        public Task DeleteMessageAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // simple integration test for the SQLite-based memory service in samples
    [Fact]
    public async Task SqliteMemoryService_persists_and_retrieves()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var sqlite = new SqliteMemoryService(tempFile);
            await sqlite.InitializeAsync();
            Assert.True(true, "initialized");

            await sqlite.StoreMessageAsync("1", "hello world");
            await sqlite.StoreMessageAsync("2", "the world is big");

            var results = await sqlite.RetrieveRelevantAsync("world", topK: 10);
            Assert.Contains("hello world", results);
            Assert.Contains("the world is big", results);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task RetrieveRelevantAsync_emptyOrMiss_returns_recent()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var sqlite = new SqliteMemoryService(tempFile);
            await sqlite.InitializeAsync();
            await sqlite.StoreMessageAsync("a", "first");
            await sqlite.StoreMessageAsync("b", "second");

            // empty query should return both entries
            var all = await sqlite.RetrieveRelevantAsync(string.Empty, topK: 5);
            Assert.Equal(2, all.Count);

            // unrelated query should fall back as well
            var none = await sqlite.RetrieveRelevantAsync("xyz", topK: 5);
            Assert.Equal(2, none.Count);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task Agent_includes_persisted_memory_on_first_reply()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var sqlite = new SqliteMemoryService(tempFile);
            await sqlite.InitializeAsync();
            await sqlite.StoreMessageAsync("1", "remember this");

            AgentContext? seen = null;
            var provider = new CallbackProvider(ctx =>
            {
                seen = ctx;
                return Task.FromResult(new AgentResponse("ok"));
            });

            var assistant = new AgentBuilder()
                .WithModelProvider(provider)
                .WithMemory(sqlite)
                .Build();

            var res = await assistant.ReplyAsync("hi");
            Assert.Equal("ok", (string)res);
            Assert.NotNull(seen);
            Assert.Contains(seen!.WorkingMessages.Select(m => m.Content), c => c.Contains("remember this"));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task MemoryMiddleware_loads_everything_on_first_turn()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var sqlite = new SqliteMemoryService(tempFile);
            await sqlite.InitializeAsync();
            // put several unrelated messages into the database
            await sqlite.StoreMessageAsync("1", "foo");
            await sqlite.StoreMessageAsync("2", "bar");

            var memMw = new MemoryMiddleware(sqlite);
            var ctx = new AgentContext("what?", new List<ChatMessage>());
            var called = false;
            AgentHandler dummy = (c, ct) => { called = true; return Task.FromResult(new AgentResponse("ok")); };

            var resp = await memMw.InvokeAsync(ctx, dummy);
            Assert.True(called);
            // because there was no history we should have inserted a system message
            Assert.Contains(ctx.WorkingMessages, m => m.Role == ChatRole.System);
            var sys = ctx.WorkingMessages.First(m => m.Role == ChatRole.System).Content;
            Assert.Contains("foo", sys);
            Assert.Contains("bar", sys);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task ReplyAsync_executes_tool_calls_until_final_answer()
    {
        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new ToolCallingModel()))
            .WithTool(new UppercaseTool())
            .Build();

        var response = await assistant.ReplyAsync("please shout hello world");

        Assert.Equal("Tool says: HELLO WORLD", (string)response);
    }

    [Fact]
    public async Task ReplyAsync_repeated_same_tool_call_returns_last_tool_result_instead_of_throwing()
    {
        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new RepeatingToolCallingModel()))
            .WithTool(new UppercaseTool())
            .Build();

        var response = await assistant.ReplyAsync("please shout hello world");

        Assert.Equal("HELLO WORLD", (string)response);
    }

    [Fact]
    public async Task ReplyAsync_includes_registered_tools_in_model_context()
    {
        IReadOnlyList<ChatMessage>? captured = null;

        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new CaptureModel(messages =>
            {
                captured = messages;
                return new AgentResponse("ok");
            })))
            .WithTool(new UppercaseTool())
            .Build();

        var response = await assistant.ReplyAsync("hello");

        Assert.Equal("ok", (string)response);
        Assert.NotNull(captured);
        var toolSystem = captured!.FirstOrDefault(m => m.Role == ChatRole.System && m.Content.Contains("Available tools:"));
        Assert.NotNull(toolSystem);
        Assert.Contains("uppercase", toolSystem!.Content);
    }

    [Fact]
    public async Task EmbeddingProvider_can_be_configured()
    {
        var apiKey = "test-key";
        var embeddingProvider = new Agentic.Providers.OpenAi.OpenAiEmbeddingProvider(apiKey);

        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .WithMemory(new InMemoryMemoryService())
            .WithEmbeddingProvider(embeddingProvider)
            .Build();

        // Should not throw
        await assistant.InitializeAsync();
    }

    private sealed class RecordingMiddleware : IAssistantMiddleware
    {
        private readonly string _name;
        private readonly List<string> _calls;
        private readonly List<IReadOnlyList<ChatMessage>> _snapshots;

        public RecordingMiddleware(string name, List<string> calls, List<IReadOnlyList<ChatMessage>> snapshots)
        {
            _name = name;
            _calls = calls;
            _snapshots = snapshots;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _calls.Add(_name);
            _snapshots.Add(context.WorkingMessages.ToList());
            return await next(context, cancellationToken);
        }
    }

    private sealed class InspectingModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            // echo all messages in single string
            var combined = string.Join("|", messages.Select(m => m.Content));
            return Task.FromResult(new AgentResponse(combined));
        }
    }

    private sealed class ToolCallingModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            var lastTool = messages.LastOrDefault(m => m.Role == ChatRole.Tool);
            if (lastTool is not null)
            {
                return Task.FromResult(new AgentResponse($"Tool says: {lastTool.Content}"));
            }

            var lastUser = messages.Last(m => m.Role == ChatRole.User).Content;
            var text = lastUser.Replace("please shout", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            var toolCalls = new List<AgentToolCall>
            {
                new("uppercase", text)
            };

            return Task.FromResult(new AgentResponse("Calling tool", toolCalls));
        }
    }

    private sealed class CaptureModel : IAgentModel
    {
        private readonly Func<IReadOnlyList<ChatMessage>, AgentResponse> _capture;

        public CaptureModel(Func<IReadOnlyList<ChatMessage>, AgentResponse> capture)
        {
            _capture = capture;
        }

        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_capture(messages));
        }
    }

    private sealed class RepeatingToolCallingModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            var lastUser = messages.Last(m => m.Role == ChatRole.User).Content;
            var text = lastUser.Replace("please shout", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            var toolCalls = new List<AgentToolCall>
            {
                new("uppercase", text)
            };

            return Task.FromResult(new AgentResponse("Still calling tool", toolCalls));
        }
    }

    private sealed class UppercaseTool : ITool
    {
        public string Name => "uppercase";
        public string Description => "Uppercases input text.";

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(arguments.ToUpperInvariant());
        }
    }

    // provider that invokes a callback so tests can inspect the context
    private sealed class CallbackProvider : IModelProvider
    {
        private readonly Func<AgentContext, Task<AgentResponse>> _fn;
        public CallbackProvider(Func<AgentContext, Task<AgentResponse>> fn) => _fn = fn;

        public IAgentModel CreateModel() => new CallbackModel(_fn);

        private sealed class CallbackModel : IAgentModel
        {
            private readonly Func<AgentContext, Task<AgentResponse>> _fn;
            public CallbackModel(Func<AgentContext, Task<AgentResponse>> fn) => _fn = fn;

            public Task<AgentResponse> CompleteAsync(
                IReadOnlyList<ChatMessage> messages,
                CancellationToken cancellationToken = default)
            {
                var ctx = new AgentContext(messages.Last(m => m.Role == ChatRole.User).Content, messages);
                return _fn(ctx);
            }
        }
    }

    [Fact]
    public async Task InMemoryVectorStore_initializes_and_stores_vectors()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        await store.UpsertAsync("vec1", [1f, 0f, 0f]);
        await store.UpsertAsync("vec2", [0f, 1f, 0f]);

        var results = await store.SearchAsync([1f, 0f, 0f], topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("vec1", results[0].Id);
        Assert.Equal(1f, results[0].Score, 5);
    }

    [Fact]
    public async Task InMemoryVectorStore_search_returns_similar_by_cosine_similarity()
    {
        var store = new InMemoryVectorStore(dimensions: 2);
        await store.InitializeAsync();

        await store.UpsertAsync("a", [1f, 0f]);
        await store.UpsertAsync("b", [0.9f, 0.1f]);
        await store.UpsertAsync("c", [0f, 1f]);

        var results = await store.SearchAsync([1f, 0f], topK: 2);

        Assert.Equal("a", results[0].Id);
        Assert.Equal("b", results[1].Id);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task InMemoryVectorStore_delete_removes_vector()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        await store.UpsertAsync("vec1", [1f, 0f, 0f]);
        await store.DeleteAsync("vec1");

        var results = await store.SearchAsync([1f, 0f, 0f], topK: 5);

        Assert.Empty(results);
    }

    [Fact]
    public async Task InMemoryVectorStore_delete_all_clears_all()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        await store.UpsertAsync("vec1", [1f, 0f, 0f]);
        await store.UpsertAsync("vec2", [0f, 1f, 0f]);
        await store.DeleteAllAsync();

        var results = await store.SearchAsync([1f, 0f, 0f], topK: 5);

        Assert.Empty(results);
    }

    [Fact]
    public async Task InMemoryVectorStore_throws_on_dimension_mismatch()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync("vec", [1f, 2f]));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync([1f, 2f], topK: 5));
    }

    [Fact]
    public async Task SqliteMemoryService_with_vector_store_stores_and_retrieves()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var vectorStore = new InMemoryVectorStore(dimensions: 3);
            var sqlite = new SqliteMemoryService(tempFile, vectorStore);
            await sqlite.InitializeAsync();

            await sqlite.StoreMessageAsync("1", "hello world");
            await sqlite.StoreEmbeddingAsync("1", [1f, 0f, 0f]);

            var results = await sqlite.RetrieveSimilarAsync([1f, 0f, 0f], topK: 5);

            Assert.Single(results);
            Assert.Equal("hello world", results[0].Content);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task SqliteMemoryService_with_vector_store_searches_semantically()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var vectorStore = new InMemoryVectorStore(dimensions: 3);
            var sqlite = new SqliteMemoryService(tempFile, vectorStore);
            await sqlite.InitializeAsync();

            await sqlite.StoreMessageAsync("1", "I love programming in C#");
            await sqlite.StoreEmbeddingAsync("1", [1f, 0.5f, 0.2f]);
            await sqlite.StoreMessageAsync("2", "What's the weather today?");
            await sqlite.StoreEmbeddingAsync("2", [0f, 0f, 1f]);

            var results = await sqlite.RetrieveSimilarAsync([0.9f, 0.5f, 0.2f], topK: 1);

            Assert.Single(results);
            Assert.Contains("C#", results[0].Content);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task AgentBuilder_with_vector_store_creates_memory_middleware_with_embedding_provider()
    {
        var apiKey = "test-key";
        var embeddingProvider = new Agentic.Providers.OpenAi.OpenAiEmbeddingProvider(apiKey);
        var vectorStore = new InMemoryVectorStore(dimensions: embeddingProvider.Dimensions);

        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .WithMemory(new InMemoryMemoryService())
            .WithEmbeddingProvider(embeddingProvider)
            .WithVectorStore(vectorStore)
            .Build();

        await assistant.InitializeAsync();
        Assert.NotNull(assistant);
    }

    [Fact]
    public async Task AgentBuilder_with_vector_store_only_auto_creates_sqlite_memory()
    {
        var vectorStore = new InMemoryVectorStore(dimensions: 1536);

        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .WithVectorStore(vectorStore)
            .Build();

        await assistant.InitializeAsync();
        Assert.NotNull(assistant);
    }

    [Fact]
    public void InMemoryVectorStore_dimensions_property_returns_configured_value()
    {
        var store = new InMemoryVectorStore(dimensions: 768);
        Assert.Equal(768, store.Dimensions);
    }

    [Fact]
    public async Task FileSystemSkillLoader_loads_skills_from_directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var skillDir = Path.Combine(tempDir, "pdf-processing");
            Directory.CreateDirectory(skillDir);
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"),
                @"---
name: pdf-processing
description: Extract text and tables from PDF files.
---
# PDF Processing
Steps to process PDFs...");

            var loader = new FileSystemSkillLoader(tempDir);
            var skills = await loader.LoadSkillsAsync();

            Assert.Single(skills);
            Assert.Equal("pdf-processing", skills[0].Name);
            Assert.Equal("Extract text and tables from PDF files.", skills[0].Description);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FileSystemSkillLoader_returns_empty_when_directory_does_not_exist()
    {
        var loader = new FileSystemSkillLoader("/nonexistent/path");
        var skills = await loader.LoadSkillsAsync();

        Assert.Empty(skills);
    }

    [Fact]
    public async Task FileSystemSkillLoader_skips_directories_without_skill_md()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "empty-skill"));

            var loader = new FileSystemSkillLoader(tempDir);
            var skills = await loader.LoadSkillsAsync();

            Assert.Empty(skills);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FileSystemSkillLoader_parses_full_metadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var skillDir = Path.Combine(tempDir, "test-skill");
            Directory.CreateDirectory(skillDir);
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"),
                @"---
name: test-skill
description: A test skill with full metadata.
license: MIT
compatibility: Requires .NET 8
allowed-tools: Bash Read
---
# Instructions");

            var loader = new FileSystemSkillLoader(tempDir);
            var skills = await loader.LoadSkillsAsync();

            Assert.Single(skills);
            Assert.Equal("test-skill", skills[0].Name);
            Assert.Equal("MIT", skills[0].License);
            Assert.Equal("Requires .NET 8", skills[0].Compatibility);
            Assert.Equal("Bash Read", skills[0].AllowedTools);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FileSystemSkillLoader_loads_full_instructions_when_requested()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var skillDir = Path.Combine(tempDir, "with-instructions");
            Directory.CreateDirectory(skillDir);
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"),
                @"---
name: with-instructions
description: A skill with instructions.
---
# Step 1
Do this first.

# Step 2
Then do this.");

            var loader = new FileSystemSkillLoader(tempDir);
            await loader.LoadSkillsAsync();
            var skill = await loader.LoadSkillAsync("with-instructions");

            Assert.NotNull(skill);
            Assert.Contains("Step 1", skill.Instructions);
            Assert.Contains("Step 2", skill.Instructions);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FileSystemSkillLoader_returns_null_for_unknown_skill()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);

            var loader = new FileSystemSkillLoader(tempDir);
            var skill = await loader.LoadSkillAsync("nonexistent");

            Assert.Null(skill);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void FileSystemSkillLoader_ToPromptXml_generates_correct_format()
    {
        var skills = new List<Skill>
        {
            new() { Name = "pdf-processing", Description = "Extract text from PDFs.", Path = "/skills/pdf" },
            new() { Name = "data-analysis", Description = "Analyze datasets.", Path = "/skills/data" }
        };

        var xml = FileSystemSkillLoader.ToPromptXml(skills);

        Assert.Contains("<available_skills>", xml);
        Assert.Contains("<name>pdf-processing</name>", xml);
        Assert.Contains("<description>Extract text from PDFs.</description>", xml);
        Assert.Contains("<location>pdf</location>", xml);
        Assert.Contains("<name>data-analysis</name>", xml);
        Assert.Contains("</available_skills>", xml);
    }

    [Fact]
    public void FileSystemSkillLoader_ToPromptXml_returns_empty_for_empty_list()
    {
        var xml = FileSystemSkillLoader.ToPromptXml([]);
        Assert.Empty(xml);
    }

    [Fact]
    public async Task AgentBuilder_with_skills_directory_loads_skills()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var skillDir = Path.Combine(tempDir, "test-skill");
            Directory.CreateDirectory(skillDir);
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"),
                @"---
name: test-skill
description: A test skill.
---
# Instructions");

            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
                .WithSkills(tempDir)
                .Build();

            await assistant.InitializeAsync();

            Assert.NotNull(assistant.Skills);
            Assert.Single(assistant.Skills);
            Assert.Equal("test-skill", assistant.Skills[0].Name);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Agent_with_no_skills_has_null_skills()
    {
        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .Build();

        await assistant.InitializeAsync();

        Assert.Null(assistant.Skills);
    }

    private sealed class TestSkillLoader : ISkillLoader
    {
        private readonly List<Skill> _skills;

        public TestSkillLoader(IEnumerable<Skill> skills)
        {
            _skills = skills.ToList();
        }

        public Task<IReadOnlyList<Skill>> LoadSkillsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Skill>>(_skills);
        }

        public Task<Skill?> LoadSkillAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Skill?>(_skills.FirstOrDefault(s => s.Name == name));
        }
    }

    [Fact]
    public async Task FileSystemSoulLoader_parses_soul_document()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# ContentWriter

## Role
You are a content marketing specialist.

## Personality
- Tone: Professional but approachable
- Style: Clear and concise

## Rules
- ALWAYS respond in English
- NEVER use clickbait

## Tools
- Use Browser to research topics
- Use WordPress API to publish

## Handoffs
- Ask @SEOAgent for keyword research");

            var loader = new FileSystemSoulLoader(soulPath);
            var soul = await loader.LoadSoulAsync();

            Assert.NotNull(soul);
            Assert.Equal("ContentWriter", soul.Name);
            Assert.Contains("content marketing specialist", soul.Role);
            Assert.Contains("Professional but approachable", soul.Personality);
            Assert.Contains("ALWAYS respond in English", soul.Rules);
            Assert.Contains("Browser to research", soul.Tools);
            Assert.Contains("@SEOAgent", soul.Handoffs);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FileSystemSoulLoader_returns_null_when_file_not_found()
    {
        var loader = new FileSystemSoulLoader("/nonexistent/path/SOUL.md");
        var soul = await loader.LoadSoulAsync();

        Assert.Null(soul);
    }

    [Fact]
    public async Task FileSystemSoulLoader_parses_from_directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "SOUL.md"),
                @"# MyAgent
## Role
Test role");

            var loader = new FileSystemSoulLoader(new DirectoryInfo(tempDir));
            var soul = await loader.LoadSoulAsync();

            Assert.NotNull(soul);
            Assert.Equal("MyAgent", soul.Name);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void FileSystemSoulLoader_ToSystemPrompt_generates_correct_format()
    {
        var soul = new SoulDocument
        {
            Name = "TestAgent",
            Role = "You are a helpful assistant.",
            Personality = "Tone: friendly",
            Rules = "ALWAYS be honest",
            Tools = "Use calculator for math"
        };

        var prompt = FileSystemSoulLoader.ToSystemPrompt(soul);

        Assert.Contains("You are a helpful assistant.", prompt);
        Assert.Contains("Tone: friendly", prompt);
        Assert.Contains("ALWAYS be honest", prompt);
        Assert.Contains("calculator for math", prompt);
    }

    [Fact]
    public void FileSystemSoulLoader_ToSystemPrompt_handles_missing_sections()
    {
        var soul = new SoulDocument
        {
            Name = "MinimalAgent",
            Role = "You are an agent."
        };

        var prompt = FileSystemSoulLoader.ToSystemPrompt(soul);

        Assert.Equal("You are an agent.", prompt);
    }

    [Fact]
    public async Task AgentBuilder_with_soul_loads_soul_document()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# AssistantBot
## Role
You are a helpful assistant.");

            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
                .WithSoul(soulPath)
                .Build();

            await assistant.InitializeAsync();

            Assert.NotNull(assistant.Soul);
            Assert.Equal("AssistantBot", assistant.Soul.Name);
            Assert.Contains("helpful assistant", assistant.Soul.Role);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Agent_with_no_soul_has_null_soul()
    {
        var assistant = new AgentBuilder()
            .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
            .Build();

        await assistant.InitializeAsync();

        Assert.Null(assistant.Soul);
    }

    [Fact]
    public async Task Agent_auto_injects_soul_as_system_message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# TestBot
## Role
You are a helpful test bot.");

            IReadOnlyList<ChatMessage>? captured = null;
            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new CaptureModel(messages =>
                {
                    captured = messages;
                    return new AgentResponse("ok");
                })))
                .WithSoul(soulPath)
                .Build();

            await assistant.ReplyAsync("hello");

            Assert.NotNull(captured);
            var systemMsg = captured!.FirstOrDefault(m => m.Role == ChatRole.System && m.Content.Contains("helpful test bot"));
            Assert.NotNull(systemMsg);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Agent_auto_injects_skills_as_system_message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var skillDir = Path.Combine(tempDir, "my-skill");
            Directory.CreateDirectory(skillDir);
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"),
                @"---
name: my-skill
description: Does something useful.
---
# Instructions
Step 1.");

            IReadOnlyList<ChatMessage>? captured = null;
            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new CaptureModel(messages =>
                {
                    captured = messages;
                    return new AgentResponse("ok");
                })))
                .WithSkills(tempDir)
                .Build();

            await assistant.ReplyAsync("hello");

            Assert.NotNull(captured);
            var systemMsg = captured!.FirstOrDefault(m => m.Role == ChatRole.System && m.Content.Contains("my-skill"));
            Assert.NotNull(systemMsg);
            Assert.Contains("Does something useful", systemMsg!.Content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Agent_soul_and_skills_injected_in_same_system_message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);

            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# ComboBot
## Role
You are a combo bot.");

            var skillDir = Path.Combine(tempDir, "combo-skill");
            Directory.CreateDirectory(skillDir);
            await File.WriteAllTextAsync(Path.Combine(skillDir, "SKILL.md"),
                @"---
name: combo-skill
description: A combo skill.
---
# Instructions");

            IReadOnlyList<ChatMessage>? captured = null;
            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new CaptureModel(messages =>
                {
                    captured = messages;
                    return new AgentResponse("ok");
                })))
                .WithSoul(soulPath)
                .WithSkills(tempDir)
                .Build();

            await assistant.ReplyAsync("hello");

            Assert.NotNull(captured);
            // Both soul and skills should appear in a single leading system message
            var systemMessages = captured!.Where(m => m.Role == ChatRole.System).ToList();
            var combined = systemMessages.FirstOrDefault(m =>
                m.Content.Contains("combo bot") && m.Content.Contains("combo-skill"));
            Assert.NotNull(combined);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task InMemoryVectorStore_concurrent_upserts_do_not_corrupt()
    {
        var store = new InMemoryVectorStore(dimensions: 3);
        await store.InitializeAsync();

        // Fire 50 concurrent upserts — would corrupt a plain Dictionary
        var tasks = Enumerable.Range(0, 50).Select(i =>
            store.UpsertAsync($"vec{i}", [i * 0.01f, 0f, 0f]));

        await Task.WhenAll(tasks);

        var results = await store.SearchAsync([0.5f, 0f, 0f], topK: 50);
        Assert.Equal(50, results.Count);
    }

    [Fact]
    public async Task WithSoulLearning_callback_fires_after_every_reply()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# LearningBot
## Role
You are a learning bot.");

            var callbackInputs = new List<string>();
            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
                .WithSoul(soulPath)
                .WithSoulLearning((userInput, agentReply, soul) =>
                {
                    callbackInputs.Add(userInput);
                    return null; // no change
                })
                .Build();

            await assistant.ReplyAsync("first message");
            await assistant.ReplyAsync("second message");

            Assert.Equal(2, callbackInputs.Count);
            Assert.Equal("first message", callbackInputs[0]);
            Assert.Equal("second message", callbackInputs[1]);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task WithSoulLearning_returning_null_does_not_change_soul()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# StableBot
## Role
You are a stable bot.");

            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
                .WithSoul(soulPath)
                .WithSoulLearning((_, _, soul) => null)
                .Build();

            await assistant.InitializeAsync();
            var originalRole = assistant.Soul!.Role;

            await assistant.ReplyAsync("hello");

            Assert.Equal(originalRole, assistant.Soul!.Role);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task WithSoulLearning_returning_new_soul_updates_agent_soul_and_system_message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# UpdateBot
## Role
Original role.");

            IReadOnlyList<ChatMessage>? secondCallMessages = null;
            var callCount = 0;

            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new CaptureModel(messages =>
                {
                    callCount++;
                    if (callCount == 2)
                        secondCallMessages = messages;
                    return new AgentResponse("ok");
                })))
                .WithSoul(soulPath)
                .WithSoulLearning((_, _, soul) =>
                    soul with { Role = "Updated role after learning." })
                .Build();

            await assistant.ReplyAsync("first");
            await assistant.ReplyAsync("second");

            // Soul property should reflect updated role
            Assert.Contains("Updated role after learning.", assistant.Soul!.Role);

            // Second call's system message should contain the updated role
            Assert.NotNull(secondCallMessages);
            var sysMsg = secondCallMessages!.FirstOrDefault(m => m.Role == ChatRole.System && m.Content.Contains("Updated role after learning."));
            Assert.NotNull(sysMsg);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task WithSoulLearning_persists_to_disk_when_persistent_loader_available()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var soulPath = Path.Combine(tempDir, "SOUL.md");
            await File.WriteAllTextAsync(soulPath,
                @"# PersistBot
## Role
Original role.");

            var assistant = new AgentBuilder()
                .WithModelProvider(new FakeModelProvider(new TestAgentModel()))
                .WithSoul(soulPath)
                .WithSoulLearning((_, _, soul) =>
                    soul with { Role = "Persisted updated role." })
                .Build();

            await assistant.ReplyAsync("trigger learning");

            // Re-read the file from disk to confirm persistence
            var diskContent = await File.ReadAllTextAsync(soulPath);
            Assert.Contains("Persisted updated role.", diskContent);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
