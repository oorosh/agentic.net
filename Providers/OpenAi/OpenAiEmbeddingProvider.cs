using System.Text.Json;
using Agentic.Abstractions;

namespace Agentic.Providers.OpenAi;

/// <summary>
/// OpenAI embedding provider using the embeddings API.
/// </summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private static readonly HttpClient SharedHttpClient = new();
    private const string EmbeddingsUrl = "https://api.openai.com/v1/embeddings";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly int? _overrideDimensions;

    /// <summary>
    /// Creates a new OpenAI embedding provider.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Embedding model (default: text-embedding-3-small).</param>
    public OpenAiEmbeddingProvider(string apiKey, string model = "text-embedding-3-small")
    {
        _apiKey = apiKey;
        _model = model;
    }

    /// <summary>
    /// Creates a new OpenAI embedding provider with an explicit dimension count.
    /// Use this overload for custom or fine-tuned models whose dimensions are not known at compile time.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Embedding model name.</param>
    /// <param name="dimensions">The number of dimensions the model produces.</param>
    public OpenAiEmbeddingProvider(string apiKey, string model, int dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        _apiKey = apiKey;
        _model = model;
        _overrideDimensions = dimensions;
    }

    /// <summary>
    /// The dimensionality of the embeddings produced by this model.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the model's dimensions are not known. Pass dimensions explicitly if using a custom model.</exception>
    public int Dimensions => _model switch
    {
        "text-embedding-ada-002" => 1536,
        "text-embedding-3-small" => 1536,
        "text-embedding-3-large" => 3072,
        _ => _overrideDimensions ?? throw new InvalidOperationException(
            $"Dimensions are not known for embedding model '{_model}'. " +
            "Pass the dimensions explicitly via the constructor.")
    };

    /// <summary>
    /// Initializes the provider (no-op for OpenAI).
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Generates an embedding for the given text.
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            input = text,
            model = _model
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, EmbeddingsUrl) { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await SharedHttpClient.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var embedding = json.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");

        return embedding.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
    }
}