using FluentAssertions;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Repositories;
using NSerf.Lighthouse.Services;
using NSerf.Lighthouse.Tests.TestHelpers;

namespace NSerf.Lighthouse.Tests.ClusterRegistration;

/// <summary>
/// Tests for cluster registration endpoint
/// </summary>
public class ClustersControllerTests
{
    private readonly IClusterRepository _repository;
    private readonly ClusterService _service;

    public ClustersControllerTests()
    {
        _repository = new InMemoryClusterRepository();
        _service = new ClusterService(_repository, new TestLogger<ClusterService>());
    }

    [Fact]
    public async Task ClustersController_RegisterCluster_ValidRequest_Returns200AndRegistersCluster()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var request = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.FixedClusterId().ToString(),
            PublicKey = Convert.ToBase64String(publicKey)
        };

        // Act
        var result = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(ClusterRegistrationStatus.Created, "valid request should create cluster");

        // Verify cluster is actually stored
        var stored = await _repository.GetByIdAsync(TestDataGenerator.FixedClusterId(), TestContext.Current.CancellationToken);
        stored.Should().NotBeNull("cluster should be persisted");
        stored.PublicKey.Should().BeEquivalentTo(publicKey);
    }

    [Fact]
    public async Task ClustersController_RegisterSameClusterTwice_Returns200OnSecondCall()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var request = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.FixedClusterId().ToString(),
            PublicKey = Convert.ToBase64String(publicKey)
        };

        // Act - Register twice
        var result1 = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);
        var result2 = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result1.Status.Should().Be(ClusterRegistrationStatus.Created, "first registration should create");
        result2.Status.Should().Be(ClusterRegistrationStatus.AlreadyExists, "second registration should be idempotent");

        // Verify only one cluster exists
        var stored = await _repository.GetByIdAsync(TestDataGenerator.FixedClusterId(), TestContext.Current.CancellationToken);
        stored.Should().NotBeNull();
        stored.PublicKey.Should().BeEquivalentTo(publicKey);
    }

    [Fact]
    public async Task ClustersController_RegisterExistingClusterWithDifferentKey_Returns409Conflict()
    {
        // Arrange
        var (publicKey1, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var (publicKey2, _) = TestDataGenerator.GenerateEcdsaKeyPair();

        var request1 = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.FixedClusterId().ToString(),
            PublicKey = Convert.ToBase64String(publicKey1)
        };

        var request2 = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.FixedClusterId().ToString(),
            PublicKey = Convert.ToBase64String(publicKey2) // Different key
        };

        // Act
        var result1 = await _service.RegisterClusterAsync(request1, TestContext.Current.CancellationToken);
        var result2 = await _service.RegisterClusterAsync(request2, TestContext.Current.CancellationToken);

        // Assert
        result1.Status.Should().Be(ClusterRegistrationStatus.Created);
        result2.Status.Should().Be(ClusterRegistrationStatus.PublicKeyMismatch,
            "different public key for same cluster should be rejected");
        result2.ErrorMessage.Should().Be("public_key_mismatch");

        // Verify original key is preserved
        var stored = await _repository.GetByIdAsync(TestDataGenerator.FixedClusterId(), TestContext.Current.CancellationToken);
        stored!.PublicKey.Should().BeEquivalentTo(publicKey1, "original key should be preserved");
    }

    [Fact]
    public async Task ClustersController_RegisterCluster_InvalidBase64PublicKey_Returns400BadRequest()
    {
        // Arrange
        var request = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.FixedClusterId().ToString(),
            PublicKey = TestDataGenerator.InvalidBase64()
        };

        // Act
        var result = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(ClusterRegistrationStatus.InvalidPublicKey,
            "invalid base64 should be rejected");
        result.ErrorMessage.Should().Be("invalid_base64");

        // Verify cluster was not created
        var stored = await _repository.GetByIdAsync(TestDataGenerator.FixedClusterId(), TestContext.Current.CancellationToken);
        stored.Should().BeNull("invalid request should not create cluster");
    }

    [Fact]
    public async Task ClustersController_RegisterCluster_InvalidGuidFormat_Returns400BadRequest()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var request = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.InvalidGuid(),
            PublicKey = Convert.ToBase64String(publicKey)
        };

        // Act
        var result = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(ClusterRegistrationStatus.InvalidGuidFormat,
            "invalid GUID should be rejected");
        result.ErrorMessage.Should().Be("invalid_guid_format");
    }

    [Fact]
    public async Task ClustersController_PublicKeyNotSpkiFormat_Returns400BadRequest()
    {
        // Arrange - Create a non-SPKI key (just random bytes)
        var invalidKey = new byte[32];
        var request = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.FixedClusterId().ToString(),
            PublicKey = Convert.ToBase64String(invalidKey)
        };

        // Act
        var result = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(ClusterRegistrationStatus.InvalidPublicKey,
            "non-SPKI key should be rejected");
    }

    [Fact]
    public async Task ClustersController_RegisterCluster_EmptyPublicKey_Returns400BadRequest()
    {
        // Arrange
        var request = new RegisterClusterRequest
        {
            ClusterId = TestDataGenerator.FixedClusterId().ToString(),
            PublicKey = string.Empty
        };

        // Act
        var result = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(ClusterRegistrationStatus.InvalidPublicKey);
    }

    [Fact]
    public async Task ClustersController_RegisterCluster_EmptyClusterId_Returns400BadRequest()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        var request = new RegisterClusterRequest
        {
            ClusterId = string.Empty,
            PublicKey = Convert.ToBase64String(publicKey)
        };

        // Act
        var result = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(ClusterRegistrationStatus.InvalidGuidFormat);
    }

    [Fact]
    public async Task ClustersController_RegisterMultipleDifferentClusters_AllSucceed()
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
            var result = await _service.RegisterClusterAsync(request, TestContext.Current.CancellationToken);
            result.Status.Should().Be(ClusterRegistrationStatus.Created);
        }

        // Assert - All should be stored
        foreach (var (id, key) in clusters)
        {
            var stored = await _repository.GetByIdAsync(id, TestContext.Current.CancellationToken);
            stored.Should().NotBeNull();
            stored.PublicKey.Should().BeEquivalentTo(key);
        }
    }

    [Fact]
    public async Task ClustersController_RegisterCluster_NullRequest_HandledGracefully()
    {
        // This would be handled by ASP.NET Core model binding, but test service directly
        // Act & Assert
        var act = async () => await _service.RegisterClusterAsync(null!);
        await act.Should().ThrowAsync<NullReferenceException>("null request should throw");
    }
}
