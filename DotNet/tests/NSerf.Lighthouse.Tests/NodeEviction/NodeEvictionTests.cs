using FluentAssertions;
using NSerf.Lighthouse.Models;
using NSerf.Lighthouse.Repositories;
using NSerf.Lighthouse.Tests.TestHelpers;

namespace NSerf.Lighthouse.Tests.NodeEviction;

/// <summary>
/// CRITICAL: Tests for node eviction logic - highest risk component
/// If these tests fail, the entire registry breaks under load
/// </summary>
public class NodeEvictionTests
{
    private readonly INodeRepository _repository;
    private readonly Guid _testClusterId;
    private const string TestVersionName = "prod";
    private const long TestVersionNumber = 1;

    public NodeEvictionTests()
    {
        _repository = new InMemoryNodeRepository();
        _testClusterId = TestDataGenerator.FixedClusterId();
    }

    [Fact]
    public async Task NodeRepository_AddSixthNode_EvictsOldestByServerTimestamp()
    {
        // Arrange - Add 5 nodes with incrementing timestamps
        var baseTimestamp = TestDataGenerator.FixedTimestampTicks;
        for (var i = 0; i < 5; i++)
        {
            var node = new NodeRegistration
            {
                ClusterId = _testClusterId,
                VersionName = TestVersionName,
                VersionNumber = TestVersionNumber,
                EncryptedPayload = [(byte)i],
                ServerTimeStamp = baseTimestamp + i // Incrementing timestamps
            };
            await _repository.AddNodeAsync(node, TestContext.Current.CancellationToken);
        }

        // Act - Add 6th node with newest timestamp
        var sixthNode = new NodeRegistration
        {
            ClusterId = _testClusterId,
            VersionName = TestVersionName,
            VersionNumber = TestVersionNumber,
            EncryptedPayload = [99],
            ServerTimeStamp = baseTimestamp + 10 // Newest timestamp
        };
        await _repository.AddNodeAsync(sixthNode, TestContext.Current.CancellationToken);

        // Assert - Should have exactly 5 nodes
        var remainingNodes = await _repository.GetNodesAsync(
            _testClusterId, TestVersionName, TestVersionNumber, 10, TestContext.Current.CancellationToken);

        remainingNodes.Should().HaveCount(5, "max 5 nodes per cluster/version");

        // Oldest node (timestamp = baseTimestamp) should be evicted
        remainingNodes.Should().NotContain(n => n.ServerTimeStamp == baseTimestamp,
            "oldest node should be evicted");

        // Newest node should be present
        remainingNodes.Should().Contain(n => n.ServerTimeStamp == baseTimestamp + 10,
            "newest node should be added");

        // Remaining nodes should be the 4 newest + the new one
        var expectedTimestamps = new[] 
        { 
            baseTimestamp + 1, 
            baseTimestamp + 2, 
            baseTimestamp + 3, 
            baseTimestamp + 4, 
            baseTimestamp + 10 
        };
        remainingNodes.Select(n => n.ServerTimeStamp).Should().BeEquivalentTo(expectedTimestamps);
    }

    [Fact]
    public async Task NodeRepository_SimultaneousRegistrations_EvictionResilientToRaceConditions()
    {
        // Arrange - Prepare 10 nodes to register concurrently
        var baseTimestamp = TestDataGenerator.FixedTimestampTicks;
        var tasks = new List<Task>();

        // Act - Register 10 nodes in parallel
        for (var i = 0; i < 10; i++)
        {
            var index = i;
            var task = Task.Run(async () =>
            {
                var node = new NodeRegistration
                {
                    ClusterId = _testClusterId,
                    VersionName = TestVersionName,
                    VersionNumber = TestVersionNumber,
                    EncryptedPayload = [(byte)index],
                    ServerTimeStamp = baseTimestamp + index
                };
                await _repository.AddNodeAsync(node);
            }, TestContext.Current.CancellationToken);
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert - Should have exactly 5 nodes (no data loss, no corruption)
        var finalNodes = await _repository.GetNodesAsync(_testClusterId, TestVersionName, TestVersionNumber, 10, TestContext.Current.CancellationToken);

        finalNodes.Should().HaveCount(5, "eviction should maintain max 5 nodes even under concurrent load");

        // All remaining nodes should have unique timestamps
        finalNodes.Select(n => n.ServerTimeStamp).Should().OnlyHaveUniqueItems(
            "no duplicate nodes should exist");

        // All remaining nodes should be the 5 newest (highest timestamps)
        var expectedNewestTimestamps = Enumerable.Range(5, 5)
            .Select(i => baseTimestamp + i)
            .ToList();
        finalNodes.Select(n => n.ServerTimeStamp).Should().BeEquivalentTo(expectedNewestTimestamps,
            "only the 5 newest nodes should remain");
    }

    [Fact]
    public async Task NodeRepository_NodesWithSameTimestamp_EvictsArbitrarilyButConsistently()
    {
        // Arrange - Add 6 nodes with same timestamp (edge case)
        var sameTimestamp = TestDataGenerator.FixedTimestampTicks;

        for (var i = 0; i < 6; i++)
        {
            var node = new NodeRegistration
            {
                ClusterId = _testClusterId,
                VersionName = TestVersionName,
                VersionNumber = TestVersionNumber,
                EncryptedPayload = [(byte)i],
                ServerTimeStamp = sameTimestamp // All same timestamp
            };
            await _repository.AddNodeAsync(node, TestContext.Current.CancellationToken);
        }

        // Assert - Should have exactly 5 nodes
        var nodes = await _repository.GetNodesAsync(_testClusterId, TestVersionName, TestVersionNumber, 10, TestContext.Current.CancellationToken);

        nodes.Should().HaveCount(5, "max 5 nodes enforced even with identical timestamps");

        // All should have the same timestamp
        nodes.Should().AllSatisfy(n => n.ServerTimeStamp.Should().Be(sameTimestamp));
    }

    [Fact]
    public async Task NodeRepository_DifferentVersions_IsolatedEviction()
    {
        // Arrange - Add 5 nodes to version "prod"
        var baseTimestamp = TestDataGenerator.FixedTimestampTicks;
        for (var i = 0; i < 5; i++)
        {
            await _repository.AddNodeAsync(new NodeRegistration
            {
                ClusterId = _testClusterId,
                VersionName = "prod",
                VersionNumber = 1,
                EncryptedPayload = [(byte)i],
                ServerTimeStamp = baseTimestamp + i
            }, TestContext.Current.CancellationToken);
        }

        // Add 5 nodes to version "dev"
        for (var i = 0; i < 5; i++)
        {
            await _repository.AddNodeAsync(new NodeRegistration
            {
                ClusterId = _testClusterId,
                VersionName = "dev",
                VersionNumber = 1,
                EncryptedPayload = [(byte)(i + 100)],
                ServerTimeStamp = baseTimestamp + i + 100
            }, TestContext.Current.CancellationToken);
        }

        // Act - Add 6th node to "prod" (should evict from prod only)
        await _repository.AddNodeAsync(new NodeRegistration
        {
            ClusterId = _testClusterId,
            VersionName = "prod",
            VersionNumber = 1,
            EncryptedPayload = [99],
            ServerTimeStamp = baseTimestamp + 10
        }, TestContext.Current.CancellationToken);

        // Assert - "prod" should have 5 nodes
        var prodNodes = await _repository.GetNodesAsync(
            _testClusterId, "prod", 1, 10, TestContext.Current.CancellationToken);
        prodNodes.Should().HaveCount(5, "prod version should have max 5 nodes");

        // "dev" should still have 5 nodes (unaffected)
        var devNodes = await _repository.GetNodesAsync(
            _testClusterId, "dev", 1, 10, TestContext.Current.CancellationToken);
        devNodes.Should().HaveCount(5, "dev version should be unaffected by prod eviction");
    }

    [Fact]
    public async Task NodeRepository_GetNodes_ReturnsInDescendingTimestampOrder()
    {
        // Arrange - Add nodes with random order timestamps
        var timestamps = new[] { 100L, 300L, 200L, 500L, 400L };
        foreach (var ts in timestamps)
        {
            await _repository.AddNodeAsync(new NodeRegistration
            {
                ClusterId = _testClusterId,
                VersionName = TestVersionName,
                VersionNumber = TestVersionNumber,
                EncryptedPayload = [(byte)ts],
                ServerTimeStamp = ts
            }, TestContext.Current.CancellationToken);
        }

        // Act
        var nodes = await _repository.GetNodesAsync(_testClusterId, TestVersionName, TestVersionNumber, 10, TestContext.Current.CancellationToken);

        // Assert - Should be ordered by timestamp descending (newest first)
        nodes.Select(n => n.ServerTimeStamp).Should().BeInDescendingOrder(
            "nodes should be returned with newest first");

        nodes.First().ServerTimeStamp.Should().Be(500L, "newest node should be first");
        nodes.Last().ServerTimeStamp.Should().Be(100L, "oldest node should be last");
    }

    [Fact]
    public async Task NodeRepository_MaxCountLimit_RespectsRequestedLimit()
    {
        // Arrange - Add 5 nodes
        var baseTimestamp = TestDataGenerator.FixedTimestampTicks;
        for (var i = 0; i < 5; i++)
        {
            await _repository.AddNodeAsync(new NodeRegistration
            {
                ClusterId = _testClusterId,
                VersionName = TestVersionName,
                VersionNumber = TestVersionNumber,
                EncryptedPayload = [(byte)i],
                ServerTimeStamp = baseTimestamp + i
            }, TestContext.Current.CancellationToken);
        }

        // Act - Request only 3 nodes
        var nodes = await _repository.GetNodesAsync(
            _testClusterId, TestVersionName, TestVersionNumber, 3, TestContext.Current.CancellationToken);

        // Assert
        nodes.Should().HaveCount(3, "should respect maxCount parameter");

        // Should return the 3 newest
        var expectedTimestamps = new[] { baseTimestamp + 4, baseTimestamp + 3, baseTimestamp + 2 };
        nodes.Select(n => n.ServerTimeStamp).Should().BeEquivalentTo(expectedTimestamps);
    }

    [Fact]
    public async Task NodeRepository_EmptyRepository_ReturnsEmptyList()
    {
        // Act
        var nodes = await _repository.GetNodesAsync(
            _testClusterId, TestVersionName, TestVersionNumber, 10, TestContext.Current.CancellationToken);

        // Assert
        nodes.Should().BeEmpty("no nodes have been registered");
    }

    [Fact]
    public async Task NodeRepository_NonExistentVersion_ReturnsEmptyList()
    {
        // Arrange - Add nodes to "prod"
        await _repository.AddNodeAsync(new NodeRegistration
        {
            ClusterId = _testClusterId,
            VersionName = "prod",
            VersionNumber = 1,
            EncryptedPayload = [1],
            ServerTimeStamp = 100
        }, TestContext.Current.CancellationToken);

        // Act - Query for "dev"
        var nodes = await _repository.GetNodesAsync(_testClusterId, "dev", 1, 10, TestContext.Current.CancellationToken);

        // Assert
        nodes.Should().BeEmpty("queried version does not exist");
    }
}
