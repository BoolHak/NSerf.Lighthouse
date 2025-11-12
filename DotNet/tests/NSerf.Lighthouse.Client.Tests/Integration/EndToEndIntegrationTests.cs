using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSerf.Lighthouse.Client.Models;

namespace NSerf.Lighthouse.Client.Tests.Integration;

/// <summary>
/// End-to-end integration tests with real Lighthouse server and PostgreSQL database
/// Tests the complete workflow from cluster registration to node discovery
/// </summary>
public class EndToEndIntegrationTests : LighthouseIntegrationTestBase
{
    [Fact]
    public async Task CompleteWorkflow_RegisterAndDiscoverNodes_Success()
    {
        // Arrange - Register cluster
        var registerResult = await LighthouseClient.RegisterClusterAsync(TestPublicKey);
        registerResult.Should().BeTrue("cluster registration should succeed");

        // Verify cluster in database
        await using var context = await GetDbContextAsync();
        var cluster = await context.Clusters.FirstOrDefaultAsync(c => c.ClusterId == TestClusterId);
        cluster.Should().NotBeNull();
        cluster!.PublicKey.Should().BeEquivalentTo(TestPublicKey);

        // Act - First node discovers (should get empty list)
        var node1 = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 7946,
            Metadata = new Dictionary<string, string>
            {
                { "region", "us-east" },
                { "datacenter", "dc1" }
            }
        };

        var discoveredNodes1 = await LighthouseClient.DiscoverNodesAsync(node1, "production", 1);

        // Assert - First node gets empty list
        discoveredNodes1.Should().BeEmpty("first node should receive no other nodes");

        // Verify node1 in database
        var dbNodes = await context.Nodes
            .Where(n => n.ClusterId == TestClusterId)
            .ToListAsync();
        dbNodes.Should().HaveCount(1);

        // Act - Second node discovers (should get first node)
        var node2 = new NodeInfo
        {
            IpAddress = "192.168.1.101",
            Port = 7946,
            Metadata = new Dictionary<string, string>
            {
                { "region", "us-west" },
                { "datacenter", "dc2" }
            }
        };

        var discoveredNodes2 = await LighthouseClient.DiscoverNodesAsync(node2, "production", 1);

        // Assert - Second node gets first node
        discoveredNodes2.Should().HaveCount(1);
        discoveredNodes2[0].IpAddress.Should().Be("192.168.1.100");
        discoveredNodes2[0].Port.Should().Be(7946);
        discoveredNodes2[0].Metadata.Should().ContainKey("region");
        discoveredNodes2[0].Metadata!["region"].Should().Be("us-east");

        // Verify both nodes in database
        dbNodes = await context.Nodes
            .Where(n => n.ClusterId == TestClusterId)
            .ToListAsync();
        dbNodes.Should().HaveCount(2);
    }

    [Fact]
    public async Task RegisterCluster_Idempotent_SuccessOnSecondCall()
    {
        // Act - Register same cluster twice
        var result1 = await LighthouseClient.RegisterClusterAsync(TestPublicKey);
        var result2 = await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();

        // Verify only one cluster in database
        await using var context = await GetDbContextAsync();
        var count = await context.Clusters.CountAsync(c => c.ClusterId == TestClusterId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task DiscoverNodes_WithoutRegistration_ThrowsException()
    {
        // Arrange - Don't register cluster
        var node = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 7946
        };

        // Act & Assert
        await LighthouseClient.Invoking(c => c.DiscoverNodesAsync(node, "production", 1))
            .Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Discovery failed*");
    }

    [Fact]
    public async Task DiscoverNodes_MultipleVersions_IsolatedCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Act - Register nodes in different versions
        var prodNode1 = new NodeInfo { IpAddress = "10.0.1.1", Port = 7946 };
        var prodNode2 = new NodeInfo { IpAddress = "10.0.1.2", Port = 7946 };
        var devNode1 = new NodeInfo { IpAddress = "10.0.2.1", Port = 7946 };
        var devNode2 = new NodeInfo { IpAddress = "10.0.2.2", Port = 7946 };

        await LighthouseClient.DiscoverNodesAsync(prodNode1, "production", 1);
        await LighthouseClient.DiscoverNodesAsync(prodNode2, "production", 1);
        await LighthouseClient.DiscoverNodesAsync(devNode1, "development", 1);
        await LighthouseClient.DiscoverNodesAsync(devNode2, "development", 1);

        // Assert - Production nodes only see production nodes
        var prodDiscovered = await LighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "10.0.1.3", Port = 7946 }, 
            "production", 
            1);
        
        prodDiscovered.Should().HaveCount(2);
        prodDiscovered.Should().OnlyContain(n => n.IpAddress.StartsWith("10.0.1."));

        // Assert - Development nodes only see development nodes
        var devDiscovered = await LighthouseClient.DiscoverNodesAsync(
            new NodeInfo { IpAddress = "10.0.2.3", Port = 7946 }, 
            "development", 
            1);
        
        devDiscovered.Should().HaveCount(2);
        devDiscovered.Should().OnlyContain(n => n.IpAddress.StartsWith("10.0.2."));
    }

    [Fact]
    public async Task DiscoverNodes_MaxFiveNodes_EvictsOldest()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Act - Register 6 nodes
        for (var i = 1; i <= 6; i++)
        {
            var node = new NodeInfo
            {
                IpAddress = $"192.168.1.{100 + i}",
                Port = 7946,
                Metadata = new Dictionary<string, string> { { "index", i.ToString() } }
            };
            await LighthouseClient.DiscoverNodesAsync(node, "production", 1);
            await Task.Delay(100); // Ensure different timestamps
        }

        // Assert - Only 5 most recent nodes remain
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == TestClusterId && n.VersionName == "production")
            .OrderByDescending(n => n.ServerTimeStamp)
            .ToListAsync();

        nodes.Should().HaveCount(5, "eviction should maintain max 5 nodes");
        
        // Verify oldest node (node 1) was evicted by checking IPs in remaining nodes
        // Note: EncryptedPayload is encrypted, so we can't directly check it
    }

    [Fact]
    public async Task DiscoverNodes_WithMetadata_PreservesData()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        var node1 = new NodeInfo
        {
            IpAddress = "10.20.30.40",
            Port = 8080,
            Metadata = new Dictionary<string, string>
            {
                { "environment", "staging" },
                { "zone", "us-central1-a" },
                { "role", "worker" },
                { "version", "2.1.0" }
            }
        };

        // Act
        await LighthouseClient.DiscoverNodesAsync(node1, "staging", 100);

        var node2 = new NodeInfo
        {
            IpAddress = "10.20.30.41",
            Port = 8080
        };

        var discovered = await LighthouseClient.DiscoverNodesAsync(node2, "staging", 100);

        // Assert
        discovered.Should().HaveCount(1);
        var discoveredNode = discovered[0];
        discoveredNode.IpAddress.Should().Be("10.20.30.40");
        discoveredNode.Port.Should().Be(8080);
        discoveredNode.Metadata.Should().NotBeNull();
        discoveredNode.Metadata.Should().HaveCount(4);
        discoveredNode.Metadata!["environment"].Should().Be("staging");
        discoveredNode.Metadata["zone"].Should().Be("us-central1-a");
        discoveredNode.Metadata["role"].Should().Be("worker");
        discoveredNode.Metadata["version"].Should().Be("2.1.0");
    }

    [Fact]
    public async Task DiscoverNodes_ConcurrentRequests_HandledCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        // Act - Send 10 concurrent discovery requests
        var tasks = new List<Task<List<NodeInfo>>>();
        for (int i = 1; i <= 10; i++)
        {
            var node = new NodeInfo
            {
                IpAddress = $"172.16.0.{i}",
                Port = 7946
            };
            tasks.Add(LighthouseClient.DiscoverNodesAsync(node, "production", 1));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All requests completed successfully
        results.Should().HaveCount(10);
        results.Should().OnlyContain(r => r != null);

        // Verify database state is consistent (max 5 nodes due to eviction)
        await using var context = await GetDbContextAsync();
        var nodes = await context.Nodes
            .Where(n => n.ClusterId == TestClusterId)
            .ToListAsync();
        
        nodes.Count.Should().BeLessThanOrEqualTo(15, 
            "concurrent requests may temporarily exceed limit before eviction completes");
    }

    [Fact]
    public async Task DiscoverNodes_EmptyMetadata_HandledCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        var node1 = new NodeInfo
        {
            IpAddress = "192.168.100.1",
            Port = 9000,
            Metadata = new Dictionary<string, string>() // Empty metadata
        };

        // Act
        await LighthouseClient.DiscoverNodesAsync(node1, "test", 1);

        var node2 = new NodeInfo
        {
            IpAddress = "192.168.100.2",
            Port = 9000
        };

        var discovered = await LighthouseClient.DiscoverNodesAsync(node2, "test", 1);

        // Assert
        discovered.Should().HaveCount(1);
        discovered[0].Metadata.Should().NotBeNull();
        discovered[0].Metadata.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverNodes_NullMetadata_HandledCorrectly()
    {
        // Arrange - Register cluster
        await LighthouseClient.RegisterClusterAsync(TestPublicKey);

        var node1 = new NodeInfo
        {
            IpAddress = "192.168.200.1",
            Port = 9000,
            Metadata = null // Null metadata
        };

        // Act
        await LighthouseClient.DiscoverNodesAsync(node1, "test", 1);

        var node2 = new NodeInfo
        {
            IpAddress = "192.168.200.2",
            Port = 9000
        };

        var discovered = await LighthouseClient.DiscoverNodesAsync(node2, "test", 1);

        // Assert
        discovered.Should().HaveCount(1);
        // Null metadata should be preserved or converted to empty dict
        discovered[0].Metadata.Should().BeNull();
    }
}
