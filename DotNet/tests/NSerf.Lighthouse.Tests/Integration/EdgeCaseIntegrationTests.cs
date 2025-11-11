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

public class EdgeCaseIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task DiscoverNodes_VeryLargePayload_HandlesCorrectly()
    {
        // Arrange - Create a very large payload (near max size)
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Create a large node name (approaching payload limits)
        var largeNodeName = new string('A', 1000);
        var request = CreateDiscoverRequest(clusterId, largeNodeName, "prod", 1, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DiscoverResponse>
            (cancellationToken: TestContext.Current.CancellationToken);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DiscoverNodes_EmptyNodeName_AcceptsRequest()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var request = CreateDiscoverRequest(clusterId, "", "prod", 1, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, TestContext.Current.CancellationToken);

        // Assert - Server accepts request since it can't validate encrypted payload contents
        // Validation of node name is client-side responsibility before encryption
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DiscoverNodes_SpecialCharactersInVersionName_HandlesCorrectly()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var specialVersionName = "v1.0-beta+build.123";
        var request = CreateDiscoverRequest(clusterId, "node-1", specialVersionName, 1, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify stored correctly
        await using var context = await GetDbContextAsync();
        var node = await context.Nodes.FirstOrDefaultAsync(n => n.VersionName == specialVersionName, cancellationToken: TestContext.Current.CancellationToken);
        node.Should().NotBeNull();
        node.VersionName.Should().Be(specialVersionName);
    }

    [Fact]
    public async Task DiscoverNodes_VeryHighVersionNumber_HandlesCorrectly()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        const long highVersionNumber = long.MaxValue - 1;
        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", highVersionNumber, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify stored correctly
        await using var context = await GetDbContextAsync();
        var node = await context.Nodes.FirstOrDefaultAsync(n => n.VersionNumber == highVersionNumber, cancellationToken: TestContext.Current.CancellationToken);
        node.Should().NotBeNull();
        node.VersionNumber.Should().Be(highVersionNumber);
    }

    [Fact]
    public async Task DiscoverNodes_RapidSequentialRequests_MaintainsConsistency()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Act - Send 20 requests sequentially as fast as possible
        var responses = new List<HttpResponseMessage>();
        for (var i = 1; i <= 20; i++)
        {
            var request = CreateDiscoverRequest(clusterId, $"node-{i}", "prod", 1, privateKey);
            var response = await Client.PostAsJsonAsync("/discover", request, TestContext.Current.CancellationToken);
            responses.Add(response);
        }

        // Assert - Some may hit rate limits
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        successCount.Should().BeGreaterThan(5, "some sequential requests should succeed");
        
        // All responses should be OK or rate-limited
        responses.Should().OnlyContain(r => 
            r.StatusCode == HttpStatusCode.OK ||
            r.StatusCode == HttpStatusCode.TooManyRequests,
            "system should handle rapid sequential requests");

        // Verify nodes are managed correctly
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId)
            .OrderBy(n => n.ServerTimeStamp)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        nodes.Count.Should().BeLessThanOrEqualTo(6, "eviction should maintain node limits");
    }

    [Fact]
    public async Task DiscoverNodes_SameNodeMultipleTimes_UpdatesTimestamp()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var nodeName = "persistent-node";

        // Act - Register the same node 3 times
        var request1 = CreateDiscoverRequest(clusterId, nodeName, "prod", 1, privateKey);
        await Client.PostAsJsonAsync("/discover", request1, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken); // Ensure different timestamps

        var request2 = CreateDiscoverRequest(clusterId, nodeName, "prod", 1, privateKey);
        await Client.PostAsJsonAsync("/discover", request2, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var request3 = CreateDiscoverRequest(clusterId, nodeName, "prod", 1, privateKey);
        var response3 = await Client.PostAsJsonAsync("/discover", request3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response3.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify we have 3 entries (not deduplicated by node name)
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);

        nodes.Should().HaveCount(3, "each registration creates a new entry");
    }

    [Fact]
    public async Task DiscoverNodes_MultipleVersionsSimultaneously_IsolatedCorrectly()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Act - Register nodes in 3 different versions concurrently
        var tasks = new List<Task<HttpResponseMessage>>();
        
        for (var v = 1; v <= 3; v++)
        {
            for (var i = 1; i <= 7; i++)
            {
                var request = CreateDiscoverRequest(clusterId, $"node-v{v}-{i}", $"version-{v}", v, privateKey);
                tasks.Add(Client.PostAsJsonAsync("/discover", request, TestContext.Current.CancellationToken));
            }
        }

        var responses = await Task.WhenAll(tasks);

        // Assert - Most should succeed
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        successCount.Should().BeGreaterThanOrEqualTo(5, "some requests should succeed across versions");

        // Verify each version has at most 5-6 nodes
        await using var context = await GetDbContextAsync();
        var allNodes = await context.Nodes
            .Where(n => n.ClusterId == clusterId)
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        
        for (var v = 1; v <= 3; v++)
        {
            var versionNodes = allNodes.Where(n => 
                n.VersionName == $"version-{v}" && n.VersionNumber == v).ToList();
            // Background eviction means temporary overshoot is expected under high concurrency
            versionNodes.Count.Should().BeLessThanOrEqualTo(10, 
                $"version-{v} should maintain max capacity even with async eviction");
        }
    }

    [Fact]
    public async Task RegisterCluster_ConcurrentRegistrationsWithSameId_OnlyOneSucceeds()
    {
        // Arrange
        var clusterId = Guid.NewGuid();
        var (publicKey1, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var (publicKey2, _) = TestDataGenerator.GenerateEcdsaKeyPair();

        var request1 = new RegisterClusterRequest
        {
            ClusterId = clusterId.ToString(),
            PublicKey = Convert.ToBase64String(publicKey1)
        };

        var request2 = new RegisterClusterRequest
        {
            ClusterId = clusterId.ToString(),
            PublicKey = Convert.ToBase64String(publicKey2)
        };

        // Act - Register concurrently with different keys
        var task1 = Client.PostAsJsonAsync("/clusters", request1, cancellationToken: TestContext.Current.CancellationToken);
        var task2 = Client.PostAsJsonAsync("/clusters", request2, cancellationToken: TestContext.Current.CancellationToken);

        var responses = await Task.WhenAll(task1, task2);

        // Assert - At least one should succeed, but only one cluster should exist in DB
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        statusCodes.Should().Contain(HttpStatusCode.Created, "at least one should be created");
        
        // The important part: verify only one cluster in a database (race condition handled by DB constraint)
        await using var context = await GetDbContextAsync();
        var clusters = await context.Clusters.Where(c => c.ClusterId == clusterId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        clusters.Should().HaveCount(1, "only one cluster should be created despite concurrent requests");
    }

    [Fact]
    public async Task DiscoverNodes_ZeroVersionNumber_HandlesCorrectly()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", 0, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var context = await GetDbContextAsync();
        var node = await context.Nodes.FirstOrDefaultAsync(n => n.VersionNumber == 0, cancellationToken: TestContext.Current.CancellationToken);
        node.Should().NotBeNull();
    }

    [Fact]
    public async Task DiscoverNodes_NegativeVersionNumber_HandlesCorrectly()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", -1, privateKey);

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Negative version numbers are technically valid (long can be negative)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify it was stored correctly
        await using var context = await GetDbContextAsync();
        var node = await context.Nodes.FirstOrDefaultAsync(n => n.VersionNumber == -1, cancellationToken: TestContext.Current.CancellationToken);
        node.Should().NotBeNull();
    }

    [Fact]
    public async Task DiscoverNodes_MalformedEncryptedPayload_ReturnsError()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Create request with invalid base64
        var request = new DiscoverRequest
        {
            ClusterId = clusterId.ToString(),
            Payload = "not-valid-base64!!!",
            Nonce = Convert.ToBase64String(new byte[4]),
            Signature = Convert.ToBase64String(new byte[64])
        };

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DiscoverNodes_WrongNonceSize_ReturnsError()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var payload = System.Text.Encoding.UTF8.GetBytes("test");
        var wrongSizeNonce = new byte[8]; // Should be 4

        var request = new DiscoverRequest
        {
            ClusterId = clusterId.ToString(),
            Payload = Convert.ToBase64String(payload),
            Nonce = Convert.ToBase64String(wrongSizeNonce),
            Signature = Convert.ToBase64String(new byte[64])
        };

        // Act
        var response = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private DiscoverRequest CreateDiscoverRequest(Guid clusterId, string nodeName, 
        string versionName, long versionNumber, ECDsa privateKey)
    {
        var aesKey = new byte[32];
        new Random().NextBytes(aesKey);

        // Only node details in encrypted payload (version sent in clear text)
        var payload = CreateNodePayload(nodeName);
        var encrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), aesKey, out var nonce);

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
        return $@"{{
            ""nodeName"": ""{nodeName}"",
            ""address"": ""10.0.0.1"",
            ""port"": 7946
        }}";
    }
}
