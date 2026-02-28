namespace Agentic.Providers.OpenAi;

public sealed class OpenAiProviderOptions
{
    public string Model { get; set; } = OpenAiModels.Gpt4oMini;
    public IReadOnlyList<OpenAiFunctionToolDefinition>? Tools { get; set; }
}
