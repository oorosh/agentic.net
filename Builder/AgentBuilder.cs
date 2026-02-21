using Agentic.Abstractions;
using Agentic.Core;
using Agentic.Middleware;

namespace Agentic.Builder;

public sealed class AgentBuilder
{
    private IModelProvider? _modelProvider;
    private IMemoryService? _memoryService;
    private IAssistantContextFactory? _contextFactory;
    private readonly List<IAssistantMiddleware> _middlewares = [];

    public AgentBuilder WithModelProvider(IModelProvider modelProvider)
    {
        _modelProvider = modelProvider;
        return this;
    }

    public AgentBuilder WithMemory(IMemoryService memoryService)
    {
        _memoryService = memoryService;
        return this;
    }

    public AgentBuilder WithContextFactory(IAssistantContextFactory contextFactory)
    {
        _contextFactory = contextFactory;
        return this;
    }

    public AgentBuilder UseMiddleware(IAssistantMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public Agent Build()
    {
        if (_modelProvider is null)
        {
            throw new InvalidOperationException("A model provider is required. Call WithModelProvider first.");
        }

        var pipeline = new List<IAssistantMiddleware>(_middlewares);

        if (_memoryService is not null && pipeline.All(middleware => middleware is not MemoryMiddleware))
        {
            pipeline.Insert(0, new MemoryMiddleware(_memoryService));
        }

        return new Agent(
            _modelProvider.CreateModel(),
            _memoryService,
            _contextFactory ?? new DefaultAssistantContextFactory(),
            pipeline);
    }
}
