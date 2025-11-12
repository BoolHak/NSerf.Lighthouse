using FluentAssertions;
using NSerf.Lighthouse.Client.Models;
using Xunit;

namespace NSerf.Lighthouse.Client.Tests.Models;

public class NodeInfoTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        // Act
        var nodeInfo = new NodeInfo();

        // Assert
        nodeInfo.IpAddress.Should().Be(string.Empty);
        nodeInfo.Port.Should().Be(0);
        nodeInfo.Metadata.Should().BeNull();
    }

    [Fact]
    public void IpAddress_CanBeSet()
    {
        // Arrange
        var expectedIp = "192.168.1.100";

        // Act
        var nodeInfo = new NodeInfo { IpAddress = expectedIp };

        // Assert
        nodeInfo.IpAddress.Should().Be(expectedIp);
    }

    [Fact]
    public void Port_CanBeSet()
    {
        // Arrange
        var expectedPort = 8080;

        // Act
        var nodeInfo = new NodeInfo { Port = expectedPort };

        // Assert
        nodeInfo.Port.Should().Be(expectedPort);
    }

    [Fact]
    public void Metadata_CanBeSet()
    {
        // Arrange
        var expectedMetadata = new Dictionary<string, string>
        {
            { "region", "us-east" },
            { "zone", "1a" }
        };

        // Act
        var nodeInfo = new NodeInfo { Metadata = expectedMetadata };

        // Assert
        nodeInfo.Metadata.Should().NotBeNull();
        nodeInfo.Metadata.Should().ContainKey("region");
        nodeInfo.Metadata.Should().ContainKey("zone");
        nodeInfo.Metadata!["region"].Should().Be("us-east");
        nodeInfo.Metadata["zone"].Should().Be("1a");
    }

    [Fact]
    public void NodeInfo_CanBeInitializedWithObjectInitializer()
    {
        // Act
        var nodeInfo = new NodeInfo
        {
            IpAddress = "10.0.0.1",
            Port = 9000,
            Metadata = new Dictionary<string, string>
            {
                { "env", "production" }
            }
        };

        // Assert
        nodeInfo.IpAddress.Should().Be("10.0.0.1");
        nodeInfo.Port.Should().Be(9000);
        nodeInfo.Metadata.Should().ContainKey("env");
        nodeInfo.Metadata!["env"].Should().Be("production");
    }

    [Fact]
    public void NodeInfo_WithoutMetadata_IsValid()
    {
        // Act
        var nodeInfo = new NodeInfo
        {
            IpAddress = "172.16.0.1",
            Port = 3000
        };

        // Assert
        nodeInfo.IpAddress.Should().Be("172.16.0.1");
        nodeInfo.Port.Should().Be(3000);
        nodeInfo.Metadata.Should().BeNull();
    }

    [Fact]
    public void NodeInfo_WithEmptyMetadata_IsValid()
    {
        // Act
        var nodeInfo = new NodeInfo
        {
            IpAddress = "192.168.1.1",
            Port = 5000,
            Metadata = new Dictionary<string, string>()
        };

        // Assert
        nodeInfo.Metadata.Should().NotBeNull();
        nodeInfo.Metadata.Should().BeEmpty();
    }

    [Theory]
    [InlineData("127.0.0.1", 80)]
    [InlineData("192.168.1.1", 8080)]
    [InlineData("10.0.0.1", 443)]
    [InlineData("172.16.0.1", 3000)]
    public void NodeInfo_AcceptsValidIpAndPort(string ipAddress, int port)
    {
        // Act
        var nodeInfo = new NodeInfo
        {
            IpAddress = ipAddress,
            Port = port
        };

        // Assert
        nodeInfo.IpAddress.Should().Be(ipAddress);
        nodeInfo.Port.Should().Be(port);
    }

    [Fact]
    public void NodeInfo_SupportsMultipleMetadataEntries()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "region", "us-west" },
            { "zone", "2b" },
            { "datacenter", "dc1" },
            { "rack", "r42" },
            { "version", "1.2.3" }
        };

        // Act
        var nodeInfo = new NodeInfo
        {
            IpAddress = "10.20.30.40",
            Port = 7000,
            Metadata = metadata
        };

        // Assert
        nodeInfo.Metadata.Should().HaveCount(5);
        nodeInfo.Metadata.Should().ContainKeys("region", "zone", "datacenter", "rack", "version");
    }
}
