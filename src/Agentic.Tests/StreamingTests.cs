using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Middleware;
using Agentic.Tests.Fakes;
using Xunit;

namespace Agentic.Tests;

/// <summary>
/// Tests for the streaming path:
/// - Agent.StreamAsync yields incremental tokens then a final complete token
/// - Final StreamingToken carries IsComplete=true, FinalUsage, FinishReason, ModelId
/// - History is updated after the stream is fully consumed
/// - Middleware streaming pipeline is invoked correctly
/// - AgentReply.Usage / FinishReason / ModelId / Duration are populated from ReplyAsync
/// </summary>
public sealed class StreamingTests
{
    // -----------------------------------------------------------------------
    // StreamAsync — incremental tokens
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_yields_incremental_tokens_before_complete()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["Hello", " ", "World"])))
            .Build();

        await agent.InitializeAsync();

        var tokens = new List<StreamingToken>();
        await foreach (var token in agent.StreamAsync("hi"))
            tokens.Add(token);

        // All non-complete tokens should carry text
        var incremental = tokens.FindAll(t => !t.IsComplete);
        Assert.Equal(3, incremental.Count);
        Assert.Equal("Hello", incremental[0].Delta);
        Assert.Equal(" ", incremental[1].Delta);
        Assert.Equal("World", incremental[2].Delta);
    }

    [Fact]
    public async Task StreamAsync_last_token_is_complete()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["Hi"])))
            .Build();

        await agent.InitializeAsync();

        StreamingToken? last = null;
        await foreach (var token in agent.StreamAsync("hello"))
            last = token;

        Assert.NotNull(last);
        Assert.True(last!.IsComplete);
        Assert.Equal(string.Empty, last.Delta);
    }

    [Fact]
    public async Task StreamAsync_only_one_complete_token_is_emitted()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["a", "b", "c"])))
            .Build();

        await agent.InitializeAsync();

        var completeTokens = new List<StreamingToken>();
        await foreach (var token in agent.StreamAsync("test"))
            if (token.IsComplete)
                completeTokens.Add(token);

        Assert.Single(completeTokens);
    }

    // -----------------------------------------------------------------------
    // StreamAsync — final token metadata
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_final_token_carries_FinalUsage()
    {
        var usage = new UsageInfo(PromptTokens: 5, CompletionTokens: 10, TotalTokens: 15);
        var model = new MetadataStreamModel("answer", usage, "stop", "test-model-v1");

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(model))
            .Build();

        await agent.InitializeAsync();

        StreamingToken? final = null;
        await foreach (var token in agent.StreamAsync("query"))
            if (token.IsComplete) final = token;

        Assert.NotNull(final);
        Assert.Equal(usage, final!.FinalUsage);
    }

    [Fact]
    public async Task StreamAsync_final_token_carries_FinishReason()
    {
        var model = new MetadataStreamModel("answer", null, "stop", null);

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(model))
            .Build();

        await agent.InitializeAsync();

        StreamingToken? final = null;
        await foreach (var token in agent.StreamAsync("query"))
            if (token.IsComplete) final = token;

        Assert.Equal("stop", final?.FinishReason);
    }

    [Fact]
    public async Task StreamAsync_final_token_carries_ModelId()
    {
        var model = new MetadataStreamModel("answer", null, null, "gpt-test-model");

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(model))
            .Build();

        await agent.InitializeAsync();

        StreamingToken? final = null;
        await foreach (var token in agent.StreamAsync("query"))
            if (token.IsComplete) final = token;

        Assert.Equal("gpt-test-model", final?.ModelId);
    }

    [Fact]
    public async Task StreamAsync_final_token_has_null_metadata_when_model_provides_none()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["hi"])))
            .Build();

        await agent.InitializeAsync();

        StreamingToken? final = null;
        await foreach (var token in agent.StreamAsync("query"))
            if (token.IsComplete) final = token;

        Assert.Null(final?.FinalUsage);
        Assert.Null(final?.FinishReason);
        Assert.Null(final?.ModelId);
    }

    // -----------------------------------------------------------------------
    // StreamAsync — history is updated after stream is fully consumed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_history_is_empty_before_stream_is_consumed()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["hello"])))
            .Build();

        await agent.InitializeAsync();

        // Enumerate only up to (but not including) the final token
        var enumerator = agent.StreamAsync("ping").GetAsyncEnumerator();
        // History should remain empty while enumeration hasn't completed
        Assert.Empty(agent.History);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task StreamAsync_history_is_updated_after_full_consumption()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["hello"])))
            .Build();

        await agent.InitializeAsync();

        await foreach (var _ in agent.StreamAsync("ping")) { /* consume fully */ }

        Assert.Equal(2, agent.History.Count);
        Assert.Equal(ChatRole.User, agent.History[0].Role);
        Assert.Equal("ping", agent.History[0].Content);
        Assert.Equal(ChatRole.Assistant, agent.History[1].Role);
        Assert.Equal("hello", agent.History[1].Content);
    }

    [Fact]
    public async Task StreamAsync_history_accumulates_content_from_all_tokens()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["Hel", "lo", " World"])))
            .Build();

        await agent.InitializeAsync();

        await foreach (var _ in agent.StreamAsync("greet")) { }

        Assert.Equal("Hello World", agent.History[1].Content);
    }

    [Fact]
    public async Task StreamAsync_multiple_turns_accumulate_history()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new EchoModel()))
            .Build();

        await agent.InitializeAsync();

        await foreach (var _ in agent.StreamAsync("first")) { }
        await foreach (var _ in agent.StreamAsync("second")) { }

        Assert.Equal(4, agent.History.Count);
        Assert.Equal("first", agent.History[0].Content);
        Assert.Equal("second", agent.History[2].Content);
    }

    // -----------------------------------------------------------------------
    // StreamAsync — middleware streaming pipeline is invoked
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_middleware_StreamAsync_is_invoked()
    {
        var streamingInvoked = false;
        var middleware = new TrackingStreamMiddleware(() => streamingInvoked = true);

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["hi"])))
            .WithMiddleware(middleware)
            .Build();

        await agent.InitializeAsync();

        await foreach (var _ in agent.StreamAsync("test")) { }

        Assert.True(streamingInvoked);
    }

    [Fact]
    public async Task StreamAsync_middleware_executes_in_registration_order()
    {
        var order = new List<string>();
        var mw1 = new NamedStreamMiddleware("mw1", order);
        var mw2 = new NamedStreamMiddleware("mw2", order);
        var mw3 = new NamedStreamMiddleware("mw3", order);

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["ok"])))
            .WithMiddleware(mw1)
            .WithMiddleware(mw2)
            .WithMiddleware(mw3)
            .Build();

        await agent.InitializeAsync();

        await foreach (var _ in agent.StreamAsync("test")) { }

        Assert.Equal(new[] { "mw1", "mw2", "mw3" }, order);
    }

    [Fact]
    public async Task StreamAsync_default_middleware_StreamAsync_passes_tokens_through()
    {
        // A middleware that only overrides InvokeAsync (not StreamAsync) — default DIM should
        // transparently forward all streaming tokens.
        var passThrough = new InvokeOnlyMiddleware();

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["a", "b"])))
            .WithMiddleware(passThrough)
            .Build();

        await agent.InitializeAsync();

        var deltas = new List<string>();
        await foreach (var token in agent.StreamAsync("test"))
            if (!token.IsComplete)
                deltas.Add(token.Delta);

        Assert.Equal(new[] { "a", "b" }, deltas);
    }

    [Fact]
    public async Task StreamAsync_middleware_can_short_circuit_stream()
    {
        var middleware = new ShortCircuitStreamMiddleware("overridden");

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new MultiTokenModel(["should-not-appear"])))
            .WithMiddleware(middleware)
            .Build();

        await agent.InitializeAsync();

        var deltas = new List<string>();
        await foreach (var token in agent.StreamAsync("test"))
            if (!token.IsComplete)
                deltas.Add(token.Delta);

        Assert.Equal(new[] { "overridden" }, deltas);
    }

    // -----------------------------------------------------------------------
    // ReplyAsync — AgentReply metadata fields
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReplyAsync_reply_Usage_is_populated()
    {
        var usage = new UsageInfo(10, 20, 30);
        var model = new MetadataCompleteModel("answer", usage, "stop", "test-model");

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(model))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("hello");

        Assert.NotNull(reply.Usage);
        Assert.Equal(10, reply.Usage!.PromptTokens);
        Assert.Equal(20, reply.Usage!.CompletionTokens);
        Assert.Equal(30, reply.Usage!.TotalTokens);
    }

    [Fact]
    public async Task ReplyAsync_reply_FinishReason_is_populated()
    {
        var model = new MetadataCompleteModel("answer", null, "stop", null);

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(model))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("hello");

        Assert.Equal("stop", reply.FinishReason);
    }

    [Fact]
    public async Task ReplyAsync_reply_ModelId_is_populated()
    {
        var model = new MetadataCompleteModel("answer", null, null, "gpt-test");

        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(model))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("hello");

        Assert.Equal("gpt-test", reply.ModelId);
    }

    [Fact]
    public async Task ReplyAsync_reply_Duration_is_positive()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new EchoModel()))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("ping");

        Assert.True(reply.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task ReplyAsync_reply_fields_are_null_when_model_provides_none()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new EchoModel()))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("ping");

        Assert.Null(reply.Usage);
        Assert.Null(reply.FinishReason);
        Assert.Null(reply.ModelId);
    }

    [Fact]
    public async Task ReplyAsync_reply_Content_and_implicit_string_still_work()
    {
        var agent = new AgentBuilder()
            .WithChatClient(new FakeChatClient(new EchoModel()))
            .Build();

        await agent.InitializeAsync();
        var reply = await agent.ReplyAsync("hello");

        Assert.Equal("echo: hello", reply.Content);
        string text = reply;
        Assert.Equal("echo: hello", text);
    }

    // -----------------------------------------------------------------------
    // UsageInfo — accumulation operator
    // -----------------------------------------------------------------------

    [Fact]
    public void UsageInfo_addition_operator_accumulates_correctly()
    {
        var a = new UsageInfo(10, 20, 30);
        var b = new UsageInfo(5, 15, 20);
        var sum = a + b;

        Assert.Equal(15, sum.PromptTokens);
        Assert.Equal(35, sum.CompletionTokens);
        Assert.Equal(50, sum.TotalTokens);
    }

    // -----------------------------------------------------------------------
    // Helpers / test models
    // -----------------------------------------------------------------------

    /// <summary>Streams the provided deltas as individual incremental tokens, then a final complete token.</summary>
    private sealed class MultiTokenModel : IAgentModel
    {
        private readonly IReadOnlyList<string> _deltas;
        public MultiTokenModel(IReadOnlyList<string> deltas) => _deltas = deltas;

        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse(string.Concat(_deltas)));

        public async IAsyncEnumerable<StreamingToken> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var delta in _deltas)
                yield return new StreamingToken(delta, IsComplete: false);

            yield return new StreamingToken(Delta: string.Empty, IsComplete: true);
        }
    }

    /// <summary>Streams a single text token followed by a complete token that carries metadata.</summary>
    private sealed class MetadataStreamModel : IAgentModel
    {
        private readonly string _text;
        private readonly UsageInfo? _usage;
        private readonly string? _finishReason;
        private readonly string? _modelId;

        public MetadataStreamModel(string text, UsageInfo? usage, string? finishReason, string? modelId)
        {
            _text = text;
            _usage = usage;
            _finishReason = finishReason;
            _modelId = modelId;
        }

        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse(_text, Usage: _usage, FinishReason: _finishReason, ModelId: _modelId));

        public async IAsyncEnumerable<StreamingToken> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new StreamingToken(_text, IsComplete: false);
            yield return new StreamingToken(
                Delta: string.Empty,
                IsComplete: true,
                FinalUsage: _usage,
                FinishReason: _finishReason,
                ModelId: _modelId);
        }
    }

    /// <summary>Returns metadata-carrying AgentResponse from CompleteAsync; uses FakeModelStreamHelper for streaming.</summary>
    private sealed class MetadataCompleteModel : IAgentModel
    {
        private readonly string _text;
        private readonly UsageInfo? _usage;
        private readonly string? _finishReason;
        private readonly string? _modelId;

        public MetadataCompleteModel(string text, UsageInfo? usage, string? finishReason, string? modelId)
        {
            _text = text;
            _usage = usage;
            _finishReason = finishReason;
            _modelId = modelId;
        }

        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse(_text, Usage: _usage, FinishReason: _finishReason, ModelId: _modelId));

        public IAsyncEnumerable<StreamingToken> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
            => FakeModelStreamHelper.StreamFromCompleteAsync(this, messages, cancellationToken);
    }

    /// <summary>Simple echo model — no metadata.</summary>
    private sealed class EchoModel : IAgentModel
    {
        public Task<AgentResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content ?? "";
            return Task.FromResult(new AgentResponse($"echo: {last}"));
        }

        public IAsyncEnumerable<StreamingToken> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
            => FakeModelStreamHelper.StreamFromCompleteAsync(this, messages, cancellationToken);
    }

    /// <summary>Middleware that records its name each time StreamAsync is entered.</summary>
    private sealed class TrackingStreamMiddleware : IAssistantMiddleware
    {
        private readonly Action _onStream;
        public TrackingStreamMiddleware(Action onStream) => _onStream = onStream;

        public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
            => next(context, cancellationToken);

        public async IAsyncEnumerable<StreamingToken> StreamAsync(
            AgentContext context,
            AgentStreamingHandler next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _onStream();
            await foreach (var token in next(context, cancellationToken).WithCancellation(cancellationToken))
                yield return token;
        }
    }

    /// <summary>Middleware that appends its name to a list when its streaming path is entered.</summary>
    private sealed class NamedStreamMiddleware : IAssistantMiddleware
    {
        private readonly string _name;
        private readonly List<string> _order;

        public NamedStreamMiddleware(string name, List<string> order) { _name = name; _order = order; }

        public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
            => next(context, cancellationToken);

        public async IAsyncEnumerable<StreamingToken> StreamAsync(
            AgentContext context,
            AgentStreamingHandler next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _order.Add(_name);
            await foreach (var token in next(context, cancellationToken).WithCancellation(cancellationToken))
                yield return token;
        }
    }

    /// <summary>Middleware that only implements InvokeAsync — relies on DIM for StreamAsync.</summary>
    private sealed class InvokeOnlyMiddleware : IAssistantMiddleware
    {
        public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
            => next(context, cancellationToken);
    }

    /// <summary>Middleware that short-circuits the streaming pipeline, emitting a fixed token.</summary>
    private sealed class ShortCircuitStreamMiddleware : IAssistantMiddleware
    {
        private readonly string _response;
        public ShortCircuitStreamMiddleware(string response) => _response = response;

        public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse(_response));

        public async IAsyncEnumerable<StreamingToken> StreamAsync(
            AgentContext context,
            AgentStreamingHandler next,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new StreamingToken(_response, IsComplete: false);
            yield return new StreamingToken(Delta: string.Empty, IsComplete: true);
        }
    }
}
