using System.Reflection;
using Agentic.Abstractions;
using Agentic.Core;
using Agentic.Loaders;
using Agentic.Middleware;
using Microsoft.Extensions.AI;

namespace Agentic.Builder;

public sealed class AgentBuilder
{
    private IChatClient? _chatClient;
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;

    private IMemoryService? _memoryService;
    private IVectorStore? _vectorStore;
    private IAssistantContextFactory? _contextFactory;
    private ISkillLoader? _skillLoader;
    private ISoulLoader? _soulLoader;
    private Func<string, string, SoulDocument, SoulDocument?>? _soulLearningCallback;
    private HeartbeatOptions? _heartbeatOptions;
    private readonly List<IAssistantMiddleware> _middlewares = [];
    private readonly List<ITool> _tools = [];

    public AgentBuilder WithChatClient(IChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        return this;
    }

    public AgentBuilder WithEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        return this;
    }

    public AgentBuilder WithMemory(IMemoryService memoryService)
    {
        _memoryService = memoryService;
        return this;
    }

    public AgentBuilder WithMemory(string dbPath, IVectorStore? vectorStore = null)
    {
        _memoryService = new SqliteMemoryService(dbPath, vectorStore);
        return this;
    }

    /// <summary>
    /// Configures a simple in-memory memory service (no persistence - suitable for development/testing).
    /// </summary>
    public AgentBuilder WithInMemoryMemory()
    {
        _memoryService = new InMemoryMemoryService();
        return this;
    }

    public AgentBuilder WithVectorStore(IVectorStore vectorStore)
    {
        _vectorStore = vectorStore;
        return this;
    }

    public AgentBuilder WithContextFactory(IAssistantContextFactory contextFactory)
    {
        _contextFactory = contextFactory;
        return this;
    }

    public AgentBuilder WithSkills()
    {
        _skillLoader = new FileSystemSkillLoader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skills"));
        return this;
    }

    public AgentBuilder WithSkills(string skillsDirectory)
    {
        _skillLoader = new FileSystemSkillLoader(skillsDirectory);
        return this;
    }

    public AgentBuilder WithSkills(ISkillLoader skillLoader)
    {
        _skillLoader = skillLoader;
        return this;
    }

    public AgentBuilder WithSoul()
    {
        _soulLoader = new FileSystemSoulLoader(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SOUL.md"));
        return this;
    }

    public AgentBuilder WithSoul(string soulFilePath)
    {
        _soulLoader = new FileSystemSoulLoader(soulFilePath);
        return this;
    }

    public AgentBuilder WithSoul(DirectoryInfo directory)
    {
        _soulLoader = new FileSystemSoulLoader(directory);
        return this;
    }

    public AgentBuilder WithSoul(ISoulLoader soulLoader)
    {
        _soulLoader = soulLoader;
        return this;
    }

    public AgentBuilder WithSoulLearning(Func<string, string, SoulDocument, SoulDocument?> callback)
    {
        _soulLearningCallback = callback;
        return this;
    }

    /// <summary>
    /// Enables proactive heartbeat ticks using the default options (5-minute interval).
    /// </summary>
    public AgentBuilder WithHeartbeat()
    {
        _heartbeatOptions = new HeartbeatOptions();
        return this;
    }

    /// <summary>
    /// Enables proactive heartbeat ticks with the specified interval and optional custom prompt.
    /// </summary>
    public AgentBuilder WithHeartbeat(TimeSpan interval, string? prompt = null)
    {
        _heartbeatOptions = new HeartbeatOptions { Interval = interval, Prompt = prompt };
        return this;
    }

    /// <summary>
    /// Enables proactive heartbeat ticks with fully customisable options.
    /// </summary>
    public AgentBuilder WithHeartbeat(HeartbeatOptions options)
    {
        _heartbeatOptions = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    /// Enables proactive heartbeat ticks, configuring options via a callback.
    /// </summary>
    public AgentBuilder WithHeartbeat(Action<HeartbeatOptions> configure)
    {
        var options = new HeartbeatOptions();
        configure(options);
        _heartbeatOptions = options;
        return this;
    }

    /// <summary>
    /// Adds a middleware to the pipeline.
    /// </summary>
    public AgentBuilder WithMiddleware(IAssistantMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Adds multiple middlewares to the pipeline.
    /// </summary>
    public AgentBuilder WithMiddlewares(IEnumerable<IAssistantMiddleware> middlewares)
    {
        _middlewares.AddRange(middlewares);
        return this;
    }

    /// <summary>
    /// Adds a middleware to the pipeline.
    /// </summary>
    [Obsolete("Use WithMiddleware instead.")]
    public AgentBuilder UseMiddleware(IAssistantMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Registers a tool the model can invoke during a conversation.
    /// </summary>
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

    /// <summary>
    /// Scans <paramref name="assembly"/> for all concrete, non-abstract classes that implement
    /// <see cref="ITool"/> and are decorated with <see cref="AgenticToolAttribute"/>, instantiates
    /// them using their public parameterless constructor, and registers each one as a tool.
    /// </summary>
    public AgentBuilder WithToolsFromAssembly(Assembly assembly)
    {
        foreach (var type in DiscoverToolTypes(assembly))
        {
            var instance = CreateToolInstance(type);
            _tools.Add(instance);
        }
        return this;
    }

    /// <summary>
    /// Scans the assembly that contains <typeparamref name="TMarker"/> for all concrete, non-abstract
    /// classes that implement <see cref="ITool"/> and are decorated with <see cref="AgenticToolAttribute"/>,
    /// instantiates them, and registers each one as a tool.
    /// </summary>
    public AgentBuilder WithToolsFromAssembly<TMarker>()
        => WithToolsFromAssembly(typeof(TMarker).Assembly);

    /// <summary>
    /// Scans the assembly of the calling code for all concrete, non-abstract classes that implement
    /// <see cref="ITool"/> and are decorated with <see cref="AgenticToolAttribute"/>,
    /// instantiates them, and registers each one as a tool.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public AgentBuilder WithToolsFromCallingAssembly()
        => WithToolsFromAssembly(Assembly.GetCallingAssembly());

    private static string GetEffectiveToolName(ITool tool)
    {
        var attr = tool.GetType().GetCustomAttribute<AgenticToolAttribute>(inherit: false);
        return attr?.Name is { Length: > 0 } attrName ? attrName : tool.Name;
    }

    private static IEnumerable<Type> DiscoverToolTypes(Assembly assembly)
    {
        var toolInterface = typeof(ITool);
        var markerAttribute = typeof(AgenticToolAttribute);

        return assembly.GetTypes()
            .Where(t =>
                t.IsClass &&
                !t.IsAbstract &&
                toolInterface.IsAssignableFrom(t) &&
                t.IsDefined(markerAttribute, inherit: false));
    }

    private static ITool CreateToolInstance(Type type)
    {
        var ctor = type.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"Tool type '{type.FullName}' is decorated with [AgenticTool] but does not have a public " +
                $"parameterless constructor. Add a public parameterless constructor or register the tool " +
                $"manually using WithTool(new {type.Name}(...)).");

        return (ITool)ctor.Invoke(null);
    }

    public IAgent Build()
    {
        if (_chatClient is null)
            throw new InvalidOperationException("A chat client is required. Call WithChatClient(IChatClient) first.");

        var model = new ChatClientAgentModel(_chatClient);

        if (_memoryService is null && _vectorStore is not null)
        {
            _memoryService = new SqliteMemoryService(_vectorStore);
        }

        var pipeline = new List<IAssistantMiddleware>(_middlewares);

        if (_memoryService is not null && pipeline.All(middleware => middleware is not MemoryMiddleware))
        {
            pipeline.Insert(0, new MemoryMiddleware(_memoryService, _embeddingGenerator));
        }

        var toolLookup = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _tools)
        {
            var effectiveName = GetEffectiveToolName(tool);
            if (!toolLookup.TryAdd(effectiveName, tool))
            {
                throw new InvalidOperationException($"Tool '{effectiveName}' is registered more than once.");
            }
        }

        return new Agent(
            model,
            _memoryService,
            _embeddingGenerator,
            _contextFactory ?? new DefaultAssistantContextFactory(),
            pipeline,
            toolLookup,
            _skillLoader,
            _soulLoader,
            _soulLearningCallback,
            _heartbeatOptions);
    }
}
