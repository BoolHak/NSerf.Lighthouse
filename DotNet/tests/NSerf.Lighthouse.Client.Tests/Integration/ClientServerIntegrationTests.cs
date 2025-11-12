using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSerf.Lighthouse.Client.Models;
using NSerf.Lighthouse.DTOs;

namespace NSerf.Lighthouse.Client.Tests.Integration;

/// <summary>
/// Integration tests focusing on client-server communication, error handling, and edge cases
/// </summary>
public class ClientServerIntegrationTests : LighthouseIntegrationTestBase
{
    [Fact]
    public async Task RegisterCluster_WithDifferentPublicKey_ReturnsFailure()
    {
        // Arrange - Register with first key
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Generate different key pair
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var differentPublicKey = ecdsa.ExportSubjectPublicKeyInfo();

        // Act - Try to register with different key
        var result = await LighthouseClient.RegisterClusterAsync(differentPublicKey);

        // Assert
        result.Should().BeFalse("registration with different key should fail");
    }

    [Fact]
    public async Task DiscoverNodes_InvalidSignature_ReturnsError()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Create request with invalid signature by directly calling the API
        var request = new DiscoverRequest
        {
            ClusterId = TestClusterId.ToString(),
            VersionName = "production",
            VersionNumber = 1,
            Payload = Convert.ToBase64String(new byte[100]),
            Nonce = Convert.ToBase64String(new byte[4]),
            Signature = Convert.ToBase64String(new byte[64]) // Invalid signature
        };

        // Act
        var response = await ServerHttpClient.PostAsJsonAsync("/discover", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DiscoverNodes_UnregisteredCluster_ReturnsNotFound()
    {
        // Arrange - Create client with unregistered cluster ID
        var unregisteredClusterId = Guid.NewGuid();
        
        // Manually create request to bypass client validation
        var request = new DiscoverRequest
        {
            ClusterId = unregisteredClusterId.ToString(),
            VersionName = "production",
            VersionNumber = 1,
            Payload = Convert.ToBase64String(new byte[100]),
            Nonce = Convert.ToBase64String(new byte[4]),
            Signature = Convert.ToBase64String(new byte[64])
        };

        // Act
        var response = await ServerHttpClient.PostAsJsonAsync("/discover", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegisterCluster_InvalidPublicKey_ReturnsBadRequest()
    {
        // Arrange - Create invalid request
        var request = new RegisterClusterRequest
        {
            ClusterId = Guid.NewGuid().ToString(),
            PublicKey = "invalid-base64-key!!!"
        };

        // Act
        var response = await ServerHttpClient.PostAsJsonAsync("/clusters", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DiscoverNodes_LargeMetadata_HandledCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Create node with large metadata
        var largeMetadata = new Dictionary<string, string>();
        for (int i = 0; i < 50; i++)
        {
            largeMetadata[$"key_{i}"] = $"value_{i}_" + new string('x', 100);
        }

        var node1 = new NodeInfo
        {
            IpAddress = "10.0.0.1",
            Port = 7946,
            Metadata = largeMetadata
        };

        // Act
        await LighthouseClient.DiscoverNodesAsync(node1, "production", 1);

        var node2 = new NodeInfo
        {
            IpAddress = "10.0.0.2",
            Port = 7946
        };

        var discovered = await LighthouseClient.DiscoverNodesAsync(node2, "production", 1);

        // Assert
        discovered.Should().HaveCount(1);
        discovered[0].Metadata.Should().HaveCount(50);
        discovered[0].Metadata!["key_0"].Should().Contain("value_0_");
    }

    [Fact]
    public async Task DiscoverNodes_RapidSuccessiveRequests_MaintainsConsistency()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        var node = new NodeInfo
        {
            IpAddress = "192.168.1.50",
            Port = 7946,
            Metadata = new Dictionary<string, string> { { "test", "rapid" } }
        };

        // Act - Send 5 rapid requests from same node
        var tasks = new List<Task<List<NodeInfo>>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(LighthouseClient.DiscoverNodesAsync(node, "production", 1));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All requests should succeed
        results.Should().HaveCount(5);
        results.Should().OnlyContain(r => r != null);

        // Verify node entries exist (may have multiple due to race conditions)
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == TestClusterId)
            .ToListAsync();

        nodes.Should().NotBeEmpty("at least one node should be registered");
        nodes.Count.Should().BeLessThanOrEqualTo(5, "should not exceed number of requests");
    }

    [Fact]
    public async Task DiscoverNodes_DifferentVersionNumbers_IsolatedCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Act - Register nodes with different version numbers
        var node1 = new NodeInfo { IpAddress = "10.1.0.1", Port = 7946 };
        var node2 = new NodeInfo { IpAddress = "10.1.0.2", Port = 7946 };
        var node3 = new NodeInfo { IpAddress = "10.2.0.1", Port = 7946 };
        var node4 = new NodeInfo { IpAddress = "10.2.0.2", Port = 7946 };

        await LighthouseClient.DiscoverNodesAsync(node1, "production", 1);
        await LighthouseClient.DiscoverNodesAsync(node2, "production", 1);
        await LighthouseClient.DiscoverNodesAsync(node3, "production", 2);
        await LighthouseClient.DiscoverNodesAsync(node4, "production", 2);

        // Assert - Version 1 nodes only see version 1
        var v1Discovered = await LighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "10.1.0.3", Port = 7946 },
            "production",
            1);

        v1Discovered.Should().HaveCount(2);
        v1Discovered.Should().OnlyContain(n => n.IpAddress.StartsWith("10.1."));

        // Assert - Version 2 nodes only see version 2
        var v2Discovered = await LighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "10.2.0.3", Port = 7946 },
            "production",
            2);

        v2Discovered.Should().HaveCount(2);
        v2Discovered.Should().OnlyContain(n => n.IpAddress.StartsWith("10.2."));
    }

    [Fact]
    public async Task DiscoverNodes_SpecialCharactersInMetadata_PreservedCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        var node1 = new NodeInfo
        {
            IpAddress = "192.168.1.1",
            Port = 7946,
            Metadata = new Dictionary<string, string>
            {
                { "description", "Node with special chars: @#$%^&*(){}[]|\\:;\"'<>,.?/~`" },
                { "unicode", "Unicode: 你好世界 🚀 🎉" },
                { "json", "{\"nested\":\"value\"}" }
            }
        };

        // Act
        await LighthouseClient.DiscoverNodesAsync(node1, "test", 1);

        var node2 = new NodeInfo { IpAddress = "192.168.1.2", Port = 7946 };
        var discovered = await LighthouseClient.DiscoverNodesAsync(node2, "test", 1);

        // Assert
        discovered.Should().HaveCount(1);
        discovered[0].Metadata.Should().ContainKey("description");
        discovered[0].Metadata!["description"].Should().Contain("@#$%^&*()");
        discovered[0].Metadata.Should().ContainKey("unicode");
        discovered[0].Metadata!["unicode"].Should().Contain("你好世界");
        discovered[0].Metadata.Should().ContainKey("json");
        discovered[0].Metadata!["json"].Should().Contain("nested");
    }

    [Fact]
    public async Task DiscoverNodes_AfterDatabaseRestart_StatePreserved()
    {
        // Arrange - Register cluster and nodes
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        for (int i = 1; i <= 3; i++)
        {
            var node = new NodeInfo
            {
                IpAddress = $"172.20.0.{i}",
                Port = 7946,
                Metadata = new Dictionary<string, string> { { "index", i.ToString() } }
            };
            await LighthouseClient.DiscoverNodesAsync(node, "production", 1);
        }

        // Simulate restart by recreating HTTP client and Lighthouse client
        ServerHttpClient.Dispose();
        ServerHttpClient = ServerFactory.CreateClient();
        
        // Recreate Lighthouse client with new HTTP client
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<LighthouseClient>();
        var options = Microsoft.Extensions.Options.Options.Create(ClientOptions);
        LighthouseClient = new LighthouseClient(ServerHttpClient, options, logger);

        // Act - New node joins after "restart"
        var newNode = new NodeInfo
        {
            IpAddress = "172.20.0.4",
            Port = 7946
        };

        var discovered = await LighthouseClient.DiscoverNodesAsync(newNode, "production", 1);

        // Assert - Should retrieve all previously registered nodes
        discovered.Should().HaveCount(3);
        discovered.Should().Contain(n => n.IpAddress == "172.20.0.1");
        discovered.Should().Contain(n => n.IpAddress == "172.20.0.2");
        discovered.Should().Contain(n => n.IpAddress == "172.20.0.3");
    }

    [Fact]
    public async Task DiscoverNodes_HighPortNumbers_HandledCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        var node1 = new NodeInfo
        {
            IpAddress = "10.0.0.1",
            Port = 65535, // Max port number
            Metadata = new Dictionary<string, string> { { "port", "max" } }
        };

        // Act
        await LighthouseClient.DiscoverNodesAsync(node1, "production", 1);

        var node2 = new NodeInfo { IpAddress = "10.0.0.2", Port = 1024 };
        var discovered = await LighthouseClient.DiscoverNodesAsync(node2, "production", 1);

        // Assert
        discovered.Should().HaveCount(1);
        discovered[0].Port.Should().Be(65535);
    }

    [Fact]
    public async Task RegisterCluster_MultipleClustersConcurrently_AllSucceed()
    {
        // Arrange - Create multiple cluster registrations
        var tasks = new List<Task<bool>>();
        var clusterKeys = new List<(Guid id, byte[] publicKey)>();

        for (int i = 0; i < 5; i++)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
            var clusterId = Guid.NewGuid();
            clusterKeys.Add((clusterId, publicKey));
        }

        // Act - Register all clusters concurrently
        foreach (var (id, key) in clusterKeys)
        {
            var request = new RegisterClusterRequest
            {
                ClusterId = id.ToString(),
                PublicKey = Convert.ToBase64String(key)
            };
            tasks.Add(ServerHttpClient.PostAsJsonAsync("/clusters", request)
                .ContinueWith(t => t.Result.IsSuccessStatusCode));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r);

        // Verify all clusters in database
        await using var context = await GetDbContextAsync();
        var clusters = await context.Clusters.ToListAsync();
        clusters.Count.Should().BeGreaterThanOrEqualTo(5);
    }
}
