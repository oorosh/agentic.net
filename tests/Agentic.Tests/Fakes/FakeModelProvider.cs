using Agentic.Abstractions;
using Agentic.Core;

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

/// <summary>
/// Utility that provides a default <see cref="IAgentModel.StreamAsync"/> implementation for test models
/// by buffering the result of <see cref="IAgentModel.CompleteAsync"/> and emitting it as a single complete token.
/// </summary>
internal static class FakeModelStreamHelper
{
    public static async IAsyncEnumerable<StreamingToken> StreamFromCompleteAsync(
        IAgentModel model,
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        var response = await model.CompleteAsync(messages, cancellationToken);
        if (!string.IsNullOrEmpty(response.Content))
            yield return new StreamingToken(response.Content, IsComplete: false);

        yield return new StreamingToken(
            Delta: string.Empty,
            IsComplete: true,
            FinalUsage: response.Usage,
            FinishReason: response.FinishReason,
            ModelId: response.ModelId,
            ToolCalls: response.ToolCalls);
    }
}
