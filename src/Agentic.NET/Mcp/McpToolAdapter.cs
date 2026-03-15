using System.Text.Json;
using Agentic.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Agentic.Mcp;

/// <summary>
/// Adapts an MCP <see cref="McpClientTool"/> as an Agentic.NET <see cref="ITool"/>.
/// </summary>
internal sealed class McpToolAdapter : ITool
{
    private readonly McpClientTool _mcpTool;
    private readonly string _description;

    internal McpToolAdapter(McpClientTool mcpTool)
    {
        _mcpTool = mcpTool;

        // Embed the JSON schema in the description so the LLM sees parameter details.
        var schema = mcpTool.JsonSchema.ValueKind != JsonValueKind.Undefined
            ? mcpTool.JsonSchema.GetRawText()
            : null;

        _description = schema is not null
            ? $"{mcpTool.Description}\n  Parameters: {schema}"
            : mcpTool.Description;
    }

    public string Name => _mcpTool.Name;

    public string Description => _description;

    public async Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, object?>? args = null;

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
            if (parsed is { Count: > 0 })
            {
                args = parsed.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object?)kvp.Value,
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        var result = await _mcpTool.CallAsync(args, cancellationToken: cancellationToken);

        return FormatResult(result);
    }

    private static string FormatResult(CallToolResult result)
    {
        if (result.Content is not { Count: > 0 } content)
        {
            return string.Empty;
        }

        var texts = content
            .OfType<TextContentBlock>()
            .Select(t => t.Text);

        var combined = string.Join("\n", texts);

        if (!string.IsNullOrEmpty(combined))
        {
            return combined;
        }

        // Fallback for non-text content (images, resources, etc.)
        return JsonSerializer.Serialize(result.Content);
    }
}
