using NSerf.Lighthouse.DTOs;

namespace NSerf.Lighthouse.Services;

public interface IClusterService
{
    Task<ClusterRegistrationResult> RegisterClusterAsync(
        RegisterClusterRequest request, 
        CancellationToken cancellationToken = default);
}

public enum ClusterRegistrationStatus
{
    Created,
    AlreadyExists,
    PublicKeyMismatch,
    InvalidGuidFormat,
    InvalidPublicKey
}

public record ClusterRegistrationResult(
    ClusterRegistrationStatus Status,
    string? ErrorMessage = null);
