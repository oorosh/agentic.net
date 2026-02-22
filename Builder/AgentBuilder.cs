using Agentic.Abstractions;
using Agentic.Core;
using Agentic.Middleware;
using Agentic.Providers.OpenAi;

namespace Agentic.Builder;

public sealed class AgentBuilder
{
    private IModelProvider? _modelProvider;
    private IMemoryService? _memoryService;
    private IAssistantContextFactory? _contextFactory;
    private readonly List<IAssistantMiddleware> _middlewares = [];
    private readonly List<ITool> _tools = [];

    public AgentBuilder WithModelProvider(IModelProvider modelProvider)
    {
        _modelProvider = modelProvider;
        return this;
    }

    public AgentBuilder WithOpenAi(
        string apiKey,
        string model = OpenAiModels.Gpt4oMini,
        IReadOnlyList<OpenAiFunctionToolDefinition>? tools = null)
    {
        _modelProvider = new OpenAiChatModelProvider(apiKey, model, tools);
        return this;
    }

    public AgentBuilder WithOpenAi(
        string apiKey,
        Action<OpenAiProviderOptions> configure)
    {
        var options = new OpenAiProviderOptions();
        configure(options);
        _modelProvider = new OpenAiChatModelProvider(apiKey, options.Model, options.Tools);
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

    public AgentBuilder WithTool(ITool tool)
    {
        _tools.Add(tool);
        return this;
    }

    public AgentBuilder WithTools(IEnumerable<ITool> tools)
    {
        _tools.AddRange(tools);
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

        var toolLookup = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _tools)
        {
            if (!toolLookup.TryAdd(tool.Name, tool))
            {
                throw new InvalidOperationException($"Tool '{tool.Name}' is registered more than once.");
            }
        }

        return new Agent(
            _modelProvider.CreateModel(),
            _memoryService,
            _contextFactory ?? new DefaultAssistantContextFactory(),
            pipeline,
            toolLookup);
    }
}
