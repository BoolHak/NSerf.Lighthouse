using NSerf.Lighthouse.DTOs;

namespace NSerf.Lighthouse.Services;

public interface INodeDiscoveryService
{
    Task<NodeDiscoveryResult> DiscoverNodesAsync(
        DiscoverRequest request,
        CancellationToken cancellationToken = default);
}

public enum NodeDiscoveryStatus
{
    Success,
    ClusterNotFound,
    InvalidGuidFormat,
    InvalidBase64,
    InvalidNonceSize,
    PayloadTooLarge,
    SignatureVerificationFailed,
    InvalidPayload,
    ReplayAttackDetected,
    InternalError
}

public record NodeDiscoveryResult(
    NodeDiscoveryStatus Status,
    DiscoverResponse? Response = null,
    string? ErrorMessage = null);
