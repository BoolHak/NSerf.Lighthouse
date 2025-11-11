using System.Collections.Concurrent;
using NSerf.Lighthouse.Models;

namespace NSerf.Lighthouse.Repositories;

public class InMemoryClusterRepository : IClusterRepository
{
    private readonly ConcurrentDictionary<Guid, ClusterRegistration> _clusters = new();

    public Task<ClusterRegistration?> GetByIdAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _clusters.TryGetValue(clusterId, out var cluster);
        return Task.FromResult(cluster);
    }

    public Task<bool> AddAsync(ClusterRegistration cluster, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_clusters.TryAdd(cluster.ClusterId, cluster));
    }

    public Task<bool> ExistsAsync(Guid clusterId, byte[] publicKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_clusters.TryGetValue(clusterId, 
            out var existing) && existing.PublicKey.SequenceEqual(publicKey));
    }
}
