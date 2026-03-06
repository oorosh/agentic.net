using System.Diagnostics;
using Agentic.Abstractions;
using Agentic.Core;
using Microsoft.Extensions.AI;

namespace Agentic.Middleware;

public sealed class MemoryMiddleware(IMemoryService memoryService, IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null) : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
    {
        // if the assistant has no history yet, this is the first call of the
        // session.  in that case we want to load *all* stored messages, not
        // just the ones matching the new input, because the user's very first
        // question may not contain any of the same tokens as earlier
        // statements ("what is my name?" etc.).
        bool initial = !context.History.Any();
        string query = initial ? string.Empty : context.Input;
        int topK = initial ? 100 : 5; // larger window on first turn

        IReadOnlyList<string> memories;
        string mode;

        using var activity = AgenticTelemetry.ActivitySource.StartActivity(AgenticTelemetry.Spans.MemoryRetrieval);

        if (!initial && embeddingGenerator != null)
        {
            mode = "semantic";
            activity?.SetTag(AgenticTelemetry.Tags.AgentMemoryMode, mode);
            var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
            var queryEmbedding = embeddings[0].Vector;
            var similar = await memoryService.RetrieveSimilarAsync(queryEmbedding, topK, cancellationToken);
            memories = similar.Select(x => x.Content).ToList();
        }
        else
        {
            mode = "keyword";
            activity?.SetTag(AgenticTelemetry.Tags.AgentMemoryMode, mode);
            memories = await memoryService.RetrieveRelevantAsync(query, topK, cancellationToken);
        }

        activity?.SetTag(AgenticTelemetry.Tags.AgentMemoryItems, memories.Count);
        AgenticTelemetry.MemoryRetrievalCounter.Add(1, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.AgentMemoryMode, mode));
        AgenticTelemetry.MemoryRetrievalItems.Record(memories.Count, new KeyValuePair<string, object?>(AgenticTelemetry.Tags.AgentMemoryMode, mode));

        if (memories.Count > 0)
        {
            var memoryContext = "Relevant past conversation:\n" + string.Join("\n", memories);
            context.WorkingMessages.Insert(0, new Agentic.Core.ChatMessage(Agentic.Core.ChatRole.System, memoryContext));
        }

        return await next(context, cancellationToken);
    }
}
