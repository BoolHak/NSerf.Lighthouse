namespace NSerf.Lighthouse.DTOs;

public class RegisterClusterRequest
{
    public string ClusterId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
}
