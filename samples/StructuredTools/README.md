# Structured Tools Sample

This sample demonstrates the **Structured Tool Parameters** feature in Agentic.NET, which enables type-safe, validated tool arguments with automatic JSON schema generation.

## Overview

Traditional tool calling in LLMs requires:
1. Tools to manually parse JSON arguments
2. Tools to implement their own validation logic
3. LLMs to guess at parameter requirements without clear schema

Structured tool parameters solve this by:
- **Defining parameters as properties** with attributes for metadata
- **Automatic parsing and validation** before tool invocation
- **Clear JSON schema** sent to the LLM so it knows what to send
- **Type-safe access** to validated parameters

## Example Tools

This sample includes three example tools:

### 1. CalculatorTool
Demonstrates basic structured parameters:
```csharp
[ToolParameter(Description = "The operation: add, subtract, multiply, or divide", Required = true)]
public string Operation { get; set; } = "";

[ToolParameter(Description = "First number", Required = true)]
public double A { get; set; }
```

**Features:**
- Required parameters
- Multiple data types (string, double)
- Simple parameter binding

### 2. HotelSearchTool
Demonstrates numeric validation constraints:
```csharp
[ToolParameter(Description = "Number of nights", Required = true, MinimumValue = 1, MaximumValue = 30)]
public int Nights { get; set; }

[ToolParameter(Description = "Maximum price per night", Required = true, MinimumValue = 0)]
public double MaxPrice { get; set; }
```

**Features:**
- Minimum/maximum value validation
- Integer and decimal types
- Realistic business logic constraints

### 3. HotelBookingTool
Demonstrates string validation and enum constraints:
```csharp
[ToolParameter(Description = "Guest name", Required = true, MinLengthValue = 2, MaxLengthValue = 100)]
public string GuestName { get; set; } = "";

[ToolParameter(Description = "Room type", Required = true, Enum = ["single", "double", "suite"])]
public string RoomType { get; set; } = "";
```

**Features:**
- String length validation
- Enum (enumeration) validation
- Complex business rules

## Running the Sample

```bash
# Set your OpenAI API key
export OPENAI_API_KEY=sk_...
export OPENAI_MODEL=gpt-4o-mini  # Optional

# Run the sample
dotnet run --project samples/StructuredTools/StructuredTools.csproj
```

## Example Interactions

```
> What is 42 plus 8?
Assistant: The answer is 50. To break it down: 42 + 8 = 50.

> Find hotels in Paris for 3 nights, max $150/night starting 2026-05-01
Assistant: Found 2 hotels in Paris for 3 nights:
- Grand Hotel - Paris ($150/night, 4.8★) - Luxury downtown location
- Riverside Resort - Paris ($120/night, 4.5★) - Scenic views

> Book the Riverside Resort (id: rs-001) from 2026-05-01 for 2 nights in the name of John Doe, double room
Assistant: Booking confirmed! Confirmation code: CONF982345
Hotel ID: rs-001
Guest: John Doe
Check-in: 2026-05-01
Nights: 2
Room: double
```

## How It Works

### Step 1: Define Parameters as Attributes

```csharp
public sealed class MyTool : ITool
{
    public string Name => "my_tool";
    public string Description => "Does something useful";

    [ToolParameter(Description = "A required parameter", Required = true)]
    public string RequiredParam { get; set; } = "";

    [ToolParameter(Description = "An optional parameter", MinimumValue = 1, MaximumValue = 100)]
    public int OptionalParam { get; set; }

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken)
    {
        // Framework automatically parses arguments and validates constraints
        // Properties are now populated with validated values
        Console.WriteLine($"Required: {RequiredParam}, Optional: {OptionalParam}");
        return Task.FromResult("Done");
    }
}
```

### Step 2: Register the Tool

```csharp
var assistant = new AgentBuilder()
    .WithChatClient(new OpenAIClient(apiKey).AsChatClient(model))
    .WithTool(new MyTool())
    .Build();
```

### Step 3: Framework Handles the Rest

When the LLM calls your tool:
1. Framework **extracts parameter metadata** via reflection
2. **Generates JSON schema** for the parameter definitions
3. **Sends schema to LLM** so it knows exact requirements
4. When LLM invokes tool, framework **parses JSON arguments**
5. **Validates all constraints** (min/max, length, enum, pattern, required)
6. **Populates tool properties** with validated values
7. Calls your `InvokeAsync()` method with safe, validated parameters
8. If validation fails, returns error to LLM automatically

## Constraint Reference

| Constraint | Type | Example | Purpose |
|-----------|------|---------|---------|
| `Required` | bool | `Required = true` | Parameter must be provided |
| `MinimumValue` | double | `MinimumValue = 1` | Minimum for numeric parameters |
| `MaximumValue` | double | `MaximumValue = 100` | Maximum for numeric parameters |
| `MinLengthValue` | int | `MinLengthValue = 2` | Minimum length for strings/arrays |
| `MaxLengthValue` | int | `MaxLengthValue = 255` | Maximum length for strings/arrays |
| `Enum` | string[] | `Enum = ["a", "b", "c"]` | Allowed values for enum-like fields |
| `Pattern` | string | `Pattern = @"^\d{10}$"` | Regex pattern for string validation |
| `DefaultValue` | object? | `DefaultValue = "default"` | Default if not provided |
| `Description` | string | `Description = "..."` | Sent to LLM in schema |

## Backward Compatibility

Structured parameters are **optional and opt-in**:
- Existing string-based tools continue to work unchanged
- You can mix structured and non-structured tools in the same agent
- No breaking changes to the API
- Gradually migrate tools as needed

## Benefits

✅ **Type Safety** - Parameters are strongly typed, no string parsing  
✅ **Automatic Validation** - Constraints enforced by framework  
✅ **Better LLM Behavior** - Schema helps LLM understand requirements  
✅ **Less Code** - No manual JSON parsing or validation  
✅ **Discoverable** - Metadata available via reflection for tooling  
✅ **Composable** - Works with existing middleware and agents  
✅ **Backward Compatible** - Optional feature, doesn't break existing tools  

## Learning More

- See `Abstractions/ToolParameterAttribute.cs` for available constraints
- See `Core/ToolParameterBinder.cs` for binding and validation logic
- See `tests/Agentic.Tests/StructuredToolParametersTests.cs` for comprehensive test examples
- Check `Abstractions/ToolParameterSchema.cs` for JSON schema generation
