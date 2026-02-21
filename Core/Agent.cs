using Agentic.Abstractions;
using Agentic.Middleware;

namespace Agentic.Core;

public sealed class Agent
{
    private readonly IAgentModel _model;
    private readonly IMemoryService? _memoryService;
    private readonly IAssistantContextFactory _contextFactory;
    private readonly IReadOnlyList<IAssistantMiddleware> _middlewares;
    private readonly IReadOnlyDictionary<string, ITool> _tools;
    private readonly List<ChatMessage> _history = [];
    private bool _initialized;

    internal Agent(
        IAgentModel model,
        IMemoryService? memoryService,
        IAssistantContextFactory contextFactory,
        IReadOnlyList<IAssistantMiddleware> middlewares,
        IReadOnlyDictionary<string, ITool> tools)
    {
        _model = model;
        _memoryService = memoryService;
        _contextFactory = contextFactory;
        _middlewares = middlewares;
        _tools = tools;
    }

    public IReadOnlyList<ChatMessage> History => _history;

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

        _initialized = true;
    }

    public async Task<string> ReplyAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }

        var context = _contextFactory.Create(input, _history);

        AgentHandler handler = async (ctx, ct) => await _model.CompleteAsync(ctx.WorkingMessages.ToList(), ct);

        foreach (var middleware in _middlewares.Reverse())
        {
            var next = handler;
            handler = (ctx, ct) => middleware.InvokeAsync(ctx, next, ct);
        }

        var response = await handler(context, cancellationToken);
        var toolCallDepth = 0;

        while (response.HasToolCalls)
        {
            if (toolCallDepth++ >= 8)
            {
                throw new InvalidOperationException("Tool call depth exceeded the maximum allowed iterations.");
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

            response = await handler(context, cancellationToken);
        }

        var userMessage = new ChatMessage(ChatRole.User, input);
        var assistantMessage = new ChatMessage(ChatRole.Assistant, response.Content);

        _history.Add(userMessage);
        _history.Add(assistantMessage);

        if (_memoryService is not null)
        {
            await _memoryService.StoreMessageAsync(Guid.NewGuid().ToString("N"), input, cancellationToken);
            await _memoryService.StoreMessageAsync(Guid.NewGuid().ToString("N"), response.Content, cancellationToken);
        }

        return response.Content;
    }
}
