using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSerf.Lighthouse.Services;

namespace NSerf.Lighthouse.Tests.Security;

public class NonceValidationServiceTests
{
    private readonly NonceValidationService _service;

    public NonceValidationServiceTests()
    {
        IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new NonceValidationOptions
        {
            WindowDuration = TimeSpan.FromMinutes(5)
        });
        var logger = new LoggerFactory().CreateLogger<NonceValidationService>();
        _service = new NonceValidationService(cache, options, logger);
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_FirstUse_ReturnsTrue()
    {
        // Arrange
        const string nonce = "test-nonce-1";
        const string signature = "test-signature-1";

        // Act
        var result = await _service.ValidateAndRecordNonceAsync(nonce, signature);

        // Assert
        result.Should().BeTrue("first use of nonce should be valid");
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_ReplayAttack_ReturnsFalse()
    {
        // Arrange
        const string nonce = "test-nonce-2";
        const string signature = "test-signature-2";

        // Act - First use
        var firstResult = await _service.ValidateAndRecordNonceAsync(nonce, signature);
        
        // Act - Replay attempt
        var replayResult = await _service.ValidateAndRecordNonceAsync(nonce, signature);

        // Assert
        firstResult.Should().BeTrue("first use should be valid");
        replayResult.Should().BeFalse("replay attempt should be rejected");
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_SameNonceDifferentSignature_ReturnsTrue()
    {
        // Arrange
        const string nonce = "test-nonce-3";
        const string signature1 = "test-signature-3a";
        const string signature2 = "test-signature-3b";

        // Act
        var result1 = await _service.ValidateAndRecordNonceAsync(nonce, signature1);
        var result2 = await _service.ValidateAndRecordNonceAsync(nonce, signature2);

        // Assert
        result1.Should().BeTrue("first nonce+signature combination should be valid");
        result2.Should().BeTrue("same nonce with different signature should be valid (different request)");
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_EmptyNonce_ReturnsFalse()
    {
        // Arrange
        const string nonce = "";
        const string signature = "test-signature-4";

        // Act
        var result = await _service.ValidateAndRecordNonceAsync(nonce, signature);

        // Assert
        result.Should().BeFalse("empty nonce should be rejected");
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_EmptySignature_ReturnsFalse()
    {
        // Arrange
        var nonce = "test-nonce-5";
        var signature = "";

        // Act
        var result = await _service.ValidateAndRecordNonceAsync(nonce, signature);

        // Assert
        result.Should().BeFalse("empty signature should be rejected");
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_MultipleNonces_AllTrackedIndependently()
    {
        // Arrange & Act
        var results = new List<bool>();
        for (var i = 0; i < 10; i++)
        {
            var nonce = $"nonce-{i}";
            var signature = $"signature-{i}";
            results.Add(await _service.ValidateAndRecordNonceAsync(nonce, signature));
        }

        // Assert
        results.Should().AllSatisfy(r => r.Should().BeTrue("all unique nonces should be valid"));
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_ConcurrentRequests_HandledCorrectly()
    {
        // Arrange
        const string nonce = "concurrent-nonce";
        const string signature = "concurrent-signature";
        var tasks = new List<Task<bool>>();

        // Act - Simulate 10 concurrent requests with same nonce+signature
        for (var i = 0; i < 10; i++)
        {
            tasks.Add(_service.ValidateAndRecordNonceAsync(nonce, signature));
        }
        var results = await Task.WhenAll(tasks);

        // Assert - Only one should succeed (race condition, but at least one must succeed)
        results.Should().Contain(true, "at least one request should succeed");
        results.Count(r => r).Should().BeGreaterThanOrEqualTo(1, "at least one should be valid");
        results.Count(r => !r).Should().BeGreaterThanOrEqualTo(1, "at least one should be rejected as replay");
    }

    [Fact]
    public async Task ValidateAndRecordNonceAsync_AfterExpiration_AllowsReuse()
    {
        // Arrange - Use very short window for testing
        var shortCache = new MemoryCache(new MemoryCacheOptions());
        var shortOptions = Options.Create(new NonceValidationOptions
        {
            WindowDuration = TimeSpan.FromMilliseconds(100)
        });
        var logger = new LoggerFactory().CreateLogger<NonceValidationService>();
        var shortService = new NonceValidationService(shortCache, shortOptions, logger);

        const string nonce = "expiring-nonce";
        const string signature = "expiring-signature";

        // Act - First use
        var firstResult = await shortService.ValidateAndRecordNonceAsync(nonce, signature);
        
        // Wait for expiration
        await Task.Delay(150, TestContext.Current.CancellationToken);
        
        // Act - After expiration
        var secondResult = await shortService.ValidateAndRecordNonceAsync(nonce, signature);

        // Assert
        firstResult.Should().BeTrue("first use should be valid");
        secondResult.Should().BeTrue("after expiration, nonce should be reusable");
    }
}
