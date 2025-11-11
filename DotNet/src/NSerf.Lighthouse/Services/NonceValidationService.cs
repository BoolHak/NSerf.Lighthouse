using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace NSerf.Lighthouse.Services;

/// <summary>
/// In-memory implementation of a nonce validation service.
/// Uses IMemoryCache with sliding expiration to track used nonces.
/// </summary>
public class NonceValidationService(
    IMemoryCache cache,
    IOptions<NonceValidationOptions> options,
    ILogger<NonceValidationService> logger)
    : INonceValidationService
{
    private readonly TimeSpan _nonceWindowDuration = options.Value.WindowDuration;

    public Task<bool> ValidateAndRecordNonceAsync(string nonce, string signature)
    {
        if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("Nonce or signature is empty");
            return Task.FromResult(false);
        }

        var cacheKey = $"nonce:{nonce}:{signature}";

        if (cache.TryGetValue(cacheKey, out _))
        {
            logger.LogWarning("Replay attack detected: nonce {Nonce} already used", nonce);
            return Task.FromResult(false);
        }

        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = _nonceWindowDuration
        };

        cache.Set(cacheKey, true, cacheOptions);
        
        logger.LogDebug("Nonce {Nonce} recorded successfully", nonce);
        return Task.FromResult(true);
    }

    
}

/// <summary>
/// Configuration options for nonce validation
/// </summary>
public class NonceValidationOptions
{
    public const string SectionName = "NonceValidation";
    
    /// <summary>
    /// Duration for which nonces are tracked (sliding window)
    /// Default: 24 hours
    /// </summary>
    public TimeSpan WindowDuration { get; init; } = TimeSpan.FromHours(24);
}
