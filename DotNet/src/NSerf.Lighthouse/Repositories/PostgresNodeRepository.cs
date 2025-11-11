using Microsoft.EntityFrameworkCore;
using NSerf.Lighthouse.Data;
using NSerf.Lighthouse.Data.Entities;
using NSerf.Lighthouse.Models;

namespace NSerf.Lighthouse.Repositories;

public class PostgresNodeRepository(LighthouseDbContext context) : INodeRepository
{
    public async Task AddNodeAsync(NodeRegistration node, CancellationToken cancellationToken = default)
    {
        var entity = new NodeEntity
        {
            ClusterId = node.ClusterId,
            VersionName = node.VersionName,
            VersionNumber = node.VersionNumber,
            EncryptedPayload = node.EncryptedPayload,
            ServerTimeStamp = node.ServerTimeStamp
        };

        context.Nodes.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<NodeRegistration>> GetNodesAsync(
        Guid clusterId,
        string versionName,
        long versionNumber,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var entities = await context.Nodes
            .AsNoTracking()
            .Where(n => n.ClusterId == clusterId &&
                       n.VersionName == versionName &&
                       n.VersionNumber == versionNumber)
            .OrderByDescending(n => n.ServerTimeStamp)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new NodeRegistration
        {
            ClusterId = e.ClusterId,
            VersionName = e.VersionName,
            VersionNumber = e.VersionNumber,
            EncryptedPayload = e.EncryptedPayload,
            ServerTimeStamp = e.ServerTimeStamp
        }).ToList();
    }
}
