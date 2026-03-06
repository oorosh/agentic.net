using System.Reflection;
using Agentic.Abstractions;

namespace Agentic.Core;

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
    /// Extracts all tool parameters from a tool type using reflection.
    /// </summary>
    public static IReadOnlyList<IToolParameterMetadata> ExtractFromTool(Type type)
    {
        var parameters = new List<IToolParameterMetadata>();

        var properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

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
