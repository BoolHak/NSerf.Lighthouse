namespace NSerf.Lighthouse.Services;

/// <summary>
/// Service for validating nonce to prevent replay attacks.
/// Tracks used nonce within a configurable sliding time window.
/// </summary>
public interface INonceValidationService
{
    /// <summary>
    /// Validates that nonce hasn't been used before within the sliding window.
    /// If valid, records the nonce to prevent future replay attacks.
    /// </summary>
    /// <param name="nonce">The nonce to validate (typically base64 encoded)</param>
    /// <param name="signature">The request signature (used as an additional key for uniqueness)</param>
    /// <returns>True if the nonce is valid (not previously used), false if it's a replay attempt</returns>
    Task<bool> ValidateAndRecordNonceAsync(string nonce, string signature);

}
