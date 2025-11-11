using System.Collections.Concurrent;
using NSerf.Lighthouse.Models;

namespace NSerf.Lighthouse.Repositories;

public class InMemoryNodeRepository : INodeRepository
{
    private readonly ConcurrentDictionary<string, List<NodeRegistration>> _nodes = new();
    private readonly object _lock = new();

    public Task AddNodeAsync(NodeRegistration node, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var key = GetKey(node.ClusterId, node.VersionName, node.VersionNumber);

        lock (_lock)
        {
            if (!_nodes.TryGetValue(key, out var nodes))
            {
                nodes = [];
                _nodes[key] = nodes;
            }
            
            nodes.Add(node);


            if (nodes.Count <= 5) return Task.CompletedTask;
            var oldest = nodes.OrderBy(n => n.ServerTimeStamp).First();
            nodes.Remove(oldest);
        }

        return Task.CompletedTask;
    }

    public Task<List<NodeRegistration>> GetNodesAsync(
        Guid clusterId, 
        string versionName, 
        long versionNumber, 
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var key = GetKey(clusterId, versionName, versionNumber);

        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var nodes))
            {
                return Task.FromResult(nodes
                    .OrderByDescending(n => n.ServerTimeStamp)
                    .Take(maxCount)
                    .ToList());
            }
        }

        return Task.FromResult(new List<NodeRegistration>());
    }

    private static string GetKey(Guid clusterId, string versionName, long versionNumber)
    {
        return $"{clusterId}:{versionName}:{versionNumber}";
    }
}
