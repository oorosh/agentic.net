namespace Agentic.Core;

/// <summary>
/// Marks a class as a tool that should be automatically discovered and registered
/// with the agent when <see cref="Agentic.Builder.AgentBuilder.WithToolsFromAssembly"/> is called.
/// </summary>
/// <remarks>
/// The class must also implement <see cref="Agentic.Abstractions.ITool"/>.
/// If <see cref="Name"/> or <see cref="Description"/> are set on the attribute they override
/// the values returned by <see cref="Agentic.Abstractions.ITool.Name"/> and
/// <see cref="Agentic.Abstractions.ITool.Description"/> respectively.
///
/// <code>
/// [AgenticTool]
/// public sealed class WeatherTool : ITool
/// {
///     public string Name =&gt; "get_weather";
///     public string Description =&gt; "Returns the current weather for a city.";
///
///     [ToolParameter("city", "City to look up", required: true)]
///     public string City { get; set; } = string.Empty;
///
///     public Task&lt;string&gt; InvokeAsync(string arguments, CancellationToken ct = default)
///         =&gt; Task.FromResult($"Weather in {City}: sunny, 22°C.");
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgenticToolAttribute : Attribute
{
    /// <summary>
    /// Optional override for the tool name exposed to the LLM.
    /// When <see langword="null"/> (default), <see cref="Agentic.Abstractions.ITool.Name"/> is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional override for the tool description exposed to the LLM.
    /// When <see langword="null"/> (default), <see cref="Agentic.Abstractions.ITool.Description"/> is used.
    /// </summary>
    public string? Description { get; set; }
}
