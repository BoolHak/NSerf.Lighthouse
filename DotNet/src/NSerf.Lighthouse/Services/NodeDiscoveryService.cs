using System.Text;
using System.Text.Json;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Models;
using NSerf.Lighthouse.Repositories;
using NSerf.Lighthouse.Utilities;

namespace NSerf.Lighthouse.Services;

public class NodeDiscoveryService(
    IClusterRepository clusterRepository,
    INodeRepository nodeRepository,
    NodeEvictionService evictionService,
    INonceValidationService nonceValidationService,
    ILogger<NodeDiscoveryService> logger)
    : INodeDiscoveryService
{
    private const int MaxPayloadSize = 10 * 1024; 

    public async Task<NodeDiscoveryResult> DiscoverNodesAsync(
        DiscoverRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(request.ClusterId, out var clusterId))
        {
            logger.LogWarning("Invalid GUID format: {ClusterId}", request.ClusterId);
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.InvalidGuidFormat,
                ErrorMessage: "invalid_guid_format");
        }

        var cluster = await clusterRepository.GetByIdAsync(clusterId, cancellationToken);
        if (cluster == null)
        {
            logger.LogWarning("Cluster not found: {ClusterId}", clusterId);
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.ClusterNotFound,
                ErrorMessage: "cluster_not_found");
        }

        byte[] payloadBytes, nonceBytes, signatureBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(request.Payload);
            nonceBytes = Convert.FromBase64String(request.Nonce);
            signatureBytes = Convert.FromBase64String(request.Signature);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid base64 encoding");
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.InvalidBase64,
                ErrorMessage: "invalid_base64");
        }

        if (nonceBytes.Length != 4)
        {
            logger.LogWarning("Invalid nonce size: {Size} bytes", nonceBytes.Length);
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.InvalidNonceSize,
                ErrorMessage: "nonce_must_be_4_bytes");
        }

        if (payloadBytes.Length > MaxPayloadSize)
        {
            logger.LogWarning("Payload too large: {Size} bytes", payloadBytes.Length);
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.PayloadTooLarge,
                ErrorMessage: "payload_too_large");
        }

        if (string.IsNullOrEmpty(request.VersionName))
        {
            logger.LogWarning("Version name is required");
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.InvalidPayload,
                ErrorMessage: "version_name_required");
        }

        var isNonceValid = await nonceValidationService.ValidateAndRecordNonceAsync(
            request.Nonce, request.Signature);
        if (!isNonceValid)
        {
            logger.LogWarning("Replay attack detected for cluster {ClusterId}", clusterId);
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.ReplayAttackDetected,
                ErrorMessage: "replay_attack_detected");
        }

        var signatureData = Encoding.UTF8.GetBytes(
            request.ClusterId + 
            request.VersionName + 
            request.VersionNumber.ToString() + 
            request.Payload + 
            request.Nonce);
        if (!CryptographyHelper.VerifySignature(cluster.PublicKey, signatureData, signatureBytes))
        {
            logger.LogWarning("Signature verification failed for cluster {ClusterId}", clusterId);
            return new NodeDiscoveryResult(
                NodeDiscoveryStatus.SignatureVerificationFailed,
                ErrorMessage: "signature_verification_failed");
        }

        var existingNodes = await nodeRepository.GetNodesAsync(
            clusterId,
            request.VersionName,
            request.VersionNumber,
            5,
            cancellationToken);

        var payloadWithNonce = new byte[nonceBytes.Length + payloadBytes.Length];
        Array.Copy(nonceBytes, 0, payloadWithNonce, 0, nonceBytes.Length);
        Array.Copy(payloadBytes, 0, payloadWithNonce, nonceBytes.Length, payloadBytes.Length);

        var nodeRegistration = new NodeRegistration
        {
            ClusterId = clusterId,
            VersionName = request.VersionName,
            VersionNumber = request.VersionNumber,
            EncryptedPayload = payloadWithNonce,
            ServerTimeStamp = DateTime.UtcNow.Ticks
        };

        await nodeRepository.AddNodeAsync(nodeRegistration, cancellationToken);

        _ = evictionService.QueueEvictionAsync(clusterId, request.VersionName, request.VersionNumber);

        var encryptedNodes = new List<string>();
        foreach (var node in existingNodes)
        {
            try
            {
                var encryptedNode = await EncryptNodePayloadAsync(node, cancellationToken);
                if (encryptedNode != null)
                {
                    encryptedNodes.Add(encryptedNode);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to encode node payload");
            }
        }

        logger.LogInformation(
            "Node discovered {Count} peers in cluster {ClusterId} version {VersionName}:{VersionNumber}",
            encryptedNodes.Count,
            clusterId,
            request.VersionName,
            request.VersionNumber);

        return new NodeDiscoveryResult(
            NodeDiscoveryStatus.Success,
            new DiscoverResponse { Nodes = encryptedNodes });
    }

    private static Task<string?> EncryptNodePayloadAsync(
        NodeRegistration node,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(Convert.ToBase64String(node.EncryptedPayload));
    }
}
