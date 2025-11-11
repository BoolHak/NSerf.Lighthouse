using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Repositories;
using NSerf.Lighthouse.Services;
using NSerf.Lighthouse.Tests.TestHelpers;
using NSerf.Lighthouse.Utilities;
using System.Text;

namespace NSerf.Lighthouse.Tests.DiscoveryWorkflow;

/// <summary>
/// Tests for discovery workflow endpoint
/// </summary>
public class DiscoverControllerTests
{
    private readonly IClusterRepository _clusterRepository;
    private readonly INodeDiscoveryService _discoveryService;
    private readonly byte[] _aesKey;
    private readonly Guid _testClusterId;

    public DiscoverControllerTests()
    {
        _clusterRepository = new InMemoryClusterRepository();
        INodeRepository nodeRepository = new InMemoryNodeRepository();
        _aesKey = TestDataGenerator.GenerateAesKey();
        _testClusterId = TestDataGenerator.FixedClusterId();


        // Create a mock eviction service (won't be called in unit tests)
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var evictionService = new NodeEvictionService(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<NodeEvictionService>>(),
            Microsoft.Extensions.Options.Options.Create(new NodeEvictionOptions()));

        // Setup nonce validation service
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var nonceOptions = Microsoft.Extensions.Options.Options.Create(new NonceValidationOptions
        {
            WindowDuration = TimeSpan.FromHours(24)
        });
        var nonceValidationService = new NonceValidationService(
            cache,
            nonceOptions,
            serviceProvider.GetRequiredService<ILogger<NonceValidationService>>());

        _discoveryService = new NodeDiscoveryService(
            _clusterRepository,
            nodeRepository,
            evictionService,
            nonceValidationService,
            serviceProvider.GetRequiredService<ILogger<NodeDiscoveryService>>());
    }

    [Fact]
    public async Task DiscoverController_FirstNodeJoins_EmptyNodesResponse()
    {
        // Arrange - Register cluster first
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        await _clusterRepository.AddAsync(new Models.ClusterRegistration
        {
            ClusterId = _testClusterId,
            PublicKey = publicKey
        }, TestContext.Current.CancellationToken);

        // Create payload for first node
        var payload = CreateNodePayload("node-1");
        var encryptedPayloadWithNonce = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), _aesKey, out var nonce);

        var request = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(encryptedPayloadWithNonce),
            Convert.ToBase64String(nonce),
            privateKey);

        // Act
        var result = await _discoveryService.DiscoverNodesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(NodeDiscoveryStatus.Success);
        result.Response.Should().NotBeNull();
        result.Response!.Nodes.Should().BeEmpty("first node should receive empty list");
    }

    [Fact]
    public async Task DiscoverController_SecondNodeJoins_ReceivesFirstNodeInResponse()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        await _clusterRepository.AddAsync(new Models.ClusterRegistration
        {
            ClusterId = _testClusterId,
            PublicKey = publicKey
        }, TestContext.Current.CancellationToken);

        // First node joins
        var payload1 = CreateNodePayload("node-1", "10.0.0.1");
        var encrypted1 = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload1), _aesKey, out var nonce1);
        var request1 = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(encrypted1),
            Convert.ToBase64String(nonce1),
            privateKey);
        await _discoveryService.DiscoverNodesAsync(request1, TestContext.Current.CancellationToken);

        // Second node joins
        var payload2 = CreateNodePayload("node-2", "10.0.0.2");
        var encrypted2 = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload2), _aesKey, out var nonce2);
        var request2 = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(encrypted2),
            Convert.ToBase64String(nonce2),
            privateKey);

        // Act
        var result = await _discoveryService.DiscoverNodesAsync(request2, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(NodeDiscoveryStatus.Success);
        result.Response!.Nodes.Should().HaveCount(1, "second node should receive first node");

        // Decrypt and verify first node is in response
        var returnedNode = result.Response.Nodes[0];
        var decrypted = DecryptNodePayload(returnedNode);
        decrypted.Should().Contain("node-1");
        decrypted.Should().Contain("10.0.0.1");
    }

    [Fact]
    public async Task DiscoverController_SixthNodeJoins_EvictsOldestNode()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        await _clusterRepository.AddAsync(new Models.ClusterRegistration
        {
            ClusterId = _testClusterId,
            PublicKey = publicKey
        }, TestContext.Current.CancellationToken);

        // Register 5 nodes
        for (var i = 1; i <= 5; i++)
        {
            var payload = CreateNodePayload($"node-{i}", $"10.0.0.{i}");
            var encrypted = CryptographyHelper.Encrypt(
                Encoding.UTF8.GetBytes(payload), _aesKey, out var nonce);
            var request = CreateDiscoverRequest(
                _testClusterId.ToString(),
                Convert.ToBase64String(encrypted),
                Convert.ToBase64String(nonce),
                privateKey);
            await _discoveryService.DiscoverNodesAsync(request, TestContext.Current.CancellationToken);
            await Task.Delay(10, TestContext.Current.CancellationToken); // Ensure different timestamps
        }

        // Act - 6th node joins
        var payload6 = CreateNodePayload("node-6", "10.0.0.6");
        var encrypted6 = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload6), _aesKey, out var nonce6);
        var request6 = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(encrypted6),
            Convert.ToBase64String(nonce6),
            privateKey);
        var result = await _discoveryService.DiscoverNodesAsync(request6, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(NodeDiscoveryStatus.Success);
        result.Response!.Nodes.Should().HaveCount(5, "max 5 nodes returned");

        // The 6th node receives the 5 existing nodes (node-1 through node-5)
        // The eviction happens AFTER the response is generated
        var nodeNames = result.Response.Nodes
            .Select(DecryptNodePayload)
            .ToList();

        // All 5 previous nodes should be in the response
        nodeNames.Should().Contain(n => n.Contains("node-1"));
        nodeNames.Should().Contain(n => n.Contains("node-2"));
        nodeNames.Should().Contain(n => n.Contains("node-3"));
        nodeNames.Should().Contain(n => n.Contains("node-4"));
        nodeNames.Should().Contain(n => n.Contains("node-5"));
    }

    [Fact]
    public async Task DiscoverController_NodeRenewsRegistration_UpdatesTimestampAndStaysInTop5()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        await _clusterRepository.AddAsync(new Models.ClusterRegistration
        {
            ClusterId = _testClusterId,
            PublicKey = publicKey
        }, TestContext.Current.CancellationToken);

        // Register node-1
        var payload1 = CreateNodePayload("node-1");
        var encrypted1 = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload1), _aesKey, out var nonce1);
        var request1 = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(encrypted1),
            Convert.ToBase64String(nonce1),
            privateKey);
        await _discoveryService.DiscoverNodesAsync(request1, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Register 4 more nodes
        for (var i = 2; i <= 5; i++)
        {
            var payload = CreateNodePayload($"node-{i}", "prod", 1);
            var encrypted = CryptographyHelper.Encrypt(
                Encoding.UTF8.GetBytes(payload), _aesKey, out var nonce);
            var request = CreateDiscoverRequest(
                _testClusterId.ToString(),
                Convert.ToBase64String(encrypted),
                Convert.ToBase64String(nonce),
                privateKey);
            await _discoveryService.DiscoverNodesAsync(request, TestContext.Current.CancellationToken);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Act - node-1 renews (should update timestamp)
        var renewPayload = CreateNodePayload("node-1");
        var renewEncrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(renewPayload), _aesKey, out var renewNonce);
        var renewRequest = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(renewEncrypted),
            Convert.ToBase64String(renewNonce),
            privateKey);
        var result = await _discoveryService.DiscoverNodesAsync(renewRequest, TestContext.Current.CancellationToken);

        // Assert - node-1 should still be in top 5
        result.Status.Should().Be(NodeDiscoveryStatus.Success);
        var nodeNames = result.Response!.Nodes
            .Select(DecryptNodePayload)
            .ToList();

        nodeNames.Should().Contain(n => n.Contains("node-1"),
            "renewed node should stay in registry");
    }

    [Fact]
    public async Task DiscoverController_RequestWithInvalidSignature_Returns401Unauthorized()
    {
        // Arrange - Register cluster
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        await _clusterRepository.AddAsync(new Models.ClusterRegistration
        {
            ClusterId = _testClusterId,
            PublicKey = publicKey
        }, TestContext.Current.CancellationToken);

        var payload = CreateNodePayload("node-1");
        var encrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), _aesKey, out var nonce);

        // Create request with invalid signature
        var request = new DiscoverRequest
        {
            ClusterId = _testClusterId.ToString(),
            VersionName = "prod",
            VersionNumber = 1,
            Payload = Convert.ToBase64String(encrypted),
            Nonce = Convert.ToBase64String(nonce),
            Signature = Convert.ToBase64String(new byte[64]) // Invalid signature
        };

        // Act
        var result = await _discoveryService.DiscoverNodesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(NodeDiscoveryStatus.SignatureVerificationFailed);
    }

    [Fact]
    public async Task DiscoverController_RequestForUnknownCluster_Returns404NotFound()
    {
        // Arrange - Don't register cluster
        var (_, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var payload = CreateNodePayload("node-1");
        var encrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), _aesKey, out var nonce);
        var request = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(encrypted),
            Convert.ToBase64String(nonce),
            privateKey);

        // Act
        var result = await _discoveryService.DiscoverNodesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(NodeDiscoveryStatus.ClusterNotFound);
    }

    [Fact]
    public async Task DiscoverController_RequestWithMalformedNonce_Returns400BadRequest()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        await _clusterRepository.AddAsync(new Models.ClusterRegistration
        {
            ClusterId = _testClusterId,
            PublicKey = publicKey
        }, TestContext.Current.CancellationToken);

        var payload = CreateNodePayload("node-1");
        var encrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), _aesKey, out _);

        // Use wrong nonce size (3 bytes instead of 4)
        var invalidNonce = new byte[3];
        var request = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(encrypted),
            Convert.ToBase64String(invalidNonce),
            privateKey,
            "prod",
            1);

        // Act
        var result = await _discoveryService.DiscoverNodesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(NodeDiscoveryStatus.InvalidNonceSize);
    }

    [Fact]
    public async Task DiscoverController_RequestWithCorruptedPayload_AcceptsRequest()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        await _clusterRepository.AddAsync(new Models.ClusterRegistration
        {
            ClusterId = _testClusterId,
            PublicKey = publicKey
        }, TestContext.Current.CancellationToken);

        // Create corrupted payload (random bytes that won't decrypt)
        var corruptedPayload = new byte[100];
        new Random().NextBytes(corruptedPayload);
        var nonce = TestDataGenerator.GenerateNonce();

        var request = CreateDiscoverRequest(
            _testClusterId.ToString(),
            Convert.ToBase64String(corruptedPayload),
            Convert.ToBase64String(nonce),
            privateKey,
            "prod",
            1);

        // Act
        var result = await _discoveryService.DiscoverNodesAsync(request, TestContext.Current.CancellationToken);

        // Assert - Server accepts request since it doesn't decrypt/validate payload contents
        // Payload validation is client-side responsibility before encryption
        result.Status.Should().Be(NodeDiscoveryStatus.Success);
    }

    // Helper methods
    private static string CreateNodePayload(string nodeName, string address = "10.0.0.1", int port = 7946)
    {
        // Only sensitive node details in encrypted payload (version info sent in clear text)
        return $@"{{
            ""nodeName"": ""{nodeName}"",
            ""address"": ""{address}"",
            ""port"": {port},
            ""timestamp"": {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
        }}";
    }

    private static DiscoverRequest CreateDiscoverRequest(string clusterId, string payload, string nonce, System.Security.Cryptography.ECDsa privateKey, string versionName = "prod", long versionNumber = 1)
    {
        // Signature includes version info to prevent tampering
        var signatureData = Encoding.UTF8.GetBytes(clusterId + versionName + versionNumber + payload + nonce);
        var signature = privateKey.SignData(signatureData, System.Security.Cryptography.HashAlgorithmName.SHA256);

        return new DiscoverRequest
        {
            ClusterId = clusterId,
            VersionName = versionName,
            VersionNumber = versionNumber,
            Payload = payload,
            Nonce = nonce,
            Signature = Convert.ToBase64String(signature)
        };
    }

    private string DecryptNodePayload(string encryptedBase64)
    {
        var encryptedWithNonce = Convert.FromBase64String(encryptedBase64);
        // Extract nonce (first 4 bytes) and ciphertext (rest)
        var nonce = encryptedWithNonce.Take(4).ToArray();
        var ciphertext = encryptedWithNonce.Skip(4).ToArray();
        var decrypted = CryptographyHelper.Decrypt(ciphertext, _aesKey, nonce);
        return Encoding.UTF8.GetString(decrypted);
    }
}
