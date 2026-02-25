using Agentic.Abstractions;
using Agentic.Loaders;
using Agentic.Middleware;

namespace Agentic.Core;

public sealed class Agent
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
            context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, BuildToolCatalogMessage(_tools.Values)));
        }

        AgentHandler handler = async (ctx, ct) => await _model.CompleteAsync(ctx.WorkingMessages.ToList(), ct);

        foreach (var middleware in _middlewares.Reverse())
        {
            var next = handler;
            handler = (ctx, ct) => middleware.InvokeAsync(ctx, next, ct);
        }

        var response = await handler(context, cancellationToken);
        var toolCallDepth = 0;
        var seenToolCallSignatures = new HashSet<string>(StringComparer.Ordinal);

        while (response.HasToolCalls)
        {
            if (toolCallDepth++ >= MaxToolCallDepth)
            {
                response = new AgentResponse(BuildToolLoopFallbackResponse(context));
                break;
            }

            var signature = BuildToolCallSignature(response.ToolCalls!);
            if (!seenToolCallSignatures.Add(signature))
            {
                response = new AgentResponse(BuildToolLoopFallbackResponse(context));
                break;
            }

            foreach (var toolCall in response.ToolCalls!)
            {
                if (!_tools.TryGetValue(toolCall.Name, out var tool))
                {
                    throw new InvalidOperationException($"Tool '{toolCall.Name}' is not registered.");
                }

                var toolResult = await tool.InvokeAsync(toolCall.Arguments, cancellationToken);
                context.WorkingMessages.Add(new ChatMessage(ChatRole.Tool, $"{toolCall.Name}: {toolResult}"));
            }

            context.WorkingMessages.Add(new ChatMessage(
                ChatRole.System,
                "Use the latest tool results to answer the user. Only call another tool if additional information is strictly required."));

            response = await handler(context, cancellationToken);
        }

        var userMessage = new ChatMessage(ChatRole.User, input);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, response.Content);

        _history.Add(userMessage);
        _history.Add(assistantMessage);

        if (_memoryService is not null)
        {
            var userId = Guid.NewGuid().ToString("N");
            await _memoryService.StoreMessageAsync(userId, input, cancellationToken);
            var responseId = Guid.NewGuid().ToString("N");
            await _memoryService.StoreMessageAsync(responseId, response.Content, cancellationToken);

            if (_embeddingProvider is not null)
            {
                var userEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(input, cancellationToken);
                await _memoryService.StoreEmbeddingAsync(userId, userEmbedding, cancellationToken);
                var responseEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(response.Content, cancellationToken);
                await _memoryService.StoreEmbeddingAsync(responseId, responseEmbedding, cancellationToken);
            }
        }

        return response.Content;
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
        var lastToolMessage = context.WorkingMessages.LastOrDefault(m => m.Role == ChatRole.Tool)?.Content;
        if (string.IsNullOrWhiteSpace(lastToolMessage))
        {
            return "I couldn't complete this request because tool calling kept repeating. Please try rephrasing your question.";
        }

        var separatorIndex = lastToolMessage.IndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < lastToolMessage.Length - 1)
        {
            var toolResult = lastToolMessage[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(toolResult))
            {
                return toolResult;
            }
        }

        return lastToolMessage;
    }
}
