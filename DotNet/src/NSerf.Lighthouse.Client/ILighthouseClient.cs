using NSerf.Lighthouse.Client.Models;

namespace NSerf.Lighthouse.Client;

/// <summary>
/// Interface for the Lighthouse client
/// </summary>
public interface ILighthouseClient
{
    /// <summary>
    /// Registers the cluster with the Lighthouse server
    /// </summary>
    Task<bool> RegisterClusterAsync(byte[] publicKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers other nodes in the cluster and registers this node
    /// </summary>
    Task<List<NodeInfo>> DiscoverNodesAsync(
        NodeInfo currentNode,
        string versionName,
        long versionNumber,
        CancellationToken cancellationToken = default);
}
