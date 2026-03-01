using System.Collections.Concurrent;
using System.Diagnostics;
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
        Func<string, string, SoulDocument, SoulDocument?>? soulLearningCallback = null)
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
    }

    public IReadOnlyList<ChatMessage> History => _history;

    public IReadOnlyList<Skill>? Skills => _skills;

    public SoulDocument? Soul => _soul;

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

            return new AgentReply(response.Content, userMessage, assistantMessage);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
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

    private async Task<AgentResponse> RunToolLoopAsync(
        AgentContext context,
        AgentHandler handler,
        CancellationToken cancellationToken)
    {
        var response = await handler(context, cancellationToken);
        var toolCallDepth = 0;
        var seenToolCallSignatures = new HashSet<string>(StringComparer.Ordinal);

        while (response.HasToolCalls)
        {
            if (toolCallDepth++ >= MaxToolCallDepth)
            {
                return new AgentResponse(BuildToolLoopFallbackResponse(context));
            }

            var signature = BuildToolCallSignature(response.ToolCalls!);
            if (!seenToolCallSignatures.Add(signature))
            {
                return new AgentResponse(BuildToolLoopFallbackResponse(context));
            }

            // Insert the assistant message that requested tool calls so the model
            // can correlate tool_call_id values in subsequent tool result messages.
            context.WorkingMessages.Add(new ChatMessage(
                ChatRole.Assistant,
                response.Content,
                ToolCalls: response.ToolCalls));

            foreach (var toolCall in response.ToolCalls!)
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

            context.WorkingMessages.Add(new ChatMessage(
                ChatRole.System,
                "Use the latest tool results to answer the user. Only call another tool if additional information is strictly required."));

            response = await handler(context, cancellationToken);
        }

        return response;
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

    public async ValueTask DisposeAsync()
    {
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
