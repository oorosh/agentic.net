# Safeguard Middleware Sample

This sample demonstrates how to implement safeguard middleware in Agentic.NET for content moderation and guardrails. It shows both prompt validation (pre-LLM) and response filtering (post-LLM) using custom `IAssistantMiddleware`.

## Key Features Demonstrated

- **Prompt Safeguards**: Validates and filters user inputs before sending to the LLM
- **Response Safeguards**: Modifies or filters LLM outputs before returning to the user
- **Middleware Pipeline**: Chains multiple middlewares for sequential processing
- **Short-Circuit Responses**: Returns safe responses without calling the LLM when needed
- **Content Moderation**: Basic keyword-based filtering (easily extensible to external APIs)

## Running the Sample

```bash
dotnet run --project samples/SafeguardMiddleware/SafeguardMiddleware.csproj
```

The sample runs an interactive chat loop. Try these test cases:

- Normal prompt: `hello world` → Echoes back normally
- Prompt blocking: `this is bad` → Returns safe error message
- Response filtering: `test` → Returns "This contains [censored] content."

```
== Safeguard Middleware Sample ==
This sample demonstrates prompt and response safeguards.
Try prompts with 'bad' to see blocking/censorship.

Type a prompt and press Enter. Type 'exit' to quit.

> hello world
Assistant: Echo: hello world

> this is bad
Assistant: I'm sorry, but I cannot process that request as it contains inappropriate content.

> test
Assistant: This contains [censored] content.

> exit
```

## Code Highlights

### Agent Configuration with Safeguards

```csharp
var assistant = new AgentBuilder()
    .WithModelProvider(new DemoModelProvider())
    .WithMiddleware(new PromptGuardMiddleware())    // Pre-LLM validation
    .WithMiddleware(new ResponseGuardMiddleware())  // Post-LLM filtering
    .Build();
```

### Prompt Guard Middleware (Pre-LLM)

```csharp
public sealed class PromptGuardMiddleware : IAssistantMiddleware
{
    public Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
    {
        // Block prompts containing prohibited words
        if (context.Input.Contains("bad", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new AgentResponse("I'm sorry, but I cannot process that request as it contains inappropriate content."));
        }

        // Proceed to next middleware/LLM
        return next(context, cancellationToken);
    }
}
```

### Response Guard Middleware (Post-LLM)

```csharp
public sealed class ResponseGuardMiddleware : IAssistantMiddleware
{
    public async Task<AgentResponse> InvokeAsync(AgentContext context, AgentHandler next, CancellationToken cancellationToken = default)
    {
        // Get response from LLM
        var response = await next(context, cancellationToken);

        // Filter prohibited content
        var filteredContent = response.Content.Replace("bad", "[censored]", StringComparison.OrdinalIgnoreCase);

        // Return modified response
        return new AgentResponse(filteredContent, response.ToolCalls);
    }
}
```

## Extending the Safeguards

This sample uses simple keyword matching for demonstration. For production use, consider:

- Integrating with external content moderation APIs (e.g., OpenAI Moderation, Azure Content Safety)
- Implementing more sophisticated filtering (regex patterns, ML models)
- Adding logging/auditing for blocked content
- Rate limiting based on user behavior
- Custom validation rules per use case

The middleware pattern allows flexible chaining of multiple safeguards for comprehensive protection.