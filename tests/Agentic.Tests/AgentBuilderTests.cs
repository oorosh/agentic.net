using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Middleware;
using Agentic.Providers.OpenAi;
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
        var builder = new AgentBuilder()
            .WithOpenAi("test-api-key", tools:
            [
                new OpenAiFunctionToolDefinition(
                    "get_weather",
                    "Get weather for a city.",
                    [new OpenAiFunctionToolParameter("city", "string", "City name")])
            ]);

        var assistant = builder.Build();

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
        var provider = new TestModelProvider(new TestAgentModel());
        var contextFactory = new TrackingContextFactory();
        var memory = new TrackingMemoryService();

        var assistant = new AgentBuilder()
            .WithModelProvider(provider)
            .WithMemory(memory)
            .WithContextFactory(contextFactory)
            .Build();

        var response = await assistant.ReplyAsync("hello");

        Assert.Equal("echo: hello", response);
        Assert.Equal(1, contextFactory.CallCount);
        Assert.Equal("hello", contextFactory.LastInput);
        Assert.True(memory.Initialized);
        Assert.Equal(2, memory.StoredMessages.Count);
        Assert.Equal("hello", memory.StoredMessages[0]);
        Assert.Equal("echo: hello", memory.StoredMessages[1]);
    }

    [Fact]
    public async Task Middleware_order_and_memory_middleware_inserted()
    {
        var provider = new TestModelProvider(new TestAgentModel());
        var memory = new TrackingMemoryService();
        memory.StoredMessages.Add("remembered context");

        var calls = new List<string>();
        var snapshots = new List<IReadOnlyList<ChatMessage>>();

        var mw1 = new RecordingMiddleware("mw1", calls, snapshots);
        var mw2 = new RecordingMiddleware("mw2", calls, snapshots);

        var assistant = new AgentBuilder()
            .WithModelProvider(provider)
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
        var provider = new TestModelProvider(new TestAgentModel());
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
        var provider = new TestModelProvider(new TestAgentModel());
        var memory = new TrackingMemoryService();
        var assistant = new AgentBuilder()
            .WithModelProvider(provider)
            .WithMemory(memory)
            .Build();

        await assistant.InitializeAsync();
        await assistant.InitializeAsync();

        Assert.True(memory.Initialized);
    }

    [Fact]
    public async Task MemoryMiddleware_retrieves_and_injects_context()
    {
        var provider = new TestModelProvider(new TestAgentModel());
        var memory = new TrackingMemoryService();
        memory.StoredMessages.Add("prior conversation");
        var assistant = new AgentBuilder()
            .WithModelProvider(provider)
            .WithMemory(memory)
            .Build();

        // use a model that echoes full working messages for inspection
        var echoModel = new InspectingModel();
        var customProvider = new TestModelProvider(echoModel);
        assistant = new AgentBuilder()
            .WithModelProvider(customProvider)
            .WithMemory(memory)
            .Build();

        var response = await assistant.ReplyAsync("question");
        Assert.Contains("prior conversation", response);
    }

    private sealed class TestModelProvider : IModelProvider
    {
        private readonly IAgentModel _model;

        public TestModelProvider(IAgentModel model) => _model = model;

        public IAgentModel CreateModel() => _model;
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
            Assert.Equal("ok", res);
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
            .WithModelProvider(new TestModelProvider(new ToolCallingModel()))
            .WithTool(new UppercaseTool())
            .Build();

        var response = await assistant.ReplyAsync("please shout hello world");

        Assert.Equal("Tool says: HELLO WORLD", response);
    }

    [Fact]
    public async Task ReplyAsync_repeated_same_tool_call_returns_last_tool_result_instead_of_throwing()
    {
        var assistant = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new RepeatingToolCallingModel()))
            .WithTool(new UppercaseTool())
            .Build();

        var response = await assistant.ReplyAsync("please shout hello world");

        Assert.Equal("HELLO WORLD", response);
    }

    [Fact]
    public async Task ReplyAsync_includes_registered_tools_in_model_context()
    {
        IReadOnlyList<ChatMessage>? captured = null;

        var assistant = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new CaptureModel(messages =>
            {
                captured = messages;
                return new AgentResponse("ok");
            })))
            .WithTool(new UppercaseTool())
            .Build();

        var response = await assistant.ReplyAsync("hello");

        Assert.Equal("ok", response);
        Assert.NotNull(captured);
        var toolSystem = captured!.FirstOrDefault(m => m.Role == ChatRole.System && m.Content.Contains("Available tools:"));
        Assert.NotNull(toolSystem);
        Assert.Contains("uppercase", toolSystem!.Content);
    }

    [Fact]
    public void Build_throws_when_duplicate_tool_names_registered()
    {
        var builder = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new TestAgentModel()))
            .WithTool(new UppercaseTool())
            .WithTool(new UppercaseTool());

        Assert.Throws<InvalidOperationException>(() => builder.Build());
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
                var result = lastTool.Content.Split(':', 2)[1].Trim();
                return Task.FromResult(new AgentResponse($"Tool says: {result}"));
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
}
