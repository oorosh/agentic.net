namespace Agentic.Tests;

using Xunit;
using Agentic.Abstractions;
using Agentic.Core;

public sealed class StructuredToolParametersTests
{
    [Fact]
    public void ToolWithoutParameters_HasEmptyMetadata()
    {
        var tool = new SimpleStringTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        Assert.Empty(metadata);
    }

    [Fact]
    public void ToolWithParameters_ExtractsAllMetadata()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        
        Assert.Equal(2, metadata.Count);
        Assert.NotNull(metadata.FirstOrDefault(m => m.Name == "City"));
        Assert.NotNull(metadata.FirstOrDefault(m => m.Name == "Units"));
    }

    [Fact]
    public void ParameterMetadata_HasCorrectProperties()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        
        var cityParam = metadata.First(m => m.Name == "City");
        Assert.Equal("City", cityParam.Name);
        Assert.Equal(typeof(string), cityParam.ParameterType);
        Assert.True(cityParam.Required);
        Assert.Equal("City name for weather lookup", cityParam.Description);
    }

    [Fact]
    public void ParameterMetadata_WithEnum_ContainsEnumValues()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        
        var unitsParam = metadata.First(m => m.Name == "Units");
        Assert.NotNull(unitsParam.Enum);
        Assert.Equal(2, unitsParam.Enum.Length);
        Assert.Contains("C", unitsParam.Enum);
        Assert.Contains("F", unitsParam.Enum);
    }

    [Fact]
    public void ParameterMetadata_WithDefault_ContainsDefaultValue()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        
        var unitsParam = metadata.First(m => m.Name == "Units");
        Assert.Equal("C", unitsParam.DefaultValue);
    }

    [Fact]
    public void GenerateSchema_CreatesValidJsonSchema()
    {
        var tool = new WeatherTool();
        var schema = ToolParameterSchema.FromTool(tool);
        
        Assert.Equal("GetWeather", schema.Name);
        Assert.NotEmpty(schema.Description);
        Assert.NotNull(schema.Parameters);
        Assert.Equal("object", schema.Parameters.Type);
        Assert.Equal(2, schema.Parameters.Properties.Count);
        Assert.NotNull(schema.Parameters.Required);
        Assert.Single(schema.Parameters.Required);
    }

    [Fact]
    public void BindParameters_WithValidJson_SetsProperties()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"City": "London", "Units": "C"}""";
        
        ToolParameterBinder.BindParameters(tool, arguments, metadata);
        
        Assert.Equal("London", tool.City);
        Assert.Equal("C", tool.Units);
    }

    [Fact]
    public void BindParameters_WithMissingRequired_Throws()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"Units": "C"}""";
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToolParameterBinder.BindParameters(tool, arguments, metadata));
        Assert.Contains("City", ex.Message);
        Assert.Contains("required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BindParameters_WithInvalidEnum_Throws()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"City": "London", "Units": "K"}""";
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToolParameterBinder.BindParameters(tool, arguments, metadata));
        Assert.Contains("Units", ex.Message);
        Assert.Contains("C", ex.Message);
        Assert.Contains("F", ex.Message);
    }

    [Fact]
    public void BindParameters_WithMissingOptional_AppliesDefault()
    {
        var tool = new WeatherTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"City": "London"}""";
        
        ToolParameterBinder.BindParameters(tool, arguments, metadata);
        
        Assert.Equal("London", tool.City);
        Assert.Equal("C", tool.Units);
    }

    [Fact]
    public void BindParameters_WithNumericParameter_ParsesCorrectly()
    {
        var tool = new NumericParamTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"Count": 42, "Temperature": 98.6}""";
        
        ToolParameterBinder.BindParameters(tool, arguments, metadata);
        
        Assert.Equal(42, tool.Count);
        Assert.Equal(98.6, tool.Temperature);
    }

    [Fact]
    public void BindParameters_WithBooleanParameter_ParsesCorrectly()
    {
        var tool = new BooleanParamTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"Enabled": true}""";
        
        ToolParameterBinder.BindParameters(tool, arguments, metadata);
        
        Assert.True(tool.Enabled);
    }

    [Fact]
    public void BindParameters_WithMinConstraint_ValidatesLowerBound()
    {
        var tool = new ConstrainedTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"Value": 5}""";
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToolParameterBinder.BindParameters(tool, arguments, metadata));
        Assert.Contains("Value", ex.Message);
        Assert.Contains("10", ex.Message);
    }

    [Fact]
    public void BindParameters_WithMaxConstraint_ValidatesUpperBound()
    {
        var tool = new ConstrainedTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"Value": 105}""";
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToolParameterBinder.BindParameters(tool, arguments, metadata));
        Assert.Contains("Value", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public void BindParameters_WithinConstraints_Succeeds()
    {
        var tool = new ConstrainedTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"Value": 50}""";
        
        ToolParameterBinder.BindParameters(tool, arguments, metadata);
        
        Assert.Equal(50, tool.Value);
    }

    [Fact]
    public void BindParameters_WithStringLengthConstraint_ValidatesLength()
    {
        var tool = new StringConstrainedTool();
        var metadata = ToolParameterMetadata.ExtractFromTool(tool);
        var arguments = """{"NameParam": "a"}""";
        
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ToolParameterBinder.BindParameters(tool, arguments, metadata));
        Assert.Contains("NameParam", ex.Message);
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void SchemaGeneration_IncludesConstraints()
    {
        var tool = new ConstrainedTool();
        var schema = ToolParameterSchema.FromTool(tool);
        
        var json = schema.ToJson();
        Assert.Contains("minimum", json);
        Assert.Contains("maximum", json);
    }

    // Test helper tools
    private sealed class SimpleStringTool : ITool
    {
        public string Name => "simple";
        public string Description => "A simple tool";
        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(arguments);
    }

    private sealed class WeatherTool : ITool
    {
        public string Name => "GetWeather";
        public string Description => "Get weather for a city";

        [ToolParameter(Description = "City name for weather lookup", Required = true)]
        public string City { get; set; } = string.Empty;

        [ToolParameter(
            Description = "Temperature unit",
            Enum = new[] { "C", "F" },
            DefaultValue = "C")]
        public string Units { get; set; } = "C";

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult($"Weather for {City} in {Units}");
    }

    private sealed class NumericParamTool : ITool
    {
        public string Name => "numeric";
        public string Description => "Tool with numeric params";

        [ToolParameter(Description = "Item count", Required = true)]
        public int Count { get; set; }

        [ToolParameter(Description = "Temperature value", Required = true)]
        public double Temperature { get; set; }

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult($"Count: {Count}, Temp: {Temperature}");
    }

    private sealed class BooleanParamTool : ITool
    {
        public string Name => "boolean";
        public string Description => "Tool with boolean param";

        [ToolParameter(Description = "Enable feature", Required = true)]
        public bool Enabled { get; set; }

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(Enabled ? "Enabled" : "Disabled");
    }

    private sealed class ConstrainedTool : ITool
    {
        public string Name => "constrained";
        public string Description => "Tool with constraints";

        [ToolParameter(
            Description = "Value between 10 and 100",
            Required = true,
            MinimumValue = 10,
            MaximumValue = 100)]
        public int Value { get; set; }

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult($"Value: {Value}");
    }

    private sealed class StringConstrainedTool : ITool
    {
        public string Name => "stringconstrained";
        public string Description => "Tool with string constraints";

        [ToolParameter(
            Description = "Name with minimum length",
            Required = true,
            MinLengthValue = 3,
            MaxLengthValue = 20)]
        public string NameParam { get; set; } = string.Empty;

        public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
            => Task.FromResult($"Name: {NameParam}");
    }
}
