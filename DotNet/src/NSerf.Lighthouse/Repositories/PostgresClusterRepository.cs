using Microsoft.EntityFrameworkCore;
using NSerf.Lighthouse.Data;
using NSerf.Lighthouse.Data.Entities;
using NSerf.Lighthouse.Models;

namespace NSerf.Lighthouse.Repositories;

public class PostgresClusterRepository(LighthouseDbContext context) : IClusterRepository
{
    public async Task<ClusterRegistration?> GetByIdAsync(Guid clusterId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Clusters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClusterId == clusterId, cancellationToken);

        if (entity == null)
            return null;

        return new ClusterRegistration
        {
            ClusterId = entity.ClusterId,
            PublicKey = entity.PublicKey
        };
    }

    public async Task<bool> AddAsync(ClusterRegistration cluster, CancellationToken cancellationToken = default)
    {
        var entity = new ClusterEntity
        {
            ClusterId = cluster.ClusterId,
            PublicKey = cluster.PublicKey
        };

        try
        {
            context.Clusters.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(Guid clusterId, byte[] publicKey, CancellationToken cancellationToken = default)
    {
        var entity = await context.Clusters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClusterId == clusterId, cancellationToken);

        return entity != null && entity.PublicKey.SequenceEqual(publicKey);
    }
}
