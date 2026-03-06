namespace Agentic.Abstractions;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    Task<string> InvokeAsync(string arguments, CancellationToken cancellationToken = default);
}
