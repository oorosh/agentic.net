using System.Text.Json;
using Agentic.Abstractions;

namespace Agentic.Providers.OpenAi;

/// <summary>
/// OpenAI embedding provider using the embeddings API.
/// </summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _httpClient = new();

    /// <summary>
    /// Creates a new OpenAI embedding provider.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Embedding model (default: text-embedding-3-small).</param>
    public OpenAiEmbeddingProvider(string apiKey, string model = "text-embedding-3-small")
    {
        _apiKey = apiKey;
        _model = model;

        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// The dimensionality of the embeddings.
    /// </summary>
    public int Dimensions => _model switch
    {
        "text-embedding-ada-002" => 1536,
        "text-embedding-3-small" => 1536,
        "text-embedding-3-large" => 3072,
        _ => 1536 // default
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

        using var response = await _httpClient.PostAsync(
            "https://api.openai.com/v1/embeddings",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var embedding = json.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");

        return embedding.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
    }
}