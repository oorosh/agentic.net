using Agentic.Abstractions;

namespace Agentic.Tests.Fakes;

/// <summary>
/// A test double for <see cref="IModelProvider"/> that wraps a pre-constructed <see cref="IAgentModel"/>.
/// </summary>
internal sealed class FakeModelProvider : IModelProvider
{
    private readonly IAgentModel _model;

    public FakeModelProvider(IAgentModel model) => _model = model;

    public IAgentModel CreateModel() => _model;
}
