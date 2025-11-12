namespace NSerf.Lighthouse.Client.Models;

/// <summary>
/// Represents a node in the cluster
/// </summary>
public class NodeInfo
{
    /// <summary>
    /// Node's IP address
    /// </summary>
    public string IpAddress { get; init; } = string.Empty;

    /// <summary>
    /// Node's port number
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Additional metadata about the node
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
