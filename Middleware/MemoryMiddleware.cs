using Agentic.Abstractions;
using Agentic.Core;

namespace Agentic.Middleware;

public sealed class MemoryMiddleware(IMemoryService memoryService) : IAssistantMiddleware
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

        var memories = await memoryService.RetrieveRelevantAsync(query, topK: topK, cancellationToken);

        if (memories.Count > 0)
        {
            var memoryContext = "Relevant past conversation:\n" + string.Join("\n", memories);
            context.WorkingMessages.Insert(0, new ChatMessage(ChatRole.System, memoryContext));
        }

        return await next(context, cancellationToken);
    }
}
