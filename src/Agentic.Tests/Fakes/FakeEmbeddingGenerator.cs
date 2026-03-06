using Microsoft.Extensions.AI;

namespace Agentic.Tests.Fakes;

internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly int _dimensions;

    public FakeEmbeddingGenerator(int dimensions) => _dimensions = dimensions;

    public EmbeddingGeneratorMetadata Metadata => new("fake", null, null, _dimensions);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(_ =>
            new Embedding<float>(new float[_dimensions])).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
