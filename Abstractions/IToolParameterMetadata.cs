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

/// <summary>
/// Default implementation of tool parameter metadata extracted from properties and attributes.
/// </summary>
public sealed class ToolParameterMetadata : IToolParameterMetadata
{
    internal ToolParameterMetadata(
        string name,
        Type parameterType,
        string description,
        bool required,
        object? defaultValue,
        string[]? @enum,
        string? pattern,
        double? minimum,
        double? maximum,
        int? minLength,
        int? maxLength)
    {
        Name = name;
        ParameterType = parameterType;
        Description = description;
        Required = required;
        DefaultValue = defaultValue;
        Enum = @enum;
        Pattern = pattern;
        Minimum = minimum;
        Maximum = maximum;
        MinLength = minLength;
        MaxLength = maxLength;
    }

    public string Name { get; }
    public Type ParameterType { get; }
    public string Description { get; }
    public bool Required { get; }
    public object? DefaultValue { get; }
    public string[]? Enum { get; }
    public string? Pattern { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public int? MinLength { get; }
    public int? MaxLength { get; }

    /// <summary>
    /// Extracts all tool parameters from a tool implementation using reflection.
    /// </summary>
    public static IReadOnlyList<IToolParameterMetadata> ExtractFromTool(object tool)
    {
        var parameters = new List<IToolParameterMetadata>();
        var type = tool.GetType();

        var properties = type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var property in properties)
        {
            var attr = property.GetCustomAttributes(typeof(ToolParameterAttribute), false)
                .FirstOrDefault() as ToolParameterAttribute;

            if (attr is not null)
            {
                // Convert attribute values to nullable based on sentinel values
                var minimum = !double.IsNegativeInfinity(attr.MinimumValue) ? (double?)attr.MinimumValue : null;
                var maximum = !double.IsPositiveInfinity(attr.MaximumValue) ? (double?)attr.MaximumValue : null;
                var minLength = attr.MinLengthValue >= 0 ? (int?)attr.MinLengthValue : null;
                var maxLength = attr.MaxLengthValue >= 0 ? (int?)attr.MaxLengthValue : null;

                var metadata = new ToolParameterMetadata(
                    property.Name,
                    property.PropertyType,
                    attr.Description,
                    attr.Required,
                    attr.DefaultValue,
                    attr.Enum,
                    attr.Pattern,
                    minimum,
                    maximum,
                    minLength,
                    maxLength);

                parameters.Add(metadata);
            }
        }

        return parameters.AsReadOnly();
    }
}
