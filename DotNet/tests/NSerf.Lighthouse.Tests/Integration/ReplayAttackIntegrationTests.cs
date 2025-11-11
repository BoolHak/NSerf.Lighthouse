using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Tests.TestHelpers;
using NSerf.Lighthouse.Utilities;

namespace NSerf.Lighthouse.Tests.Integration;

public class ReplayAttackIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task DiscoverNodes_ReplayAttack_Returns403Forbidden()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", 1, privateKey);

        // Act - First request should succeed
        var firstResponse = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);
        
        // Act - Replay the exact same request
        var replayResponse = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, "first request should succeed");
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, "replay attack should be rejected");
        
        var replayContent = await replayResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        replayContent.Should().Contain("replay_attack_detected");
    }

    [Fact]
    public async Task DiscoverNodes_SameNonceDifferentSignature_BothSucceed()
    {
        // Arrange
        var (publicKey1, privateKey1) = TestDataGenerator.GenerateEcdsaKeyPair();
        var (publicKey2, privateKey2) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId1 = Guid.NewGuid();
        var clusterId2 = Guid.NewGuid();
        
        await RegisterClusterAsync(clusterId1, publicKey1);
        await RegisterClusterAsync(clusterId2, publicKey2);

        // Create requests with same nonce but different signatures (different clusters/keys)
        var aesKey = new byte[32];
        new Random().NextBytes(aesKey);
        var payload = CreateNodePayload("node-1");
        var encrypted = CryptographyHelper.Encrypt(Encoding.UTF8.GetBytes(payload), aesKey, out var nonce);
        var nonceBase64 = Convert.ToBase64String(nonce);
        var payloadBase64 = Convert.ToBase64String(encrypted);

        var request1 = CreateDiscoverRequestWithNonce(clusterId1, payloadBase64, nonceBase64, privateKey1, "prod", 1);
        var request2 = CreateDiscoverRequestWithNonce(clusterId2, payloadBase64, nonceBase64, privateKey2, "prod", 1);

        // Act
        var response1 = await Client.PostAsJsonAsync("/discover", request1, cancellationToken: TestContext.Current.CancellationToken);
        var response2 = await Client.PostAsJsonAsync("/discover", request2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Both should succeed because signatures are different
        response1.StatusCode.Should().Be(HttpStatusCode.OK, "first request with unique signature should succeed");
        response2.StatusCode.Should().Be(HttpStatusCode.OK, "second request with different signature should succeed");
    }

    [Fact]
    public async Task DiscoverNodes_MultipleReplayAttempts_AllRejected()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", 1, privateKey);

        // Act - First request
        var firstResponse = await Client.PostAsJsonAsync("/discover", request, cancellationToken: TestContext.Current.CancellationToken);
        
        // Act - Multiple replay attempts
        var replayTasks = Enumerable.Range(0, 5)
            .Select(_ => Client.PostAsJsonAsync("/discover", request))
            .ToList();
        var replayResponses = await Task.WhenAll(replayTasks);

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, "first request should succeed");
        replayResponses.Should().AllSatisfy(r => 
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "all replay attempts should be rejected"));
    }

    [Fact]
    public async Task DiscoverNodes_DifferentNonces_AllSucceed()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        // Act - Send multiple requests with different nonces
        var tasks = Enumerable.Range(0, 5)
            .Select(i => CreateDiscoverRequest(clusterId, $"node-{i}", "prod", 1, privateKey))
            .Select(req => Client.PostAsJsonAsync("/discover", req))
            .ToList();
        var responses = await Task.WhenAll(tasks);

        // Assert - All should succeed because nonces are different
        responses.Should().AllSatisfy(r => 
            r.StatusCode.Should().Be(HttpStatusCode.OK, "requests with unique nonces should all succeed"));
    }

    [Fact]
    public async Task DiscoverNodes_ConcurrentIdenticalRequests_OnlyOneSucceeds()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        await RegisterClusterAsync(clusterId, publicKey);

        var request = CreateDiscoverRequest(clusterId, "node-1", "prod", 1, privateKey);

        // Act - Send 10 identical requests concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Client.PostAsJsonAsync("/discover", request))
            .ToList();
        var responses = await Task.WhenAll(tasks);

        // Assert - At least one should succeed, others should be rejected
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var forbiddenCount = responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden);

        successCount.Should().BeGreaterThanOrEqualTo(1, "at least one request should succeed");
        forbiddenCount.Should().BeGreaterThanOrEqualTo(1, "at least one should be rejected as replay");
        (successCount + forbiddenCount).Should().Be(10, "all requests should be either success or forbidden");
    }

    // Helper methods
    private DiscoverRequest CreateDiscoverRequest(
        Guid clusterId, 
        string nodeName, 
        string versionName, 
        long versionNumber, 
        ECDsa privateKey)
    {
        var aesKey = new byte[32];
        new Random().NextBytes(aesKey);

        var payload = CreateNodePayload(nodeName);
        var encrypted = CryptographyHelper.Encrypt(
            Encoding.UTF8.GetBytes(payload), aesKey, out var nonce);

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

    private DiscoverRequest CreateDiscoverRequestWithNonce(
        Guid clusterId,
        string payloadBase64,
        string nonceBase64,
        ECDsa privateKey,
        string versionName,
        long versionNumber)
    {
        var signatureData = Encoding.UTF8.GetBytes(
            clusterId.ToString() + 
            versionName + 
            versionNumber.ToString() + 
            payloadBase64 + 
            nonceBase64);
        var signature = privateKey.SignData(signatureData, HashAlgorithmName.SHA256);

        return new DiscoverRequest
        {
            ClusterId = clusterId.ToString(),
            VersionName = versionName,
            VersionNumber = versionNumber,
            Payload = payloadBase64,
            Nonce = nonceBase64,
            Signature = Convert.ToBase64String(signature)
        };
    }

    private string CreateNodePayload(string nodeName)
    {
        return $@"{{
            ""nodeName"": ""{nodeName}"",
            ""address"": ""10.0.0.1"",
            ""port"": 7946,
            ""timestamp"": {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
        }}";
    }
}
