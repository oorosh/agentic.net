namespace Agentic.Abstractions;

public interface IChannel
{
    string Name { get; }
    Task StartAsync(Func<string, CancellationToken, Task<string>> onMessage, CancellationToken cancellationToken = default);
}
