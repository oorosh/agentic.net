# ResponseValidationMiddleware Sample

## Overview

This sample demonstrates **response validation middleware** - a pattern that validates the LLM's output **after it's generated** to catch "stupid" or invalid responses.

## Key Concepts

### What Is Response Validation?

Response validation middleware inspects what the LLM returns **before sending it to the user**. If the response is invalid, it can:
- Reject it and retry
- Return a fallback response
- Log the issue for debugging

### When Does It Run?

```
Request → [LLM Called] → Response Generated
                            ↓
                    [Validation Middleware]
                    (CHECK: Is it good?)
                            ↓
                    Response to User
```

This is **different** from input validation which happens BEFORE the LLM is called.

## Validation Checks

The middleware performs these checks:

### 1. **Length Validation**
- Too short (< 5 chars) = likely incomplete
- Too long (> 10000 chars) = suspicious
- Moderate length = probably OK

### 2. **Empty/Null Detection**
- Rejects truly empty responses
- Catches `null` values and serialization errors

### 3. **Error Pattern Detection**
Looks for signs of failure:
- "I cannot provide..."
- "undefined" in response
- "[object Object]" (serialization error)
- "Error:" (literal error leaked)

### 4. **Sentence Structure**
- Response should end with punctuation (., !, ?, ))
- Sign that response is complete

### 5. **Coherence Check**
- Contains common English words and structure
- At least 5 words with variety
- Sign of real language, not gibberish

### 6. **Repetition Detection**
- Detects when model gets stuck repeating words
- Sign of hallucination or confusion
- Triggers retry

## Retry Strategy

If validation fails, middleware retries up to **2 more times**. This helps with:
- Transient LLM hallucinations
- Intermittent API issues that produce garbage
- Non-deterministic LLM behavior

If all retries fail, returns helpful fallback message.

## Usage

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMiddleware(new ResponseValidationMiddleware())
    .Build();

// Now all responses are validated automatically
var response = await agent.ReplyAsync("What is AI?");
// Returns validated, high-quality response or retry/fallback
```

## Real-World Scenarios

### Scenario 1: Model Hallucination
```
User: "Tell me about quantum computing"
LLM Returns: "I cannot provide information about quantum computing"
Validation: ❌ Detected "I cannot provide"
Action: Retry (up to 2 times)
```

### Scenario 2: Incomplete Response
```
User: "Explain photosynthesis"
LLM Returns: "Photosynthesis is the"
Validation: ❌ Response too short (21 chars < 5)
Action: Retry
```

### Scenario 3: Serialization Error
```
User: "What's your name?"
LLM Returns: "[object Object]"
Validation: ❌ Serialization error detected
Action: Retry
```

### Scenario 4: Good Response
```
User: "What is photosynthesis?"
LLM Returns: "Photosynthesis is the process by which plants convert sunlight into chemical energy..."
Validation: ✅ All checks passed
Action: Return to user immediately
```

## Advanced Use Cases

### Custom Validation Rules

Extend `ResponseValidationMiddleware` to add domain-specific checks:

```csharp
// Check for required information
if (!response.Contains("protein"))
    return ValidationResult(false, "Missing protein information");

// Check for factual accuracy (would need external data)
if (!await IsFactuallyCorrect(response))
    return ValidationResult(false, "Response appears factually incorrect");

// Domain-specific quality checks
if (response.Length < 100 && context.Input.Contains("explain"))
    return ValidationResult(false, "Explanation too brief");
```

### Combining with Other Middleware

```csharp
var agent = new AgentBuilder()
    .WithOpenAi(apiKey)
    .WithMiddleware(new RateLimitingMiddleware())     // Block bad users
    .WithMiddleware(new InputValidationMiddleware())  // Block bad inputs
    .WithMiddleware(new ErrorHandlingMiddleware())    // Retry transient errors
    .WithMiddleware(new ResponseValidationMiddleware()) // Check output quality
    .WithMiddleware(new CachingMiddleware())          // Cache good responses
    .Build();
```

## Performance Considerations

- **Fast checks** run first (length, empty)
- **Expensive checks** run later (coherence, repetition)
- Caching combined with validation = don't re-validate cached responses

## Alternatives

### Alternative 1: Token Counting
Count tokens instead of characters for more accurate length validation:
```csharp
var tokenCount = await tokenizer.CountTokensAsync(response);
if (tokenCount < 10)
    return false; // Too short
```

### Alternative 2: Semantic Similarity
Use embeddings to check if response is similar to input:
```csharp
var similarity = await embeddingProvider.ComputeSimilarityAsync(input, response);
if (similarity < 0.3)
    return false; // Completely off-topic
```

### Alternative 3: External API Validation
Use a separate LLM to validate another LLM's response:
```csharp
var isValid = await validationModel.CheckQuality(response);
```

## Files

- **ResponseValidationMiddleware.cs** - Main middleware implementation
- **Program.cs** - Demo application with test scenarios

## Running the Sample

```bash
OPENAI_API_KEY=sk-... dotnet run --project samples/ResponseValidationMiddleware/ResponseValidationMiddleware.csproj
```

You'll see:
- Validation checks for each response
- Retry logic in action
- Fallback messages when validation fails repeatedly
