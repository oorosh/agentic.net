namespace Agentic.Abstractions;

public sealed record Skill
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Path { get; init; }
    public string? License { get; init; }
    public string? Compatibility { get; init; }
    public string? AllowedTools { get; init; }
    public string Instructions { get; init; } = string.Empty;
}

public interface ISkillLoader
{
    Task<IReadOnlyList<Skill>> LoadSkillsAsync(CancellationToken cancellationToken = default);
    Task<Skill?> LoadSkillAsync(string name, CancellationToken cancellationToken = default);
}
