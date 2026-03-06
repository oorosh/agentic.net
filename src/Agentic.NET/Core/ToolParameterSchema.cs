using System.Text.Json;
using System.Text.Json.Serialization;
using Agentic.Abstractions;

namespace Agentic.Core;

/// <summary>
/// Represents a JSON schema for tool parameters that can be sent to LLMs.
/// </summary>
public sealed class ToolParameterSchema
{
    /// <summary>
    /// Tool name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tool description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Parameters schema object.
    /// </summary>
    [JsonPropertyName("parameters")]
    public ParametersSchema Parameters { get; set; } = new();

    /// <summary>
    /// Generates a JSON schema from tool parameters.
    /// </summary>
    public static ToolParameterSchema FromTool(ITool tool, IReadOnlyList<IToolParameterMetadata>? parameters = null)
    {
        parameters ??= ToolParameterMetadata.ExtractFromTool(tool.GetType());

        var properties = new Dictionary<string, JsonElement>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            var propSchema = BuildPropertySchema(param);
            var element = JsonSerializer.SerializeToElement(propSchema, SerializerOptions);
            properties[param.Name] = element;

            if (param.Required)
            {
                required.Add(param.Name);
            }
        }

        return new ToolParameterSchema
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = new ParametersSchema
            {
                Type = "object",
                Properties = properties,
                Required = required.Count > 0 ? required : null
            }
        };
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions IndentedSerializerOptions = new() { WriteIndented = true };

    private static Dictionary<string, object?> BuildPropertySchema(IToolParameterMetadata param)
    {
        var schema = new Dictionary<string, object?>();

        var jsonType = GetJsonType(param.ParameterType);
        schema["type"] = jsonType;

        if (!string.IsNullOrEmpty(param.Description))
        {
            schema["description"] = param.Description;
        }

        if (param.Enum is not null && param.Enum.Length > 0)
        {
            schema["enum"] = param.Enum;
        }

        if (!string.IsNullOrEmpty(param.Pattern))
        {
            schema["pattern"] = param.Pattern;
        }

        if (param.Minimum.HasValue)
        {
            schema["minimum"] = param.Minimum;
        }

        if (param.Maximum.HasValue)
        {
            schema["maximum"] = param.Maximum;
        }

        if (param.MinLength.HasValue)
        {
            schema["minLength"] = param.MinLength;
        }

        if (param.MaxLength.HasValue)
        {
            schema["maxLength"] = param.MaxLength;
        }

        if (param.DefaultValue is not null)
        {
            schema["default"] = param.DefaultValue;
        }

        return schema;
    }

    private static string GetJsonType(Type type)
    {
        if (type == typeof(string))
            return "string";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(bool))
            return "boolean";
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            return "array";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return "object";

        return "string";
    }

    /// <summary>
    /// Converts schema to JSON string for LLM consumption.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, IndentedSerializerOptions);
}

/// <summary>
/// Parameters schema container.
/// </summary>
public sealed class ParametersSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, JsonElement> Properties { get; set; } = [];

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }
}
