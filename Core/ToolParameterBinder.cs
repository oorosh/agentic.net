namespace Agentic.Core;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agentic.Abstractions;

/// <summary>
/// Handles validation and parsing of tool parameters from JSON arguments.
/// Provides automatic type conversion, constraint validation, and error handling.
/// </summary>
public sealed class ToolParameterBinder
{
    /// <summary>
    /// Parses and binds JSON arguments to tool properties based on parameter metadata.
    /// Validates all constraints and applies default values.
    /// </summary>
    public static void BindParameters(object tool, string arguments, IReadOnlyList<IToolParameterMetadata> parameters)
    {
        if (parameters.Count == 0)
            return;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(arguments);
            var root = doc.RootElement;

            foreach (var param in parameters)
            {
                var property = tool.GetType().GetProperty(param.Name);
                if (property is null)
                    continue;

                // Try to get the value from JSON
                var hasValue = root.TryGetProperty(param.Name, out var element);

                if (!hasValue)
                {
                    if (param.Required)
                    {
                        throw new InvalidOperationException(
                            $"Required parameter '{param.Name}' is missing from tool arguments.");
                    }

                    // Apply default value if available
                    if (param.DefaultValue is not null)
                    {
                        property.SetValue(tool, param.DefaultValue);
                    }

                    continue;
                }

                // Parse and validate the value
                var value = ParseValue(element, param);
                ValidateValue(value, param);

                property.SetValue(tool, value);
            }
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private static object? ParseValue(JsonElement element, IToolParameterMetadata param)
    {
        // Handle null
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (param.Required)
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' cannot be null.");
            }
            return null;
        }

        var targetType = param.ParameterType;

        // String
        if (targetType == typeof(string))
        {
            return element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();
        }

        // Numeric types
        if (targetType == typeof(int))
            return element.GetInt32();
        if (targetType == typeof(long))
            return element.GetInt64();
        if (targetType == typeof(double))
            return element.GetDouble();
        if (targetType == typeof(float))
            return element.GetSingle();
        if (targetType == typeof(decimal))
            return element.GetDecimal();
        if (targetType == typeof(short))
            return element.GetInt16();
        if (targetType == typeof(byte))
            return element.GetByte();

        // Boolean
        if (targetType == typeof(bool))
            return element.GetBoolean();

        // Array types
        if (targetType.IsArray || (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>)))
        {
            if (element.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"Parameter '{param.Name}' must be an array.");

            var elementType = targetType.IsArray
                ? targetType.GetElementType()!
                : targetType.GetGenericArguments()[0];

            var list = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(elementType))!;

            foreach (var item in element.EnumerateArray())
            {
                list.Add(Convert.ChangeType(item.GetRawText(), elementType, CultureInfo.InvariantCulture));
            }

            if (targetType.IsArray)
            {
                var array = Array.CreateInstance(elementType, list.Count);
                list.CopyTo(array, 0);
                return array;
            }

            return list;
        }

        throw new InvalidOperationException(
            $"Unsupported parameter type '{targetType.Name}' for '{param.Name}'.");
    }

    private static void ValidateValue(object? value, IToolParameterMetadata param)
    {
        if (value is null)
            return;

        // Enum validation
        if (param.Enum is not null && param.Enum.Length > 0)
        {
            var stringValue = value.ToString();
            if (!param.Enum.Contains(stringValue))
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' must be one of: {string.Join(", ", param.Enum)}. Got: {stringValue}");
            }
        }

        // Pattern validation
        if (!string.IsNullOrEmpty(param.Pattern) && value is string stringVal)
        {
            if (!Regex.IsMatch(stringVal, param.Pattern))
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' does not match required pattern: {param.Pattern}");
            }
        }

        // Numeric range validation
        if (value is IComparable comparable)
        {
            if (param.Minimum.HasValue)
            {
                var minVal = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (minVal < param.Minimum.Value)
                {
                    throw new InvalidOperationException(
                        $"Parameter '{param.Name}' must be >= {param.Minimum}. Got: {value}");
                }
            }

            if (param.Maximum.HasValue)
            {
                var maxVal = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (maxVal > param.Maximum.Value)
                {
                    throw new InvalidOperationException(
                        $"Parameter '{param.Name}' must be <= {param.Maximum}. Got: {value}");
                }
            }
        }

        // String length validation
        if (value is string str)
        {
            if (param.MinLength.HasValue && str.Length < param.MinLength)
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' must be at least {param.MinLength} characters. Got: {str.Length}");
            }

            if (param.MaxLength.HasValue && str.Length > param.MaxLength)
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' must be at most {param.MaxLength} characters. Got: {str.Length}");
            }
        }

        // Array length validation
        if (value is System.Collections.ICollection collection)
        {
            if (param.MinLength.HasValue && collection.Count < param.MinLength)
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' must have at least {param.MinLength} items. Got: {collection.Count}");
            }

            if (param.MaxLength.HasValue && collection.Count > param.MaxLength)
            {
                throw new InvalidOperationException(
                    $"Parameter '{param.Name}' must have at most {param.MaxLength} items. Got: {collection.Count}");
            }
        }
    }
}
