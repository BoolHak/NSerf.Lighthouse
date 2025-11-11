using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Tests.TestHelpers;

namespace NSerf.Lighthouse.Tests.Integration;

/// <summary>
/// Full integration tests for cluster registration with real PostgreSQL database
/// </summary>
public class ClusterRegistrationIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task RegisterCluster_WithValidData_PersistsToDatabase()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        var request = new RegisterClusterRequest
        {
            ClusterId = clusterId.ToString(),
            PublicKey = Convert.ToBase64String(publicKey)
        };

        // Act
        var response = await Client.PostAsJsonAsync("/clusters", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verify in database
        await using var context = await GetDbContextAsync();
        var cluster = await context.Clusters.FirstOrDefaultAsync(c => c.ClusterId == clusterId, cancellationToken: TestContext.Current.CancellationToken);
        cluster.Should().NotBeNull();
        cluster!.PublicKey.Should().BeEquivalentTo(publicKey);
    }

    [Fact]
    public async Task RegisterCluster_Twice_IsIdempotent()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();
        var request = new RegisterClusterRequest
        {
            ClusterId = clusterId.ToString(),
            PublicKey = Convert.ToBase64String(publicKey)
        };

        // Act - Register twice
        var response1 = await Client.PostAsJsonAsync("/clusters", request, cancellationToken: TestContext.Current.CancellationToken);
        var response2 = await Client.PostAsJsonAsync("/clusters", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify only one record in database
        await using var context = await GetDbContextAsync();
        var count = await context.Clusters.CountAsync(c => c.ClusterId == clusterId, cancellationToken: TestContext.Current.CancellationToken);
        count.Should().Be(1, "should not create duplicate clusters");
    }

    [Fact]
    public async Task RegisterCluster_WithDifferentKey_ReturnsConflict()
    {
        // Arrange
        var (publicKey1, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var (publicKey2, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var clusterId = Guid.NewGuid();

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

        // Act
        var response1 = await Client.PostAsJsonAsync("/clusters", request1, cancellationToken: TestContext.Current.CancellationToken);
        var response2 = await Client.PostAsJsonAsync("/clusters", request2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Verify original key is preserved
        await using var context = await GetDbContextAsync();
        var cluster = await context.Clusters.FirstOrDefaultAsync(c => c.ClusterId == clusterId, cancellationToken: TestContext.Current.CancellationToken);
        cluster!.PublicKey.Should().BeEquivalentTo(publicKey1);
    }

    [Fact]
    public async Task RegisterCluster_InvalidGuid_ReturnsBadRequest()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var request = new RegisterClusterRequest
        {
            ClusterId = "not-a-valid-guid",
            PublicKey = Convert.ToBase64String(publicKey)
        };

        // Act
        var response = await Client.PostAsJsonAsync("/clusters", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterCluster_InvalidBase64_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterClusterRequest
        {
            ClusterId = Guid.NewGuid().ToString(),
            PublicKey = "not-valid-base64!!!"
        };

        // Act
        var response = await Client.PostAsJsonAsync("/clusters", request, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterMultipleClusters_AllPersistIndependently()
    {
        // Arrange
        var clusters = new List<(Guid id, byte[] key)>();
        for (var i = 0; i < 5; i++)
        {
            var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
            clusters.Add((Guid.NewGuid(), publicKey));
        }

        // Act - Register all clusters
        foreach (var (id, key) in clusters)
        {
            var request = new RegisterClusterRequest
            {
                ClusterId = id.ToString(),
                PublicKey = Convert.ToBase64String(key)
            };
            var response = await Client.PostAsJsonAsync("/clusters", request, cancellationToken: TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Assert - All should be in database
        await using var context = await GetDbContextAsync();
        var dbClusters = await context.Clusters.ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        dbClusters.Count.Should().BeGreaterThanOrEqualTo(5);

        foreach (var (id, key) in clusters)
        {
            var cluster = dbClusters.FirstOrDefault(c => c.ClusterId == id);
            cluster.Should().NotBeNull();
            cluster!.PublicKey.Should().BeEquivalentTo(key);
        }
    }
}
