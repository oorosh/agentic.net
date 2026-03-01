using Agentic.Abstractions;
using Agentic.Builder;
using Agentic.Core;
using Agentic.Providers.OpenAi;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Please set the OPENAI_API_KEY environment variable.");
    return;
}

var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? OpenAiModels.Gpt4oMini;

var assistant = new AgentBuilder()
    .WithOpenAi(apiKey, model)
    .WithTool(new CalculatorTool())
    .WithTool(new HotelSearchTool())
    .WithTool(new HotelBookingTool())
    .Build();

Console.WriteLine("== Structured Tools Sample ==");
Console.WriteLine("Try prompts like:");
Console.WriteLine("- 'What is 42 plus 8?'");
Console.WriteLine("- 'Find hotels in Paris for 3 nights, max $150/night starting 2026-05-01'");
Console.WriteLine("- 'Book the Grand Hotel (id: gh-001) from 2026-05-01 for 2 nights in the name of John Doe, double room'");
Console.WriteLine("Type 'exit' to quit.\n");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    try
    {
        var reply = await assistant.ReplyAsync(input);
        Console.WriteLine($"Assistant: {reply}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}\n");
    }
}

/// <summary>
/// Example of a structured tool using parameter attributes for type-safe, validated arguments.
/// </summary>
public sealed class CalculatorTool : ITool
{
    public string Name => "calculate";
    public string Description => "Performs mathematical calculations (add, subtract, multiply, divide)";

    [ToolParameter(Description = "The operation: add, subtract, multiply, or divide", Required = true)]
    public string Operation { get; set; } = "";

    [ToolParameter(Description = "First number", Required = true)]
    public double A { get; set; }

    [ToolParameter(Description = "Second number", Required = true)]
    public double B { get; set; }

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var result = Operation.ToLower() switch
        {
            "add" => A + B,
            "subtract" => A - B,
            "multiply" => A * B,
            "divide" => B != 0 ? A / B : throw new InvalidOperationException("Division by zero"),
            _ => throw new InvalidOperationException($"Unknown operation: {Operation}")
        };

        return Task.FromResult($"{A} {Operation} {B} = {result}");
    }
}

/// <summary>
/// Hotel search tool demonstrating validation constraints on parameters.
/// </summary>
public sealed class HotelSearchTool : ITool
{
    public string Name => "search_hotels";
    public string Description => "Search for available hotels with filters";

    [ToolParameter(Description = "City name", Required = true)]
    public string City { get; set; } = "";

    [ToolParameter(Description = "Check-in date in YYYY-MM-DD format", Required = true)]
    public string CheckInDate { get; set; } = "";

    [ToolParameter(Description = "Number of nights", Required = true, MinimumValue = 1, MaximumValue = 30)]
    public int Nights { get; set; }

    [ToolParameter(Description = "Maximum price per night in USD", Required = true, MinimumValue = 0)]
    public double MaxPrice { get; set; }

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var hotels = new[]
        {
            $"Grand Hotel - {City} ($180/night, 4.8★) - Luxury downtown location",
            $"Budget Inn - {City} ($65/night, 3.9★) - Near airport",
            $"Riverside Resort - {City} ($120/night, 4.5★) - Scenic views"
        };

        var filtered = hotels
            .Where(h => double.Parse(h.Split('$')[1].Split('/')[0]) <= MaxPrice)
            .ToList();

        if (!filtered.Any())
        {
            return Task.FromResult($"No hotels found in {City} within budget of ${MaxPrice}/night");
        }

        return Task.FromResult($"Found {filtered.Count} hotels in {City} for {Nights} nights:\n- " + string.Join("\n- ", filtered));
    }
}

/// <summary>
/// Hotel booking tool with enumeration validation.
/// </summary>
public sealed class HotelBookingTool : ITool
{
    public string Name => "book_hotel";
    public string Description => "Book a hotel reservation";

    [ToolParameter(Description = "Hotel identifier", Required = true)]
    public string HotelId { get; set; } = "";

    [ToolParameter(Description = "Check-in date (YYYY-MM-DD)", Required = true)]
    public string CheckInDate { get; set; } = "";

    [ToolParameter(Description = "Number of nights", Required = true, MinimumValue = 1, MaximumValue = 30)]
    public int Nights { get; set; }

    [ToolParameter(Description = "Guest name", Required = true, MinLengthValue = 2, MaxLengthValue = 100)]
    public string GuestName { get; set; } = "";

    [ToolParameter(Description = "Room type: single, double, or suite", Required = true, Enum = ["single", "double", "suite"])]
    public string RoomType { get; set; } = "";

    public Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var confirmationCode = $"CONF{DateTime.Now.Ticks % 1000000}";
        return Task.FromResult(
            $"Booking confirmed! Confirmation code: {confirmationCode}\n" +
            $"Hotel ID: {HotelId}\n" +
            $"Guest: {GuestName}\n" +
            $"Check-in: {CheckInDate}\n" +
            $"Nights: {Nights}\n" +
            $"Room: {RoomType}");
    }
}
