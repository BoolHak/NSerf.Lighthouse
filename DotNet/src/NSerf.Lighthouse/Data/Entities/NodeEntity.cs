namespace NSerf.Lighthouse.Data.Entities;

public class NodeEntity
{
    public long Id { get; init; }
    public Guid ClusterId { get; init; }
    public string VersionName { get; init; } = string.Empty;
    public long VersionNumber { get; init; }
    public byte[] EncryptedPayload { get; init; } = [];
    public long ServerTimeStamp { get; init; }
}
