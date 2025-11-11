namespace NSerf.Lighthouse.Data.Entities;

public class ClusterEntity
{
    public Guid ClusterId { get; init; }
    public byte[] PublicKey { get; init; } = [];
}
