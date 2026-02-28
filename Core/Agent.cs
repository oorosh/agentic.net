using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Agentic.Abstractions;
using Agentic.Loaders;
using Agentic.Middleware;

namespace Agentic.Core;

public sealed class Agent : IAsyncDisposable
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
    private readonly List<ChatMessage> _history = [];
    // Fix 3: cache ToolParameterMetadata per tool type to avoid repeated reflection
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<IToolParameterMetadata>> ParameterMetadataCache = new();
    // Fix 14: cache the tool catalog string (built once since tools don't change after construction)
    private string? _toolCatalogMessage;
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
        ISoulLoader? soulLoader = null)
    {
        _model = model;
        _memoryService = memoryService;
        _embeddingProvider = embeddingProvider;
        _contextFactory = contextFactory;
        _middlewares = middlewares;
        _tools = tools;
        _skillLoader = skillLoader;
        _soulLoader = soulLoader;
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

        _initialized = true;
    }

    public async Task UpdateSoulAsync(CancellationToken cancellationToken = default)
    {
        if (_soulLoader is not null)
        {
            _soul = await _soulLoader.ReloadSoulAsync(cancellationToken);
        }
    }

    public async Task UpdateSoulAsync(SoulDocument updatedSoul, CancellationToken cancellationToken = default)
    {
        _soul = updatedSoul;
        
        if (_soulLoader is IPersistentSoulLoader persistentLoader)
        {
            await persistentLoader.UpdateSoulAsync(updatedSoul, cancellationToken);
        }
    }

    public async Task<string> ReplyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        var context = _contextFactory.Create(input, _history);

        if (_tools.Count > 0)
        {
            _toolCatalogMessage ??= BuildToolCatalogMessage(_tools.Values);
            context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, _toolCatalogMessage));
        }

        var handler = BuildPipeline();
        var response = await RunToolLoopAsync(context, handler, cancellationToken);

        var userMessage = new ChatMessage(ChatRole.User, input);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, response.Content);

        _history.Add(userMessage);
        _history.Add(assistantMessage);

        await PersistToMemoryAsync(input, response.Content, cancellationToken);

        return response.Content;
    }

    private AgentHandler BuildPipeline()
    {
        AgentHandler handler = async (ctx, ct) => await _model.CompleteAsync(ctx.WorkingMessages.ToList(), ct);

        foreach (var middleware in _middlewares.Reverse())
        {
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

            foreach (var toolCall in response.ToolCalls!)
            {
                if (!_tools.TryGetValue(toolCall.Name, out var tool))
                {
                    context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, $"Error - Tool '{toolCall.Name}' is not registered.", toolCall.Name));
                    continue;
                }

                // Bind structured parameters if the tool has them
                var parameters = ParameterMetadataCache.GetOrAdd(tool.GetType(), t => ToolParameterMetadata.ExtractFromTool(tool));
                if (parameters.Count > 0)
                {
                    try
                    {
                        ToolParameterBinder.BindParameters(tool, toolCall.Arguments, parameters);
                    }
                    catch (Exception ex)
                    {
                        context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, $"Error - {ex.Message}", toolCall.Name));
                        continue;
                    }
                }

                try
                {
                    var toolResult = await tool.InvokeAsync(toolCall.Arguments, cancellationToken);
                    context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, toolResult, toolCall.Name));
                }
                catch (Exception ex)
                {
                    context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, $"Error - {ex.Message}", toolCall.Name));
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
            var parameters = ParameterMetadataCache.GetOrAdd(tool.GetType(), t => ToolParameterMetadata.ExtractFromTool(tool));
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
