using NSerf.Lighthouse.Models;

namespace NSerf.Lighthouse.Repositories;

public interface INodeRepository
{
    Task AddNodeAsync(NodeRegistration node, CancellationToken cancellationToken = default);
    Task<List<NodeRegistration>> GetNodesAsync(
        Guid clusterId, 
        string versionName, 
        long versionNumber, 
        int maxCount,
        CancellationToken cancellationToken = default);
}
