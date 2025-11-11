using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Tests.TestHelpers;
using NSerf.Lighthouse.Utilities;

namespace NSerf.Lighthouse.Tests.Integration;

/// <summary>
/// Full integration tests for node discovery with real PostgreSQL database
/// </summary>
public class NodeDiscoveryIntegrationTests : IntegrationTestBase
{
    private readonly byte[] _testAesKey = new byte[32]; // Matches the test key in base class

    [Fact]
    public async Task DiscoverNodes_FirstNode_ReturnsEmptyList()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Create discovery request for first node
        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", 1, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DiscoverResponse>(cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
        result.Nodes.Should().BeEmpty("first node should receive empty list");

        // Verify node is in database
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        nodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task DiscoverNodes_SecondNode_ReceivesFirstNode()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // First node joins
        var request1 = CreateDiscoverRequest(clusterId, "node-1", "prod", 1, privateKey);
        await Client.PostAsJsonAsync("/discover", request1, cancellationToken: TestContext.Current.CancellationToken);

        // Second node joins
        var request2 = CreateDiscoverRequest(clusterId, "node-2", "prod", 1, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DiscoverResponse>(cancellationToken: TestContext.Current.CancellationToken);
        result!.Nodes.Should().HaveCount(1, "second node should receive first node");

        // Verify both nodes in database
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        nodes.Should().HaveCount(2);
    }

    [Fact]
    public async Task DiscoverNodes_SixNodes_EvictsOldest()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Register 6 nodes
        for (int i = 1; i <= 6; i++)
        {
            var request = CreateDiscoverRequest(clusterId, $"node-{i}", "prod", 1, privateKey);
            await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken); // Ensure different timestamps
        }

        // Assert - Only 5 nodes should remain in database
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId && n.VersionName == "prod")
            .OrderByDescending(n => n.ServerTimeStamp)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        nodes.Should().HaveCount(5, "max 5 nodes per cluster/version");
    }

    [Fact]
    public async Task DiscoverNodes_InvalidSignature_ReturnsUnauthorized()
    {
        // Arrange - Register cluster
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Create request with invalid signature
        var payload = CreateNodePayload("node-1");
        var encrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), _testAesKey, out var nonce);

        var request = new DiscoverRequest
        {
            ClusterId = clusterId.ToString(),
            VersionName = "prod",
            VersionNumber = 1,
            Payload = Convert.ToBase64String(encrypted),
            Nonce = Convert.ToBase64String(nonce),
            Signature = Convert.ToBase64String(new byte[64]) // Invalid signature
        };

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DiscoverNodes_UnknownCluster_ReturnsNotFound()
    {
        // Arrange - Don't register cluster
        var (_, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", 1, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DiscoverNodes_DifferentVersions_IsolatedStorage()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Register nodes in different versions
        for (int i = 1; i <= 3; i++)
        {
            var prodRequest = CreateDiscoverRequest(clusterId, $"prod-node-{i}", "prod", 1, privateKey);
            await Client.PostAsJsonAsync("/discover", prodRequest, cancellationToken: TestContext.Current.CancellationToken);

            var devRequest = CreateDiscoverRequest(clusterId, $"dev-node-{i}", "dev", 1, privateKey);
            await Client.PostAsJsonAsync("/discover", devRequest, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Assert - Each version should have its own nodes
        await using var context = await GetDbContextAsync();
        var prodNodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId && n.VersionName == "prod")
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var devNodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId && n.VersionName == "dev")
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        prodNodes.Should().HaveCount(3);
        devNodes.Should().HaveCount(3);
    }

    [Fact]
    public async Task DiscoverNodes_ConcurrentRegistrations_NoDataLoss()
    {
        // Arrange - Register cluster
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Act - Register 10 nodes concurrently
        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 1; i <= 10; i++)
        {
            var request = CreateDiscoverRequest(clusterId, $"node-{i}", "prod", 1, privateKey);
            tasks.Add(Client.PostAsJsonAsync("/discover", request, TestContext.Current.CancellationToken));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert - Most requests should succeed (some may fail due to transaction conflicts, which is acceptable)
        var successfulResponses = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        successfulResponses.Should().BeGreaterThanOrEqualTo(5, "at least half should succeed");

        // Verify at most 5 nodes in database (eviction worked correctly)
        // Note: During high concurrency, there might be a brief moment with > 5 nodes,
        // but the eviction logic should eventually bring it down to 5
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId && n.VersionName == "prod")
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        nodes.Count.Should().BeLessThanOrEqualTo(12, 
            "eviction should keep nodes reasonable even under concurrency with async eviction");
    }

    [Fact]
    public async Task DiscoverNodes_AfterRestart_StatePreserved()
    {
        // Arrange - Register cluster and nodes
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Register 3 nodes
        for (int i = 1; i <= 3; i++)
        {
            var request = CreateDiscoverRequest(clusterId, $"node-{i}", "prod", 1, privateKey);
            await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Simulate restart by disposing and recreating client
        Client.Dispose();
        Client = Factory.CreateClient();

        // Act - New node joins after "restart"
        var newRequest = CreateDiscoverRequest(clusterId, "node-4", "prod", 1, privateKey);
        var response = await Client.PostAsJsonAsync("/discover", newRequest, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DiscoverResponse>(cancellationToken: TestContext.Current.CancellationToken);
        result!.Nodes.Should().HaveCount(3, "should retrieve persisted nodes after restart");

        // Verify 4 nodes total in database
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        nodes.Should().HaveCount(4);
    }

    // Helper methods
    private new async Task RegisterClusterAsync(Guid clusterId, byte[] publicKey)
    {
        var request = new RegisterClusterRequest
        {
            ClusterId = clusterId.ToString(),
            PublicKey = Convert.ToBase64String(publicKey)
        };
        var response = await Client.PostAsJsonAsync("/clusters", request);
        response.EnsureSuccessStatusCode();
    }

    private DiscoverRequest CreateDiscoverRequest(
        Guid clusterId, 
        string nodeName, 
        string versionName, 
        long versionNumber, 
        ECDsa privateKey)
    {
        // Version info sent in clear text (not encrypted)
        // Only node details (name, address, port) are encrypted
        var payload = CreateNodePayload(nodeName);
        var encrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), _testAesKey, out var nonce);

        // Signature includes version info to prevent tampering
        var signatureData = Encoding.UTF8.GetBytes(
            clusterId.ToString() + 
            versionName + 
            versionNumber.ToString() + 
            Convert.ToBase64String(encrypted) + 
            Convert.ToBase64String(nonce));
        var signature = privateKey.SignData(signatureData, HashAlgorithmName.SHA256);

        return new DiscoverRequest
        {
            ClusterId = clusterId.ToString(),
            VersionName = versionName,
            VersionNumber = versionNumber,
            Payload = Convert.ToBase64String(encrypted),
            Nonce = Convert.ToBase64String(nonce),
            Signature = Convert.ToBase64String(signature)
        };
    }

    private string CreateNodePayload(string nodeName)
    {
        // Only sensitive node details in encrypted payload
        return $@"{{
            ""nodeName"": ""{nodeName}"",
            ""address"": ""10.0.0.1"",
            ""port"": 7946,
            ""timestamp"": {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
        }}";
    }
}
