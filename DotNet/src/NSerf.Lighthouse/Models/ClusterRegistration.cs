namespace NSerf.Lighthouse.Models;

public class ClusterRegistration
{
    public Guid ClusterId { get; init; }
    public byte[] PublicKey { get; init; } = [];
}
