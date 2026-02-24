using Agentic.Core;
using Agentic.Middleware;

namespace RateLimitingMiddlewareSample;

/// <summary>
/// RateLimitingMiddleware demonstrates how to implement rate limiting to prevent abuse
/// and ensure fair resource usage. This middleware uses a token bucket algorithm.
/// </summary>
sealed class RateLimitingMiddleware(int maxRequestsPerMinute = 10) : IAssistantMiddleware
{
    private readonly int _maxRequestsPerMinute = maxRequestsPerMinute;
    private readonly Dictionary<string, RequestBucket> _buckets = [];
    private readonly object _lock = new();

    public async Task<AgentResponse> InvokeAsync(
        AgentContext context,
        AgentHandler next,
        CancellationToken cancellationToken = default)
    {
        var clientId = GetClientId();
        
        lock (_lock)
        {
            if (!_buckets.TryGetValue(clientId, out var bucket))
            {
                bucket = new RequestBucket(_maxRequestsPerMinute);
                _buckets[clientId] = bucket;
            }

            // Check if rate limit exceeded
            if (!bucket.TryConsumeToken())
            {
                var timeUntilReset = bucket.TimeUntilReset();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Rate limit exceeded! Try again in {timeUntilReset.TotalSeconds:F0} seconds.");
                Console.ResetColor();
                
                throw new InvalidOperationException(
                    $"Rate limit exceeded. Max {_maxRequestsPerMinute} requests per minute. " +
                    $"Try again in {timeUntilReset.TotalSeconds:F0} seconds.");
            }

            var remaining = bucket.RemainingTokens;
            var resetTime = bucket.TimeUntilReset();
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"📊 Rate Limit: {remaining}/{_maxRequestsPerMinute} requests remaining (resets in {resetTime.TotalSeconds:F0}s)");
            Console.ResetColor();
        }

        return await next(context, cancellationToken);
    }

    private static string GetClientId()
    {
        // In a real application, you might get this from:
        // - HTTP request headers (X-Client-ID)
        // - User authentication
        // - IP address
        // For this sample, we use a fixed client ID
        return "default-client";
    }

    /// <summary>
    /// Token bucket implementation for rate limiting.
    /// </summary>
    private sealed class RequestBucket
    {
        private readonly int _capacity;
        private readonly TimeSpan _refillInterval = TimeSpan.FromMinutes(1);
        private int _tokens;
        private DateTime _lastRefill;

        public RequestBucket(int capacity)
        {
            _capacity = capacity;
            _tokens = capacity;
            _lastRefill = DateTime.UtcNow;
        }

        public bool TryConsumeToken()
        {
            RefillTokens();

            if (_tokens > 0)
            {
                _tokens--;
                return true;
            }

            return false;
        }

        public int RemainingTokens
        {
            get
            {
                RefillTokens();
                return _tokens;
            }
        }

        public TimeSpan TimeUntilReset()
        {
            var timeSinceLastRefill = DateTime.UtcNow - _lastRefill;
            var timeUntilReset = _refillInterval - timeSinceLastRefill;
            return timeUntilReset > TimeSpan.Zero ? timeUntilReset : TimeSpan.Zero;
        }

        private void RefillTokens()
        {
            var now = DateTime.UtcNow;
            var timeSinceLastRefill = now - _lastRefill;

            if (timeSinceLastRefill >= _refillInterval)
            {
                _tokens = _capacity;
                _lastRefill = now;
            }
        }
    }
}
