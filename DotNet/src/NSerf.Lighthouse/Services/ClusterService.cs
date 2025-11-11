using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Models;
using NSerf.Lighthouse.Repositories;
using NSerf.Lighthouse.Utilities;

namespace NSerf.Lighthouse.Services;

public class ClusterService(
    IClusterRepository clusterRepository,
    ILogger<ClusterService> logger)
    : IClusterService
{
    public async Task<ClusterRegistrationResult> RegisterClusterAsync(
        RegisterClusterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(request.ClusterId, out var clusterId))
        {
            logger.LogWarning("Invalid GUID format: {ClusterId}", request.ClusterId);
            return new ClusterRegistrationResult(
                ClusterRegistrationStatus.InvalidGuidFormat,
                "invalid_guid_format");
        }

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(request.PublicKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid base64 public key");
            return new ClusterRegistrationResult(
                ClusterRegistrationStatus.InvalidPublicKey,
                "invalid_base64");
        }

        if (!CryptographyHelper.ValidatePublicKey(publicKey))
        {
            logger.LogWarning("Invalid public key format or curve");
            return new ClusterRegistrationResult(
                ClusterRegistrationStatus.InvalidPublicKey,
                "invalid_base64");
        }

        var existing = await clusterRepository.GetByIdAsync(clusterId, cancellationToken);
        if (existing != null)
        {
            if (existing.PublicKey.SequenceEqual(publicKey))
            {
                logger.LogInformation("Cluster {ClusterId} already registered with same key", clusterId);
                return new ClusterRegistrationResult(ClusterRegistrationStatus.AlreadyExists);
            }

            logger.LogWarning("Cluster {ClusterId} exists with different public key", clusterId);
            return new ClusterRegistrationResult(
                ClusterRegistrationStatus.PublicKeyMismatch,
                "public_key_mismatch");
        }

        var cluster = new ClusterRegistration
        {
            ClusterId = clusterId,
            PublicKey = publicKey
        };

        await clusterRepository.AddAsync(cluster, cancellationToken);
        logger.LogInformation("Registered new cluster {ClusterId}", clusterId);

        return new ClusterRegistrationResult(ClusterRegistrationStatus.Created);
    }
}
