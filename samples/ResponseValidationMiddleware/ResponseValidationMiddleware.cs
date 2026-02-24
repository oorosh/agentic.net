using Agentic.Core;
using Agentic.Middleware;

namespace ResponseValidationMiddlewareSample;

/// <summary>
/// ResponseValidationMiddleware demonstrates how to validate LLM responses AFTER they're generated.
/// This middleware checks for common issues like incomplete responses, nonsense, and low confidence.
/// 
/// This is different from input validation - it validates the OUTPUT of the LLM.
/// Useful for catching "stupid" responses and triggering retries or fallbacks.
/// </summary>
sealed class ResponseValidationMiddleware : IAssistantMiddleware
{
    private readonly int _maxRetries = 2;
    private readonly int _minResponseLength = 5;
    private readonly int _maxResponseLength = 10000;

    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        int attempt = 0;

        while (attempt < _maxRetries)
        {
            attempt++;
            
            // ✅ Call the LLM
            var response = await next(context, cancellationToken);
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Validation] Attempt {attempt}/{_maxRetries}");
            Console.ResetColor();

            // 🔍 Validate the response
            var validation = ValidateResponse(response.Content);

            if (validation.IsValid)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Response validation passed: {validation.Reason}");
                Console.ResetColor();
                return response;
            }

            // Response failed validation
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ Response validation failed: {validation.Reason}");
            Console.ResetColor();

            if (attempt >= _maxRetries)
            {
                // Max retries reached, return generic fallback
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Max validation attempts ({_maxRetries}) reached. Returning fallback response.");
                Console.ResetColor();
                return new AgentResponse(
                    "I had trouble generating a proper response. Please try rephrasing your question or try again later.");
            }

            Console.WriteLine("🔄 Retrying...\n");
        }

        // Should never reach here, but return fallback just in case
        return new AgentResponse("Unable to generate a valid response.");
    }

    /// <summary>
    /// Validates an LLM response for common issues.
    /// </summary>
    private ValidationResult ValidateResponse(string content)
    {
        // Check 1: Not empty or whitespace only
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ValidationResult(false, "Response is empty");
        }

        // Check 2: Length validation
        if (content.Length < _minResponseLength)
        {
            return new ValidationResult(false, $"Response too short ({content.Length} chars, minimum {_minResponseLength})");
        }

        if (content.Length > _maxResponseLength)
        {
            return new ValidationResult(false, $"Response too long ({content.Length} chars, maximum {_maxResponseLength})");
        }

        // Check 3: Check for common "stupid" patterns
        var stupidPatterns = new[]
        {
            ("I am not able to", "Unhelpful deflection"),
            ("I cannot provide", "Refusing without reason"),
            ("I don't have the ability", "False capability denial"),
            ("Error:", "Error message leaked"),
            ("undefined", "Undefined value in response"),
            ("null", "Null value in response"),
            ("[object Object]", "Serialization error"),
            ("NaN", "Not-a-number error"),
        };

        foreach (var (pattern, reason) in stupidPatterns)
        {
            if (content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new ValidationResult(false, $"Detected pattern '{pattern}': {reason}");
            }
        }

        // Check 4: Check for repetition (a sign of model confusion)
        if (HasExcessiveRepetition(content))
        {
            return new ValidationResult(false, "Response contains excessive repetition");
        }

        // Check 5: Check for coherence (basic validation)
        if (!HasMinimumCoherence(content))
        {
            return new ValidationResult(false, "Response lacks coherence");
        }

        // Check 6: Check for relevance (basic check)
        if (!HasSentenceStructure(content))
        {
            return new ValidationResult(false, "Response is not properly formatted");
        }

        return new ValidationResult(true, "All validations passed");
    }

    /// <summary>
    /// Detects if response has excessive word repetition (sign of hallucination/confusion).
    /// </summary>
    private static bool HasExcessiveRepetition(string content)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordCounts = new Dictionary<string, int>();

        foreach (var word in words)
        {
            var normalizedWord = word.ToLowerInvariant().TrimPunctuation();
            if (normalizedWord.Length > 3) // Ignore small words
            {
                if (!wordCounts.TryGetValue(normalizedWord, out var count))
                {
                    wordCounts[normalizedWord] = 0;
                }
                wordCounts[normalizedWord]++;
            }
        }

        // If any word appears more than 20% of the time, it's suspicious
        var threshold = words.Length / 5;
        foreach (var count in wordCounts.Values)
        {
            if (count > threshold && count > 10)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if response has basic coherence (contains pronouns, verbs, etc).
    /// </summary>
    private static bool HasMinimumCoherence(string content)
    {
        // Very basic check: response should have some variety of parts of speech
        var hasCommonWords = content.Contains(" is ") ||
                           content.Contains(" are ") ||
                           content.Contains(" the ") ||
                           content.Contains(" a ") ||
                           content.Contains(" and ") ||
                           content.Contains(" of ");

        return hasCommonWords && content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5;
    }

    /// <summary>
    /// Checks if response has proper sentence structure (ends with punctuation, etc).
    /// </summary>
    private static bool HasSentenceStructure(string content)
    {
        // Response should have proper punctuation (not end in middle of sentence)
        var endsWithPunctuation = content.TrimEnd().EndsWith(".") ||
                                content.TrimEnd().EndsWith("!") ||
                                content.TrimEnd().EndsWith("?") ||
                                content.TrimEnd().EndsWith(")");

        return endsWithPunctuation;
    }

    private sealed class ValidationResult(bool isValid, string reason)
    {
        public bool IsValid { get; } = isValid;
        public string Reason { get; } = reason;
    }
}

/// <summary>
/// Extension methods for string helpers.
/// </summary>
internal static class StringExtensions
{
    public static string TrimPunctuation(this string str)
    {
        var chars = new[] { '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')' };
        return str.Trim(chars);
    }
}
