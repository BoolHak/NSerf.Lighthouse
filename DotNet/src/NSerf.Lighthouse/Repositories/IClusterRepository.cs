using NSerf.Lighthouse.Models;

namespace NSerf.Lighthouse.Repositories;

public interface IClusterRepository
{
    Task<ClusterRegistration?> GetByIdAsync(Guid clusterId, CancellationToken cancellationToken = default);
    Task<bool> AddAsync(ClusterRegistration cluster, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid clusterId, byte[] publicKey, CancellationToken cancellationToken = default);
}
