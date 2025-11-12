using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSerf.Lighthouse.Client.Cryptography;
using NSerf.Lighthouse.Client.Models;

namespace NSerf.Lighthouse.Client;

/// <summary>
/// Client for interacting with the Lighthouse server
/// </summary>
public class LighthouseClient : ILighthouseClient
{
    private readonly HttpClient _httpClient;
    private readonly LighthouseClientOptions _options;
    private readonly ILogger<LighthouseClient> _logger;
    private readonly byte[] _privateKey;
    private readonly byte[] _aesKey;

    /// <summary>
    /// Initializes a new instance of the LighthouseClient
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests</param>
    /// <param name="options">Configuration options for the client</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public LighthouseClient(
        HttpClient httpClient,
        IOptions<LighthouseClientOptions> options,
        ILogger<LighthouseClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrEmpty(_options.BaseUrl))
            throw new ArgumentException("BaseUrl is required", nameof(options));
        if (string.IsNullOrEmpty(_options.ClusterId))
            throw new ArgumentException("ClusterId is required", nameof(options));
        if (string.IsNullOrEmpty(_options.PrivateKey))
            throw new ArgumentException("PrivateKey is required", nameof(options));
        if (string.IsNullOrEmpty(_options.AesKey))
            throw new ArgumentException("AesKey is required", nameof(options));

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        _privateKey = Convert.FromBase64String(_options.PrivateKey);
        _aesKey = Convert.FromBase64String(_options.AesKey);

        if (_aesKey.Length != 32)
            throw new ArgumentException("AES key must be 32 bytes (256 bits)", nameof(options));

        _logger.LogInformation("Lighthouse client initialized for cluster {ClusterId} targeting {BaseUrl}",
            _options.ClusterId, _options.BaseUrl);
    }

    /// <summary>
    /// Registers the cluster with the Lighthouse server using the provided public key
    /// </summary>
    /// <param name="publicKey">The ECDSA public key in SPKI format</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if registration succeeded, false otherwise</returns>
    public async Task<bool> RegisterClusterAsync(byte[] publicKey, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Registering cluster {ClusterId}", _options.ClusterId);

            var request = new
            {
                _options.ClusterId,
                PublicKey = Convert.ToBase64String(publicKey)
            };

            var response = await _httpClient.PostAsJsonAsync("/clusters", request, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Cluster {ClusterId} registered successfully", _options.ClusterId);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Cluster registration failed with status {StatusCode}: {Error}",
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register cluster {ClusterId}", _options.ClusterId);
            throw;
        }
    }

    /// <summary>
    /// Discovers other nodes in the cluster and registers the current node
    /// </summary>
    /// <param name="currentNode">Information about the current node</param>
    /// <param name="versionName">The version name (e.g., "production", "staging")</param>
    /// <param name="versionNumber">The version number</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of discovered peer nodes</returns>
    public async Task<List<NodeInfo>> DiscoverNodesAsync(
        NodeInfo currentNode,
        string versionName,
        long versionNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Starting node discovery for cluster {ClusterId}, version {VersionName}:{VersionNumber}",
                _options.ClusterId, versionName, versionNumber);

            var nodePayload = JsonSerializer.SerializeToUtf8Bytes(currentNode);
            var encryptedPayload = CryptoHelper.Encrypt(nodePayload, _aesKey, out var nonce);

        var payloadBase64 = Convert.ToBase64String(encryptedPayload);
        var nonceBase64 = Convert.ToBase64String(nonce);

        var signatureData = Encoding.UTF8.GetBytes(
            _options.ClusterId +
            versionName +
            versionNumber +
            payloadBase64 +
            nonceBase64);

        var signature = CryptoHelper.SignData(_privateKey, signatureData);
        var signatureBase64 = Convert.ToBase64String(signature);

        var discoverRequest = new
        {
            _options.ClusterId,
            VersionName = versionName,
            VersionNumber = versionNumber,
            Payload = payloadBase64,
            Nonce = nonceBase64,
            Signature = signatureBase64
        };

            var response = await _httpClient.PostAsJsonAsync("/discover", discoverRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Discovery request failed with status {StatusCode}: {Error}",
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"Discovery failed: {response.StatusCode} - {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<DiscoverResponse>(cancellationToken);
            if (result?.Nodes == null)
            {
                _logger.LogInformation("No nodes discovered for cluster {ClusterId}", _options.ClusterId);
                return [];
            }

            var nodes = new List<NodeInfo>();
            var nbFailedDecryption = 0;

            foreach (var encryptedNode in result.Nodes)
            {
                try
                {
                    var encryptedBytes = Convert.FromBase64String(encryptedNode);
                    
                    var nodeNonce = new byte[4];
                    Array.Copy(encryptedBytes, 0, nodeNonce, 0, 4);
                    
                    var ciphertext = new byte[encryptedBytes.Length - 4];
                    Array.Copy(encryptedBytes, 4, ciphertext, 0, ciphertext.Length);
                    
                    var decryptedBytes = CryptoHelper.Decrypt(ciphertext, _aesKey, nodeNonce);
                    var node = JsonSerializer.Deserialize<NodeInfo>(decryptedBytes);

                    if (node == null) continue;
                    nodes.Add(node);
                    _logger.LogDebug("Decrypted node: {IpAddress}:{Port}", node.IpAddress, node.Port);
                }
                catch (Exception ex)
                {
                    nbFailedDecryption++;
                    _logger.LogWarning(ex, "Failed to decrypt node payload");
                }
            }

            if (nbFailedDecryption > 0)
            {
                _logger.LogWarning("Failed to decrypt {FailedCount} out of {TotalCount} nodes",
                    nbFailedDecryption, result.Nodes.Count);
            }

            _logger.LogInformation("Discovered {NodeCount} nodes for cluster {ClusterId}, version {VersionName}:{VersionNumber}",
                nodes.Count, _options.ClusterId, versionName, versionNumber);

            return nodes;
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            _logger.LogError(ex, "Unexpected error during node discovery for cluster {ClusterId}",
                _options.ClusterId);
            throw;
        }
    }

    private sealed class DiscoverResponse
    {
        public List<string> Nodes { get; init; } = [];
    }
}
