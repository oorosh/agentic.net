namespace Agentic.Core;

/// <summary>
/// Attribute for defining structured parameters on tool properties.
/// Enables type-safe parameter handling with automatic validation and schema generation.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ToolParameterAttribute : Attribute
{
    /// <summary>
    /// Human-readable description of the parameter.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this parameter is required for tool invocation.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Default value for the parameter if not provided.
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Allowed enum values for the parameter.
    /// </summary>
    public string[]? Enum { get; set; }

    /// <summary>
    /// Regular expression pattern for string validation.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Minimum value for numeric parameters.
    /// </summary>
    public double MinimumValue { get; set; } = double.NegativeInfinity;

    /// <summary>
    /// Maximum value for numeric parameters.
    /// </summary>
    public double MaximumValue { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// Minimum length for string or array parameters.
    /// </summary>
    public int MinLengthValue { get; set; } = -1;

    /// <summary>
    /// Maximum length for string or array parameters.
    /// </summary>
    public int MaxLengthValue { get; set; } = -1;
}
