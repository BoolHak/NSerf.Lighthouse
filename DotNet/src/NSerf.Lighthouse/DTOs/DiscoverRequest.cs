namespace NSerf.Lighthouse.DTOs;

public class DiscoverRequest
{
    public string ClusterId { get; set; } = string.Empty;
    public string VersionName { get; set; } = string.Empty;
    public long VersionNumber { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}
