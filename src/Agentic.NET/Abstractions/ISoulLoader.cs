namespace Agentic.Abstractions;

public sealed record SoulDocument
{
    public required string Name { get; init; }
    public string? Role { get; init; }
    public string? Personality { get; init; }
    public string? Rules { get; init; }
    public string? OutputFormat { get; init; }
    public string? Tools { get; init; }
    public string? Handoffs { get; init; }
    public string RawContent { get; init; } = string.Empty;
}

public interface ISoulLoader
{
    Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default);
    Task<SoulDocument?> ReloadSoulAsync(CancellationToken cancellationToken = default);
}

public interface IPersistentSoulLoader : ISoulLoader
{
    Task UpdateSoulAsync(SoulDocument soul, CancellationToken cancellationToken = default);
}
