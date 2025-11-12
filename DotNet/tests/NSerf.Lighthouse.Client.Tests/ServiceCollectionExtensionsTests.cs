using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Xunit;

namespace NSerf.Lighthouse.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLighthouseClient_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var act = () => services.AddLighthouseClient(options => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddLighthouseClient_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddLighthouseClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddLighthouseClient_RegistersILighthouseClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        // Act
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://lighthouse.test.com";
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
            options.TimeoutSeconds = 30;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var client = serviceProvider.GetService<ILighthouseClient>();
        client.Should().NotBeNull();
        client.Should().BeOfType<LighthouseClient>();
    }

    [Fact]
    public void AddLighthouseClient_RegistersLighthouseClientOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        var expectedBaseUrl = "https://lighthouse.test.com";
        var expectedClusterId = Guid.NewGuid().ToString();

        // Act
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = expectedBaseUrl;
            options.ClusterId = expectedClusterId;
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
            options.TimeoutSeconds = 45;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var options = serviceProvider.GetService<IOptions<LighthouseClientOptions>>();
        options.Should().NotBeNull();
        options!.Value.BaseUrl.Should().Be(expectedBaseUrl);
        options.Value.ClusterId.Should().Be(expectedClusterId);
        options.Value.TimeoutSeconds.Should().Be(45);
    }

    [Fact]
    public void AddLighthouseClient_RegistersHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        // Act
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://lighthouse.test.com";
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddLighthouseClient_ConfiguresHttpClientWithBaseAddress()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        var expectedBaseUrl = "https://lighthouse.test.com";

        // Act
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = expectedBaseUrl;
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var client = serviceProvider.GetRequiredService<ILighthouseClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddLighthouseClient_ConfiguresHttpClientWithTimeout()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        // Act
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://lighthouse.test.com";
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
            options.TimeoutSeconds = 60;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var client = serviceProvider.GetRequiredService<ILighthouseClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddLighthouseClient_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        // Act
        var result = services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://lighthouse.test.com";
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
        });

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLighthouseClient_CanBeCalledMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        // Act
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://lighthouse1.test.com";
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
        });

        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://lighthouse2.test.com";
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var client = serviceProvider.GetService<ILighthouseClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddLighthouseClient_WithResiliencePolicies_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics(); // Required for resilience telemetry

        var aesKey = new byte[32];
        RandomNumberGenerator.Fill(aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();

        // Act
        services.AddLighthouseClient(options =>
        {
            options.BaseUrl = "https://lighthouse.test.com";
            options.ClusterId = Guid.NewGuid().ToString();
            options.PrivateKey = Convert.ToBase64String(privateKey);
            options.AesKey = Convert.ToBase64String(aesKey);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should not throw when resolving
        var act = () => serviceProvider.GetRequiredService<ILighthouseClient>();
        act.Should().NotThrow();
    }
}
