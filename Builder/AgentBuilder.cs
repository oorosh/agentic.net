using System.Reflection;
using Agentic.Abstractions;
using Agentic.Core;
using Agentic.Loaders;
using Agentic.Middleware;
using Agentic.Providers.OpenAi;

namespace Agentic.Builder;

public sealed class AgentBuilder
{
    private IModelProvider? _modelProvider;
    // Deferred OpenAI config so tools can be auto-wired at Build() time
    private string? _openAiApiKey;
    private string? _openAiModel;
    private OpenAiProviderOptions? _openAiOptions;

    private IMemoryService? _memoryService;
    private IEmbeddingProvider? _embeddingProvider;
    private IVectorStore? _vectorStore;
    private IAssistantContextFactory? _contextFactory;
    private ISkillLoader? _skillLoader;
    private ISoulLoader? _soulLoader;
    private Func<string, string, SoulDocument, SoulDocument?>? _soulLearningCallback;
    private readonly List<IAssistantMiddleware> _middlewares = [];
    private readonly List<ITool> _tools = [];

    public AgentBuilder WithModelProvider(IModelProvider modelProvider)
    {
        _modelProvider = modelProvider;
        _openAiApiKey = null;
        _openAiModel = null;
        _openAiOptions = null;
        return this;
    }

    public AgentBuilder WithOpenAi(string apiKey, string model = OpenAiModels.Gpt4oMini)
    {
        _openAiApiKey = apiKey;
        _openAiModel = model;
        _openAiOptions = null;
        _modelProvider = null;
        return this;
    }

    public AgentBuilder WithOpenAi(
        string apiKey,
        Action<OpenAiProviderOptions> configure)
    {
        var options = new OpenAiProviderOptions();
        configure(options);
        _openAiApiKey = apiKey;
        _openAiModel = options.Model;
        _openAiOptions = options;
        _modelProvider = null;
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
    /// Configures a simple in-memory memory service (no persistence — suitable for development/testing).
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

    public AgentBuilder WithEmbeddingProvider(IEmbeddingProvider embeddingProvider)
    {
        _embeddingProvider = embeddingProvider;
        return this;
    }

    /// <summary>
    /// Convenience method to configure OpenAI embeddings using the default model (text-embedding-3-small).
    /// </summary>
    public AgentBuilder WithOpenAiEmbeddings(string apiKey, string model = "text-embedding-3-small")
    {
        _embeddingProvider = new OpenAiEmbeddingProvider(apiKey, model);
        return this;
    }

    /// <summary>
    /// Configures semantic memory with in-memory vector store using OpenAI embeddings.
    /// Automatically sets up <see cref="InMemoryMemoryService"/>, <see cref="InMemoryVectorStore"/>,
    /// and <see cref="OpenAiEmbeddingProvider"/> using the default text-embedding-3-small model.
    /// </summary>
    public AgentBuilder WithSemanticMemory(string openAiApiKey, string embeddingModel = "text-embedding-3-small")
    {
        _embeddingProvider = new OpenAiEmbeddingProvider(openAiApiKey, embeddingModel);
        // Dimensions will be resolved when InitializeAsync is called on the provider.
        // Use a lazy vector store that reads dimensions after initialization.
        _memoryService = new InMemoryMemoryService();
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
    /// <remarks>
    /// Decorate the tool's parameter properties with <see cref="Agentic.Abstractions.ToolParameterAttribute"/>
    /// so the framework can auto-generate a JSON schema for the LLM. Tools without any
    /// <c>[ToolParameter]</c> properties will emit a diagnostic warning at build time because the
    /// LLM will receive an empty schema and may not call the tool correctly.
    /// </remarks>
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
    /// <param name="assembly">The assembly to scan.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a discovered tool type does not have a public parameterless constructor.
    /// </exception>
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
    /// <typeparam name="TMarker">Any type in the assembly you want to scan.</typeparam>
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

    /// <summary>
    /// Returns the effective name for <paramref name="tool"/>, honouring any
    /// <see cref="AgenticToolAttribute.Name"/> override declared on the tool's type.
    /// Falls back to <see cref="ITool.Name"/> when the attribute is absent or its
    /// <c>Name</c> property is <see langword="null"/> or whitespace.
    /// </summary>
    private static string GetEffectiveToolName(ITool tool)
    {
        var attr = tool.GetType().GetCustomAttribute<AgenticToolAttribute>(inherit: false);
        return attr?.Name is { Length: > 0 } attrName ? attrName : tool.Name;
    }

    /// <summary>
    /// Returns the effective description for <paramref name="tool"/>, honouring any
    /// <see cref="AgenticToolAttribute.Description"/> override declared on the tool's type.
    /// Falls back to <see cref="ITool.Description"/> when the attribute is absent or its
    /// <c>Description</c> property is <see langword="null"/> or whitespace.
    /// </summary>
    private static string GetEffectiveToolDescription(ITool tool)
    {
        var attr = tool.GetType().GetCustomAttribute<AgenticToolAttribute>(inherit: false);
        return attr?.Description is { Length: > 0 } attrDesc ? attrDesc : tool.Description;
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
        // Resolve the model provider — either a custom one set via WithModelProvider,
        // or auto-build an OpenAI provider with auto-wired tool definitions.
        var modelProvider = ResolveModelProvider();

        if (_memoryService is null && _vectorStore is not null)
        {
            _memoryService = new SqliteMemoryService(_vectorStore);
        }

        var pipeline = new List<IAssistantMiddleware>(_middlewares);

        if (_memoryService is not null && pipeline.All(middleware => middleware is not MemoryMiddleware))
        {
            pipeline.Insert(0, new MemoryMiddleware(_memoryService, _embeddingProvider));
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
            modelProvider.CreateModel(),
            _memoryService,
            _embeddingProvider,
            _contextFactory ?? new DefaultAssistantContextFactory(),
            pipeline,
            toolLookup,
            _skillLoader,
            _soulLoader,
            _soulLearningCallback);
    }

    private IModelProvider ResolveModelProvider()
    {
        // Custom provider wins — user is responsible for its tool definitions.
        if (_modelProvider is not null)
            return _modelProvider;

        if (_openAiApiKey is null)
            throw new InvalidOperationException("A model provider is required. Call WithOpenAi or WithModelProvider first.");

        var model = _openAiModel ?? OpenAiModels.Gpt4oMini;

        // Start from any explicitly configured tool definitions.
        var explicitDefs = _openAiOptions?.Tools is { Count: > 0 }
            ? new List<OpenAiFunctionToolDefinition>(_openAiOptions.Tools)
            : new List<OpenAiFunctionToolDefinition>();

        var explicitNames = new HashSet<string>(explicitDefs.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);

        // Auto-generate definitions for tools not already covered by explicit defs.
        foreach (var tool in _tools)
        {
            var effectiveName = GetEffectiveToolName(tool);
            var effectiveDescription = GetEffectiveToolDescription(tool);

            if (explicitNames.Contains(effectiveName))
                continue;

            var parameters = ToolParameterMetadata.ExtractFromTool(tool.GetType());

            if (parameters.Count == 0)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"[Agentic] Tool '{effectiveName}' ({tool.GetType().Name}) has no [ToolParameter] properties. " +
                    $"The LLM will receive an empty parameter schema and may not call the tool correctly. " +
                    $"Add [ToolParameter] attributes to the tool's properties, or pass an explicit OpenAiFunctionToolDefinition via WithOpenAi(key, options => options.Tools = ...).");
            }

            var openAiParams = parameters
                .Select(p => new OpenAiFunctionToolParameter(
                    p.Name,
                    MapToJsonType(p.ParameterType),
                    p.Description,
                    p.Required))
                .ToList();

            explicitDefs.Add(new OpenAiFunctionToolDefinition(effectiveName, effectiveDescription, openAiParams));
        }

        return new OpenAiChatModelProvider(_openAiApiKey, model, explicitDefs);
    }

    private static string MapToJsonType(Type type)
    {
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(bool))
            return "boolean";
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            return "array";
        return "string";
    }
}
