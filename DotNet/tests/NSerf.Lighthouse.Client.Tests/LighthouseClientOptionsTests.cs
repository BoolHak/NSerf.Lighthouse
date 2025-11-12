using FluentAssertions;
using Xunit;

namespace NSerf.Lighthouse.Client.Tests;

public class LighthouseClientOptionsTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        // Act
        var options = new LighthouseClientOptions();

        // Assert
        options.BaseUrl.Should().Be(string.Empty);
        options.ClusterId.Should().Be(string.Empty);
        options.PrivateKey.Should().Be(string.Empty);
        options.AesKey.Should().Be(string.Empty);
        options.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void SectionName_HasCorrectValue()
    {
        // Assert
        LighthouseClientOptions.SectionName.Should().Be("LighthouseClient");
    }

    [Fact]
    public void BaseUrl_CanBeSet()
    {
        // Arrange
        var options = new LighthouseClientOptions();
        var expectedUrl = "https://lighthouse.example.com";

        // Act
        options.BaseUrl = expectedUrl;

        // Assert
        options.BaseUrl.Should().Be(expectedUrl);
    }

    [Fact]
    public void ClusterId_CanBeSet()
    {
        // Arrange
        var options = new LighthouseClientOptions();
        var expectedClusterId = Guid.NewGuid().ToString();

        // Act
        options.ClusterId = expectedClusterId;

        // Assert
        options.ClusterId.Should().Be(expectedClusterId);
    }

    [Fact]
    public void PrivateKey_CanBeSet()
    {
        // Arrange
        var options = new LighthouseClientOptions();
        var expectedKey = "base64encodedprivatekey";

        // Act
        options.PrivateKey = expectedKey;

        // Assert
        options.PrivateKey.Should().Be(expectedKey);
    }

    [Fact]
    public void AesKey_CanBeSet()
    {
        // Arrange
        var options = new LighthouseClientOptions();
        var expectedKey = "base64encodedaeskey";

        // Act
        options.AesKey = expectedKey;

        // Assert
        options.AesKey.Should().Be(expectedKey);
    }

    [Fact]
    public void TimeoutSeconds_CanBeSet()
    {
        // Arrange
        var options = new LighthouseClientOptions();
        var expectedTimeout = 60;

        // Act
        options.TimeoutSeconds = expectedTimeout;

        // Assert
        options.TimeoutSeconds.Should().Be(expectedTimeout);
    }

    [Fact]
    public void Options_CanBeInitializedWithObjectInitializer()
    {
        // Act
        var options = new LighthouseClientOptions
        {
            BaseUrl = "https://lighthouse.example.com",
            ClusterId = Guid.NewGuid().ToString(),
            PrivateKey = "privatekey123",
            AesKey = "aeskey456",
            TimeoutSeconds = 45
        };

        // Assert
        options.BaseUrl.Should().Be("https://lighthouse.example.com");
        options.ClusterId.Should().NotBeEmpty();
        options.PrivateKey.Should().Be("privatekey123");
        options.AesKey.Should().Be("aeskey456");
        options.TimeoutSeconds.Should().Be(45);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void TimeoutSeconds_AcceptsValidValues(int timeout)
    {
        // Arrange
        var options = new LighthouseClientOptions();

        // Act
        options.TimeoutSeconds = timeout;

        // Assert
        options.TimeoutSeconds.Should().Be(timeout);
    }

    [Fact]
    public void Options_AllPropertiesAreSettable()
    {
        // Arrange
        var options = new LighthouseClientOptions();

        // Act & Assert - Should not throw
        var act = () =>
        {
            options.BaseUrl = "https://test.com";
            options.ClusterId = "cluster-123";
            options.PrivateKey = "key1";
            options.AesKey = "key2";
            options.TimeoutSeconds = 100;
        };

        act.Should().NotThrow();
    }
}
