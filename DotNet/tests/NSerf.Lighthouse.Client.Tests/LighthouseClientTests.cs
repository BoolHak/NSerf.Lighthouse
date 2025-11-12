using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NSerf.Lighthouse.Client.Models;

namespace NSerf.Lighthouse.Client.Tests;

public class LighthouseClientTests
{
    private readonly Mock<ILogger<LighthouseClient>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly LighthouseClientOptions _options;
    private readonly byte[] _aesKey;
    private readonly byte[] _privateKey;

    public LighthouseClientTests()
    {
        _loggerMock = new Mock<ILogger<LighthouseClient>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        // Generate valid keys
        _aesKey = new byte[32];
        RandomNumberGenerator.Fill(_aesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privateKey = ecdsa.ExportPkcs8PrivateKey();

        _options = new LighthouseClientOptions
        {
            BaseUrl = "https://lighthouse.test.com",
            ClusterId = Guid.NewGuid().ToString(),
            PrivateKey = Convert.ToBase64String(_privateKey),
            AesKey = Convert.ToBase64String(_aesKey),
            TimeoutSeconds = 30
        };
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new LighthouseClient(
            null!,
            Options.Create(_options),
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsException()
    {
        // Act & Assert
        var act = () => new LighthouseClient(
            _httpClient,
            null!,
            _loggerMock.Object);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new LighthouseClient(
            _httpClient,
            Options.Create(_options),
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithEmptyBaseUrl_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new LighthouseClientOptions
        {
            BaseUrl = "",
            ClusterId = Guid.NewGuid().ToString(),
            PrivateKey = Convert.ToBase64String(_privateKey),
            AesKey = Convert.ToBase64String(_aesKey)
        };

        // Act & Assert
        var act = () => new LighthouseClient(
            _httpClient,
            Options.Create(invalidOptions),
            _loggerMock.Object);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*BaseUrl is required*");
    }

    [Fact]
    public void Constructor_WithEmptyClusterId_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new LighthouseClientOptions
        {
            BaseUrl = "https://lighthouse.test.com",
            ClusterId = "",
            PrivateKey = Convert.ToBase64String(_privateKey),
            AesKey = Convert.ToBase64String(_aesKey)
        };

        // Act & Assert
        var act = () => new LighthouseClient(
            _httpClient,
            Options.Create(invalidOptions),
            _loggerMock.Object);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ClusterId is required*");
    }

    [Fact]
    public void Constructor_WithEmptyPrivateKey_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new LighthouseClientOptions
        {
            BaseUrl = "https://lighthouse.test.com",
            ClusterId = Guid.NewGuid().ToString(),
            PrivateKey = "",
            AesKey = Convert.ToBase64String(_aesKey)
        };

        // Act & Assert
        var act = () => new LighthouseClient(
            _httpClient,
            Options.Create(invalidOptions),
            _loggerMock.Object);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*PrivateKey is required*");
    }

    [Fact]
    public void Constructor_WithEmptyAesKey_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new LighthouseClientOptions
        {
            BaseUrl = "https://lighthouse.test.com",
            ClusterId = Guid.NewGuid().ToString(),
            PrivateKey = Convert.ToBase64String(_privateKey),
            AesKey = ""
        };

        // Act & Assert
        var act = () => new LighthouseClient(
            _httpClient,
            Options.Create(invalidOptions),
            _loggerMock.Object);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*AesKey is required*");
    }

    [Fact]
    public void Constructor_WithInvalidAesKeyLength_ThrowsArgumentException()
    {
        // Arrange
        var invalidAesKey = new byte[16]; // Wrong size should be 32
        RandomNumberGenerator.Fill(invalidAesKey);

        var invalidOptions = new LighthouseClientOptions
        {
            BaseUrl = "https://lighthouse.test.com",
            ClusterId = Guid.NewGuid().ToString(),
            PrivateKey = Convert.ToBase64String(_privateKey),
            AesKey = Convert.ToBase64String(invalidAesKey)
        };

        // Act & Assert
        var act = () => new LighthouseClient(
            _httpClient,
            Options.Create(invalidOptions),
            _loggerMock.Object);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*AES key must be 32 bytes*");
    }

    [Fact]
    public async Task RegisterClusterAsync_WithSuccessResponse_ReturnsTrue()
    {
        // Arrange
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("/clusters")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act
        var result = await client.RegisterClusterAsync(publicKey);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterClusterAsync_WithErrorResponse_ReturnsFalse()
    {
        // Arrange
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("Invalid request")
            });

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act
        var result = await client.RegisterClusterAsync(publicKey);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterClusterAsync_WithHttpException_ThrowsException()
    {
        // Arrange
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act & Assert
        await client.Invoking(c => c.RegisterClusterAsync(publicKey))
            .Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Network error*");
    }

    [Fact]
    public async Task DiscoverNodesAsync_WithValidResponse_ReturnsDecryptedNodes()
    {
        // Arrange
        var currentNode = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 8080,
            Metadata = new Dictionary<string, string> { { "region", "us-east" } }
        };

        var otherNode = new NodeInfo
        {
            IpAddress = "192.168.1.101",
            Port = 8080,
            Metadata = new Dictionary<string, string> { { "region", "us-west" } }
        };

        // Encrypt the other node
        var nodeJson = JsonSerializer.SerializeToUtf8Bytes(otherNode);
        var encryptedNode = Client.Cryptography.CryptoHelper.Encrypt(nodeJson, _aesKey, out var nonce);
        
        // Combine nonce and encrypted data as the server would
        var combined = new byte[nonce.Length + encryptedNode.Length];
        Array.Copy(nonce, 0, combined, 0, nonce.Length);
        Array.Copy(encryptedNode, 0, combined, nonce.Length, encryptedNode.Length);

        var responseContent = JsonSerializer.Serialize(new
        {
            Nodes = new[] { Convert.ToBase64String(combined) }
        });

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("/discover")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act
        var result = await client.DiscoverNodesAsync(currentNode, "v1.0", 1);

        // Assert
        result.Should().HaveCount(1);
        result[0].IpAddress.Should().Be("192.168.1.101");
        result[0].Port.Should().Be(8080);
        result[0].Metadata.Should().ContainKey("region");
    }

    [Fact]
    public async Task DiscoverNodesAsync_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var currentNode = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 8080
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            Nodes = Array.Empty<string>()
        });

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act
        var result = await client.DiscoverNodesAsync(currentNode, "v1.0", 1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverNodesAsync_WithErrorResponse_ThrowsHttpRequestException()
    {
        // Arrange
        var currentNode = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 8080
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("Invalid signature")
            });

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act & Assert
        await client.Invoking(c => c.DiscoverNodesAsync(currentNode, "v1.0", 1))
            .Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Discovery failed*");
    }

    [Fact]
    public async Task DiscoverNodesAsync_WithInvalidEncryptedData_SkipsInvalidNodes()
    {
        // Arrange
        var currentNode = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 8080
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            Nodes = new[] { "invalid-base64-data!!!" }
        });

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act
        var result = await client.DiscoverNodesAsync(currentNode, "v1.0", 1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverNodesAsync_SendsCorrectPayload()
    {
        // Arrange
        var currentNode = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 8080
        };

        HttpRequestMessage? capturedRequest = null;

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { Nodes = Array.Empty<string>() }))
            });

        var client = new LighthouseClient(_httpClient, Options.Create(_options), _loggerMock.Object);

        // Act
        await client.DiscoverNodesAsync(currentNode, "v1.0", 1);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.ToString().Should().Contain("/discover");

        var content = await capturedRequest.Content!.ReadAsStringAsync();
        // JSON serialization uses camelCase by default
        content.Should().Contain("clusterId");
        content.Should().Contain("versionName");
        content.Should().Contain("versionNumber");
        content.Should().Contain("payload");
        content.Should().Contain("nonce");
        content.Should().Contain("signature");
    }
}
