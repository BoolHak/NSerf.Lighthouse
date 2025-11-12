namespace NSerf.Lighthouse.Client;

/// <summary>
/// Configuration options for the Lighthouse client
/// </summary>
public class LighthouseClientOptions
{
    /// <summary>
    /// The configuration section name for binding options from appsettings.json
    /// </summary>
    public const string SectionName = "LighthouseClient";

    /// <summary>
    /// Base URL of the Lighthouse server (e.g., "https://lighthouse.example.com")
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Cluster ID (GUID format)
    /// </summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// ECDSA private key in PKCS#8 format (base64 encoded)
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// AES-256 encryption key (base64 encoded, 32 bytes)
    /// </summary>
    public string AesKey { get; set; } = string.Empty;

    /// <summary> 
    /// HTTP request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
