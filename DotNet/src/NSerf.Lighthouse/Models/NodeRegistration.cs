namespace NSerf.Lighthouse.Models;

public class NodeRegistration
{
    public Guid ClusterId { get; init; }
    public string VersionName { get; init; } = string.Empty;
    public long VersionNumber { get; init; }
    public byte[] EncryptedPayload { get; init; } = [];
    public long ServerTimeStamp { get; init; }
}
