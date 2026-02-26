using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Middleware;
using Xunit;

namespace Agentic.Tests;

/// <summary>
/// Tests for middleware pipeline composition and execution order.
/// </summary>
public sealed class MiddlewareTests
{
    [Fact]
    public async Task Middleware_executes_in_registration_order()
    {
        var calls = new List<string>();

        var mw1 = new OrderRecordingMiddleware("mw1", calls);
        var mw2 = new OrderRecordingMiddleware("mw2", calls);
        var mw3 = new OrderRecordingMiddleware("mw3", calls);

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .UseMiddleware(mw1)
            .UseMiddleware(mw2)
            .UseMiddleware(mw3)
            .Build();

        await agent.ReplyAsync("test");

        Assert.Equal(new[] { "mw1", "mw2", "mw3" }, calls);
    }

    [Fact]
    public async Task Middleware_can_modify_context()
    {
        var modifiedContext = false;

        var middleware = new ContextModifyingMiddleware(ctx =>
        {
            // Add a marker to context
            if (ctx.WorkingMessages.Count == 0)
            {
                modifiedContext = true;
            }
        });

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .UseMiddleware(middleware)
            .Build();

        await agent.ReplyAsync("test");

        Assert.True(modifiedContext);
    }

    [Fact]
    public async Task Multiple_middlewares_all_execute()
    {
        var executed = new List<string>();

        var mw1 = new TrackingMiddleware("first", executed);
        var mw2 = new TrackingMiddleware("second", executed);

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .UseMiddleware(mw1)
            .UseMiddleware(mw2)
            .Build();

        await agent.ReplyAsync("hello");

        Assert.Contains("first", executed);
        Assert.Contains("second", executed);
    }

    [Fact]
    public async Task Middleware_receives_user_input()
    {
        var captured = new List<string>();

        var middleware = new InputCapturingMiddleware(captured);

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .UseMiddleware(middleware)
            .Build();

        await agent.ReplyAsync("hello world");

        Assert.Single(captured);
        Assert.Equal("hello world", captured[0]);
    }

    [Fact]
    public async Task Memory_middleware_auto_inserted_before_custom_middleware()
    {
        var capturedMessages = new List<IReadOnlyList<ChatMessage>>();

        var middleware = new MessageCapturingMiddleware(capturedMessages);

        var memory = new TrackingMemoryService();
        memory.StoredMessages.Add("remembered");

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .WithMemory(memory)
            .UseMiddleware(middleware)
            .Build();

        await agent.ReplyAsync("test");

        // Custom middleware should see the system message with remembered content injected
        Assert.NotEmpty(capturedMessages);
        var messages = capturedMessages[0];
        var systemMsg = messages.FirstOrDefault(m => m.Role == ChatRole.System);
        Assert.NotNull(systemMsg);
        Assert.Contains("remembered", systemMsg!.Content);
    }

    [Fact]
    public async Task Middleware_exception_propagates()
    {
        var middleware = new ExceptionThrowingMiddleware();

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .UseMiddleware(middleware)
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ReplyAsync("test"));
    }

    [Fact]
    public async Task Middleware_can_short_circuit_response()
    {
        var middleware = new ResponseShortCircuitMiddleware();

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new NeverCalledModel()))
            .UseMiddleware(middleware)
            .Build();

        var response = await agent.ReplyAsync("test");

        Assert.Equal("short-circuit response", response);
    }

    [Fact]
    public async Task Nested_middleware_execution()
    {
        var order = new List<string>();

        var mw1 = new OrderTrackingMiddleware("outer_enter", "outer_exit", order);
        var mw2 = new OrderTrackingMiddleware("inner_enter", "inner_exit", order);

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .UseMiddleware(mw1)
            .UseMiddleware(mw2)
            .Build();

        await agent.ReplyAsync("test");

        // mw1 enters first, mw2 enters, mw2 exits, mw1 exits
        Assert.Equal(new[]
        {
            "outer_enter", "inner_enter", "inner_exit", "outer_exit"
        }, order);
    }

    [Fact]
    public async Task Middleware_accesses_message_history()
    {
        var capturedHistoryCounts = new List<int>();

        var middleware = new HistoryTrackingMiddleware(capturedHistoryCounts);

        var agent = new AgentBuilder()
            .WithModelProvider(new TestModelProvider(new EchoModel()))
            .UseMiddleware(middleware)
            .Build();

        await agent.ReplyAsync("first");
        await agent.ReplyAsync("second");

        // First call should see empty history, second should see previous exchange
        Assert.Equal(2, capturedHistoryCounts.Count);
        Assert.Equal(0, capturedHistoryCounts[0]); // First call
        Assert.Equal(2, capturedHistoryCounts[1]); // Second call sees user+assistant from first
    }

    private sealed class OrderRecordingMiddleware : IAssistantMiddleware
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public OrderRecordingMiddleware(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _calls.Add(_name);
            return await next(context, cancellationToken);
        }
    }

    private sealed class ContextModifyingMiddleware : IAssistantMiddleware
    {
        private readonly Action<AgentContext> _modifier;

        public ContextModifyingMiddleware(Action<AgentContext> modifier)
        {
            _modifier = modifier;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _modifier(context);
            return await next(context, cancellationToken);
        }
    }

    private sealed class TrackingMiddleware : IAssistantMiddleware
    {
        private readonly string _name;
        private readonly List<string> _executed;

        public TrackingMiddleware(string name, List<string> executed)
        {
            _name = name;
            _executed = executed;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _executed.Add(_name);
            return await next(context, cancellationToken);
        }
    }

    private sealed class InputCapturingMiddleware : IAssistantMiddleware
    {
        private readonly List<string> _captured;

        public InputCapturingMiddleware(List<string> captured)
        {
            _captured = captured;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _captured.Add(context.Input);
            return await next(context, cancellationToken);
        }
    }

    private sealed class MessageCapturingMiddleware : IAssistantMiddleware
    {
        private readonly List<IReadOnlyList<ChatMessage>> _captured;

        public MessageCapturingMiddleware(List<IReadOnlyList<ChatMessage>> captured)
        {
            _captured = captured;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _captured.Add(context.WorkingMessages.ToList());
            return await next(context, cancellationToken);
        }
    }

    private sealed class ExceptionThrowingMiddleware : IAssistantMiddleware
    {
        public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Middleware error");
        }
    }

    private sealed class ResponseShortCircuitMiddleware : IAssistantMiddleware
    {
        public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            // Return response without calling next
            return Task.FromResult(new AgentResponse("short-circuit response"));
        }
    }

    private sealed class OrderTrackingMiddleware : IAssistantMiddleware
    {
        private readonly string _onEnter;
        private readonly string _onExit;
        private readonly List<string> _order;

        public OrderTrackingMiddleware(string onEnter, string onExit, List<string> order)
        {
            _onEnter = onEnter;
            _onExit = onExit;
            _order = order;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _order.Add(_onEnter);
            var result = await next(context, cancellationToken);
            _order.Add(_onExit);
            return result;
        }
    }

    private sealed class HistoryTrackingMiddleware : IAssistantMiddleware
    {
        private readonly List<int> _historyCounts;

        public HistoryTrackingMiddleware(List<int> historyCounts)
        {
            _historyCounts = historyCounts;
        }

        public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
        {
            _historyCounts.Add(context.History.Count);
            return await next(context, cancellationToken);
        }
    }

    private sealed class EchoModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            var lastUser = messages.Last(m => m.Role == ChatRole.User).Content;
            return Task.FromResult(new AgentResponse($"echo: {lastUser}"));
        }
    }

    private sealed class NeverCalledModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Model should not be called");
        }
    }

    private sealed class TestModelProvider : IModelProvider
    {
        private readonly IAgentModel _model;

        public TestModelProvider(IAgentModel model) => _model = model;

        public IAgentModel CreateModel() => _model;
    }

    private sealed class TrackingMemoryService : IMemoryService
    {
        public List<string> StoredMessages { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StoreMessageAsync(string id, string content, CancellationToken cancellationToken = default)
        {
            StoredMessages.Add(content);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> RetrieveRelevantAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(StoredMessages);
        }

        public Task StoreEmbeddingAsync(string id, float[] embedding, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<(string Content, float Score)>> RetrieveSimilarAsync(float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<(string Content, float Score)>>(new List<(string, float)>());
        }
    }
}
