namespace Agentic.Abstractions;

/// <summary>
/// Abstraction for generating embeddings from text.
/// Implementations can use cloud APIs (e.g., OpenAI) or local models.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Initializes the embedding provider (e.g., load models or authenticate).
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector as a float array.</returns>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// The dimensionality of the embedding vectors produced by this provider.
    /// </summary>
    int Dimensions { get; }
}