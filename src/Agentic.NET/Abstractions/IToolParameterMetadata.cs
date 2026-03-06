namespace Agentic.Abstractions;

/// <summary>
/// Represents metadata about a tool parameter including its type, constraints, and validation rules.
/// </summary>
public interface IToolParameterMetadata
{
    /// <summary>
    /// The name of the parameter property.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The CLR type of the parameter.
    /// </summary>
    Type ParameterType { get; }

    /// <summary>
    /// Human-readable description of the parameter.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether this parameter is required.
    /// </summary>
    bool Required { get; }

    /// <summary>
    /// Default value if not provided.
    /// </summary>
    object? DefaultValue { get; }

    /// <summary>
    /// Allowed enum values (null if not restricted).
    /// </summary>
    string[]? Enum { get; }

    /// <summary>
    /// Regex pattern for validation (null if not used).
    /// </summary>
    string? Pattern { get; }

    /// <summary>
    /// Minimum value for numeric types (null if not used).
    /// </summary>
    double? Minimum { get; }

    /// <summary>
    /// Maximum value for numeric types (null if not used).
    /// </summary>
    double? Maximum { get; }

    /// <summary>
    /// Minimum length for string/array types (null if not used).
    /// </summary>
    int? MinLength { get; }

    /// <summary>
    /// Maximum length for string/array types (null if not used).
    /// </summary>
    int? MaxLength { get; }
}
