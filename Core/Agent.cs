using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Agentic.Abstractions;
using Agentic.Loaders;
using Agentic.Middleware;

namespace Agentic.Core;

public sealed class Agent : IAgent, IAsyncDisposable
{
    private const int MaxToolCallDepth = 12;
    private readonly IAgentModel _model;
    private readonly IMemoryService? _memoryService;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IAssistantContextFactory _contextFactory;
    private readonly IReadOnlyList<IAssistantMiddleware> _middlewares;
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly ISkillLoader? _skillLoader;
    private readonly ISoulLoader? _soulLoader;
    private readonly Func<string, string, SoulDocument, SoulDocument?>? _soulLearningCallback;
    private readonly List<ChatMessage> _history = [];
    // Fix 3: cache ToolParameterMetadata per tool type to avoid repeated reflection
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<IToolParameterMetadata>> ParameterMetadataCache = new();
    // Fix 8: cache the middleware pipeline — tools and middlewares don't change after construction
    private AgentHandler? _pipeline;
    private AgentStreamingHandler? _streamingPipeline;
    // Fix 14: cache the tool catalog string (built once since tools don't change after construction)
    private string? _toolCatalogMessage;
    // Soul and Skills system prompt cached after first InitializeAsync
    private string? _soulAndSkillsMessage;
    private List<Skill>? _skills;
    private SoulDocument? _soul;
    private bool _initialized;

    internal Agent(
        IAgentModel model,
        IMemoryService? memoryService,
        IEmbeddingProvider? embeddingProvider,
        IAssistantContextFactory contextFactory,
        IReadOnlyList<IAssistantMiddleware> middlewares,
        IReadOnlyDictionary<string, ITool> tools,
        ISkillLoader? skillLoader = null,
        ISoulLoader? soulLoader = null,
        Func<string, string, SoulDocument, SoulDocument?>? soulLearningCallback = null,
        HeartbeatOptions? heartbeatOptions = null)
    {
        _model = model;
        _memoryService = memoryService;
        _embeddingProvider = embeddingProvider;
        _contextFactory = contextFactory;
        _middlewares = middlewares;
        _tools = tools;
        _skillLoader = skillLoader;
        _soulLoader = soulLoader;
        _soulLearningCallback = soulLearningCallback;
        // Heartbeat service is constructed here so it can safely reference `this`.
        Heartbeat = heartbeatOptions is not null
            ? new AgentHeartbeatService(this, heartbeatOptions)
            : null;
    }

    public IReadOnlyList<ChatMessage> History => _history;

    public IReadOnlyList<Skill>? Skills => _skills;

    public SoulDocument? Soul => _soul;

    public IHeartbeatService? Heartbeat { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        if (_memoryService is not null)
        {
            await _memoryService.InitializeAsync(cancellationToken);
        }

        if (_embeddingProvider is not null)
        {
            await _embeddingProvider.InitializeAsync(cancellationToken);
        }

        if (_skillLoader is not null)
        {
            _skills = (await _skillLoader.LoadSkillsAsync(cancellationToken)).ToList();
        }

        if (_soulLoader is not null)
        {
            _soul = await _soulLoader.LoadSoulAsync(cancellationToken);
        }

        _soulAndSkillsMessage = BuildSoulAndSkillsMessage(_soul, _skills);
        _initialized = true;
    }

    public async Task UpdateSoulAsync(CancellationToken cancellationToken = default)
    {
        if (_soulLoader is not null)
        {
            _soul = await _soulLoader.ReloadSoulAsync(cancellationToken);
            _soulAndSkillsMessage = BuildSoulAndSkillsMessage(_soul, _skills);
        }
    }

    public async Task UpdateSoulAsync(SoulDocument updatedSoul, CancellationToken cancellationToken = default)
    {
        _soul = updatedSoul;
        _soulAndSkillsMessage = BuildSoulAndSkillsMessage(_soul, _skills);

        if (_soulLoader is IPersistentSoulLoader persistentLoader)
        {
            await persistentLoader.UpdateSoulAsync(updatedSoul, cancellationToken);
        }
    }

    public async Task<AgentReply> ReplyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        using var activity = AgenticTelemetry.ActivitySource.StartActivity(AgenticTelemetry.Spans.Reply);
        activity?.SetTag(AgenticTelemetry.Tags.AgentHistoryLen, _history.Count);

        var sw = Stopwatch.StartNew();
        try
        {
            var context = PrepareContext(input);
            var handler = _pipeline ??= BuildPipeline();
            var response = await RunToolLoopAsync(context, handler, cancellationToken);

            var userMessage = new ChatMessage(ChatRole.User, input);
            var assistantMessage = new ChatMessage(ChatRole.Assistant, response.Content);

            _history.Add(userMessage);
            _history.Add(assistantMessage);

            await PersistToMemoryAsync(input, response.Content, cancellationToken);
            await MaybeLearnFromConversationAsync(input, response.Content, cancellationToken);

            sw.Stop();
            AgenticTelemetry.ReplyCounter.Add(1);
            AgenticTelemetry.ReplyDuration.Record(sw.Elapsed.TotalMilliseconds);

            return new AgentReply(
                response.Content,
                userMessage,
                assistantMessage,
                Usage: response.Usage,
                FinishReason: response.FinishReason,
                ModelId: response.ModelId,
                Duration: sw.Elapsed);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async IAsyncEnumerable<StreamingToken> StreamAsync(
        string input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_initialized)
            await InitializeAsync(cancellationToken);

        using var activity = AgenticTelemetry.ActivitySource.StartActivity(AgenticTelemetry.Spans.Reply);
        activity?.SetTag(AgenticTelemetry.Tags.AgentHistoryLen, _history.Count);

        var sw = Stopwatch.StartNew();

        var context = PrepareContext(input);
        var streamingHandler = _streamingPipeline ??= BuildStreamingPipeline();

        // Accumulate full content and metadata for history/memory
        var contentBuilder = new StringBuilder();
        StreamingToken? finalToken = null;

        // Stream from the model; if the response contains tool calls, we resolve the
        // tool loop without streaming (same as ReplyAsync) and then stream the final answer.
        IReadOnlyList<AgentToolCall>? pendingToolCalls = null;

        await foreach (var token in streamingHandler(context, cancellationToken).WithCancellation(cancellationToken))
        {
            if (!token.IsComplete)
            {
                contentBuilder.Append(token.Delta);
                yield return token;
            }
            else
            {
                finalToken = token;
                pendingToolCalls = token.ToolCalls;
            }
        }

        // If the model returned tool calls we need to resolve them before we can
        // give the user a final streamed answer.
        if (pendingToolCalls is { Count: > 0 })
        {
            // Build an AgentResponse from the streamed tool-call intent so RunToolLoopAsync
            // can continue from this point.
            var toolCallResponse = new AgentResponse(
                contentBuilder.ToString(),
                pendingToolCalls,
                finalToken?.FinalUsage,
                finalToken?.FinishReason,
                finalToken?.ModelId);

            // Add the assistant tool-call message to working context
            context.WorkingMessages.Add(new ChatMessage(
                ChatRole.Assistant,
                toolCallResponse.Content,
                ToolCalls: toolCallResponse.ToolCalls));

            var syncHandler = _pipeline ??= BuildPipeline();
            var resolvedResponse = await RunToolLoopFromToolCallsAsync(context, toolCallResponse, syncHandler, cancellationToken);

            // Now stream the final resolved answer
            contentBuilder.Clear();
            await foreach (var token in streamingHandler(context, cancellationToken).WithCancellation(cancellationToken))
            {
                if (!token.IsComplete)
                {
                    contentBuilder.Append(token.Delta);
                    yield return token;
                }
                else
                {
                    finalToken = token;
                }
            }

            _ = resolvedResponse; // consumed for side-effects (tool execution)
        }

        var fullContent = contentBuilder.ToString();
        var userMessage = new ChatMessage(ChatRole.User, input);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, fullContent);

        _history.Add(userMessage);
        _history.Add(assistantMessage);

        await PersistToMemoryAsync(input, fullContent, cancellationToken);
        await MaybeLearnFromConversationAsync(input, fullContent, cancellationToken);

        sw.Stop();
        AgenticTelemetry.ReplyCounter.Add(1);
        AgenticTelemetry.ReplyDuration.Record(sw.Elapsed.TotalMilliseconds);

        // Emit final sentinel
        yield return new StreamingToken(
            Delta: string.Empty,
            IsComplete: true,
            FinalUsage: finalToken?.FinalUsage,
            FinishReason: finalToken?.FinishReason,
            ModelId: finalToken?.ModelId);
    }

    private AgentContext PrepareContext(string input)
    {
        var context = _contextFactory.Create(input, _history);

        if (_tools.Count > 0)
        {
            _toolCatalogMessage ??= BuildToolCatalogMessage(_tools.Values);
            context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, _toolCatalogMessage));
        }

        if (_soulAndSkillsMessage is not null)
        {
            context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, _soulAndSkillsMessage));
        }

        return context;
    }

    private AgentHandler BuildPipeline()
    {
        AgentHandler handler = (ctx, ct) => _model.CompleteAsync(ctx.WorkingMessages as IReadOnlyList<ChatMessage> ?? ctx.WorkingMessages.ToList(), ct);

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = handler;
            handler = (ctx, ct) => middleware.InvokeAsync(ctx, next, ct);
        }

        return handler;
    }

    private AgentStreamingHandler BuildStreamingPipeline()
    {
        AgentStreamingHandler handler = (ctx, ct) => _model.StreamAsync(ctx.WorkingMessages as IReadOnlyList<ChatMessage> ?? ctx.WorkingMessages.ToList(), ct);

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = handler;
            handler = (ctx, ct) => middleware.StreamAsync(ctx, next, ct);
        }

        return handler;
    }

    private async Task<AgentResponse> RunToolLoopAsync(
        AgentContext context,
        AgentHandler handler,
        CancellationToken cancellationToken)
    {
        var response = await handler(context, cancellationToken);
        var toolCallDepth = 0;
        var seenToolCallSignatures = new HashSet<string>(StringComparer.Ordinal);
        UsageInfo? aggregatedUsage = response.Usage;

        while (response.HasToolCalls)
        {
            if (toolCallDepth++ >= MaxToolCallDepth)
            {
                return new AgentResponse(BuildToolLoopFallbackResponse(context), null, aggregatedUsage);
            }

            var signature = BuildToolCallSignature(response.ToolCalls!);
            if (!seenToolCallSignatures.Add(signature))
            {
                return new AgentResponse(BuildToolLoopFallbackResponse(context), null, aggregatedUsage);
            }

            // Insert the assistant message that requested tool calls so the model
            // can correlate tool_call_id values in subsequent tool result messages.
            context.WorkingMessages.Add(new ChatMessage(
                ChatRole.Assistant,
                response.Content,
                ToolCalls: response.ToolCalls));

            await ExecuteToolCallsAsync(context, response.ToolCalls!, cancellationToken);

            context.WorkingMessages.Add(new ChatMessage(
                ChatRole.System,
                "Use the latest tool results to answer the user. Only call another tool if additional information is strictly required."));

            response = await handler(context, cancellationToken);

            // Accumulate token usage across loop iterations
            if (response.Usage is not null)
                aggregatedUsage = aggregatedUsage is not null ? aggregatedUsage + response.Usage : response.Usage;
        }

        // Return final response with aggregated usage
        return response with { Usage = aggregatedUsage };
    }

    /// <summary>
    /// Continues the tool loop starting from an already-resolved initial tool-call response.
    /// Used by <see cref="StreamAsync"/> after the first streaming pass produced tool calls.
    /// </summary>
    private async Task<AgentResponse> RunToolLoopFromToolCallsAsync(
        AgentContext context,
        AgentResponse initialResponse,
        AgentHandler handler,
        CancellationToken cancellationToken)
    {
        await ExecuteToolCallsAsync(context, initialResponse.ToolCalls!, cancellationToken);
        context.WorkingMessages.Add(new ChatMessage(
            ChatRole.System,
            "Use the latest tool results to answer the user. Only call another tool if additional information is strictly required."));

        // Let the normal tool loop handle any further chained tool calls
        return await RunToolLoopAsync(context, handler, cancellationToken);
    }

    private async Task ExecuteToolCallsAsync(
        AgentContext context,
        IReadOnlyList<AgentToolCall> toolCalls,
        CancellationToken cancellationToken)
    {
        foreach (var toolCall in toolCalls)
        {
            if (!_tools.TryGetValue(toolCall.Name, out var tool))
            {
                context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, $"Error - Tool '{toolCall.Name}' is not registered.", toolCall.Name, toolCall.ToolCallId));
                continue;
            }

            // Bind structured parameters if the tool has them
            var parameters = ParameterMetadataCache.GetOrAdd(tool.GetType(), static t => ToolParameterMetadata.ExtractFromTool(t));
            if (parameters.Count > 0)
            {
                try
                {
                    ToolParameterBinder.BindParameters(tool, toolCall.Arguments, parameters);
                }
                catch (Exception ex)
                {
                    context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, $"Error - {ex.Message}", toolCall.Name, toolCall.ToolCallId));
                    continue;
                }
            }

            using var toolActivity = AgenticTelemetry.ActivitySource.StartActivity(AgenticTelemetry.Spans.ToolCall);
            toolActivity?.SetTag(AgenticTelemetry.Tags.AgentToolName, toolCall.Name);
            var toolSw = Stopwatch.StartNew();
            try
            {
                var toolResult = await tool.InvokeAsync(toolCall.Arguments, cancellationToken);
                toolSw.Stop();
                toolActivity?.SetTag(AgenticTelemetry.Tags.AgentToolSuccess, true);
                AgenticTelemetry.ToolCallCounter.Add(1, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.AgentToolName, toolCall.Name));
                AgenticTelemetry.ToolCallDuration.Record(toolSw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.AgentToolName, toolCall.Name));
                context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, toolResult, toolCall.Name, toolCall.ToolCallId));
            }
            catch (Exception ex)
            {
                toolSw.Stop();
                toolActivity?.SetTag(AgenticTelemetry.Tags.AgentToolSuccess, false);
                toolActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                AgenticTelemetry.ToolCallCounter.Add(1, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.AgentToolName, toolCall.Name));
                AgenticTelemetry.ToolCallDuration.Record(toolSw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.AgentToolName, toolCall.Name));
                context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, $"Error - {ex.Message}", toolCall.Name, toolCall.ToolCallId));
            }
        }
    }

    private async Task PersistToMemoryAsync(string input, string responseContent, CancellationToken cancellationToken)
    {
        if (_memoryService is null)
        {
            return;
        }

        var userId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input + DateTimeOffset.UtcNow.Ticks)));
        await _memoryService.StoreMessageAsync(userId, input, cancellationToken);
        var responseId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(responseContent + DateTimeOffset.UtcNow.Ticks)));
        await _memoryService.StoreMessageAsync(responseId, responseContent, cancellationToken);

        if (_embeddingProvider is not null)
        {
            var userEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(input, cancellationToken);
            await _memoryService.StoreEmbeddingAsync(userId, userEmbedding, cancellationToken);
            var responseEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(responseContent, cancellationToken);
            await _memoryService.StoreEmbeddingAsync(responseId, responseEmbedding, cancellationToken);
        }
    }

    private async Task MaybeLearnFromConversationAsync(string input, string reply, CancellationToken cancellationToken)
    {
        if (_soul is null || _soulLearningCallback is null)
            return;

        var updated = _soulLearningCallback(input, reply, _soul);
        if (updated is not null)
            await UpdateSoulAsync(updated, cancellationToken);
    }

    private static string? BuildSoulAndSkillsMessage(SoulDocument? soul, List<Skill>? skills)
    {
        var parts = new List<string>();

        if (soul is not null)
        {
            var soulPrompt = FileSystemSoulLoader.ToSystemPrompt(soul);
            if (!string.IsNullOrWhiteSpace(soulPrompt))
                parts.Add(soulPrompt);
        }

        if (skills is { Count: > 0 })
        {
            var skillsXml = FileSystemSkillLoader.ToPromptXml(skills);
            if (!string.IsNullOrWhiteSpace(skillsXml))
                parts.Add(skillsXml);
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    private static string BuildToolCatalogMessage(IEnumerable<ITool> tools)
    {
        var lines = new List<string>
        {
            "Available tools:"
        };

        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {tool.Name}: {tool.Description}");

            // Include parameter schema if the tool has structured parameters
            var parameters = ParameterMetadataCache.GetOrAdd(tool.GetType(), static t => ToolParameterMetadata.ExtractFromTool(t));
            if (parameters.Count > 0)
            {
                var schema = ToolParameterSchema.FromTool(tool, parameters);
                lines.Add($"  Parameters: {schema.ToJson()}");
            }
        }

        lines.Add("If a tool is needed, return tool calls using the exact tool name and arguments.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildToolCallSignature(IReadOnlyList<AgentToolCall> toolCalls)
    {
        return string.Join("|", toolCalls.Select(tc => $"{tc.Name}\u001f{tc.Arguments}"));
    }

    private static string BuildToolLoopFallbackResponse(AgentContext context)
    {
        var lastToolMessage = context.WorkingMessages.LastOrDefault(m => m.Role == ChatRole.Tool);
        if (lastToolMessage is null || string.IsNullOrWhiteSpace(lastToolMessage.Content))
        {
            return "I couldn't complete this request because tool calling kept repeating. Please try rephrasing your question.";
        }

        return lastToolMessage.Content;
    }

    /// <summary>
    /// Removes the last user+assistant message pair from the conversation history.
    /// Called by <see cref="AgentHeartbeatService"/> when the agent replies with the
    /// silent token so that silent heartbeat exchanges don't pollute context.
    /// </summary>
    internal void PruneLastExchange()
    {
        // History is always written as [user, assistant] pairs — remove both.
        if (_history.Count >= 2)
            _history.RemoveRange(_history.Count - 2, 2);
    }

    public async ValueTask DisposeAsync()
    {
        if (Heartbeat is not null)
            await Heartbeat.DisposeAsync();

        if (_memoryService is IAsyncDisposable asyncDisposableMemory)
            await asyncDisposableMemory.DisposeAsync();
        else if (_memoryService is IDisposable disposableMemory)
            disposableMemory.Dispose();

        if (_embeddingProvider is IAsyncDisposable asyncDisposableEmbedding)
            await asyncDisposableEmbedding.DisposeAsync();
        else if (_embeddingProvider is IDisposable disposableEmbedding)
            disposableEmbedding.Dispose();
    }
}
