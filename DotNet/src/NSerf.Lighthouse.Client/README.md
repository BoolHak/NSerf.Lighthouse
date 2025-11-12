# NSerf.Lighthouse.Client

.NET client library for consuming the NSerf Lighthouse API. This library provides a simple interface for cluster registration and node discovery in NSerf clusters.

## Features

- **Cluster Registration**: Register your cluster with the Lighthouse server
- **Node Discovery**: Discover other nodes in your cluster version
- **Cryptographic Security**: Built-in ECDSA signature verification and AES-256-GCM encryption
- **Resilience Policies**: Automatic retry with exponential backoff and circuit breaker using Microsoft.Extensions.Http.Resilience
- **Comprehensive Logging**: Structured logging for all operations and errors
- **Telemetry Support**: Built-in metrics and observability
- **Easy Integration**: Simple dependency injection setup

## Installation

```bash
dotnet add package NSerf.Lighthouse.Client
```

## Quick Start

### 1. Configure the Client

Add configuration to your `appsettings.json`:

```json
{
  "LighthouseClient": {
    "BaseUrl": "https://lighthouse.example.com",
    "ClusterId": "your-cluster-guid",
    "PrivateKey": "base64-encoded-pkcs8-private-key",
    "AesKey": "base64-encoded-32-byte-aes-key",
    "TimeoutSeconds": 30
  }
}
```

### 2. Register the Client

In your `Program.cs` or `Startup.cs`:

```csharp
using NSerf.Lighthouse.Client;

builder.Services.AddLighthouseClient(options =>
{
    builder.Configuration.GetSection(LighthouseClientOptions.SectionName).Bind(options);
});
```

### 3. Use the Client

```csharp
public class MyService
{
    private readonly ILighthouseClient _lighthouseClient;

    public MyService(ILighthouseClient lighthouseClient)
    {
        _lighthouseClient = lighthouseClient;
    }

    public async Task RegisterAndDiscoverAsync()
    {
        // Register cluster (one-time setup)
        var publicKey = GetPublicKey(); // Your ECDSA public key
        await _lighthouseClient.RegisterClusterAsync(publicKey);

        // Discover nodes
        var currentNode = new NodeInfo
        {
            IpAddress = "192.168.1.100",
            Port = 7946,
            Metadata = new Dictionary<string, string>
            {
                ["region"] = "us-east-1",
                ["role"] = "worker"
            }
        };

        var peers = await _lighthouseClient.DiscoverNodesAsync(
            currentNode,
            versionName: "production",
            versionNumber: 1);

        foreach (var peer in peers)
        {
            Console.WriteLine($"Discovered peer: {peer.IpAddress}:{peer.Port}");
        }
    }
}
```

## Key Generation

### Generate ECDSA Key Pair (P-256)

```csharp
using System.Security.Cryptography;

var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var privateKey = ecdsa.ExportPkcs8PrivateKey();
var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

var privateKeyBase64 = Convert.ToBase64String(privateKey);
var publicKeyBase64 = Convert.ToBase64String(publicKey);
```

### Generate AES-256 Key

```csharp
var aesKey = new byte[32];
RandomNumberGenerator.Fill(aesKey);
var aesKeyBase64 = Convert.ToBase64String(aesKey);
```

## API Reference

### ILighthouseClient

#### RegisterClusterAsync
Registers the cluster with the Lighthouse server.

```csharp
Task<bool> RegisterClusterAsync(byte[] publicKey, CancellationToken cancellationToken = default)
```

#### DiscoverNodesAsync
Discovers other nodes in the cluster and registers the current node.

```csharp
Task<List<NodeInfo>> DiscoverNodesAsync(
    NodeInfo currentNode,
    string versionName,
    long versionNumber,
    CancellationToken cancellationToken = default)
```

### NodeInfo

Represents a node in the cluster:

```csharp
public class NodeInfo
{
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}
```

## Resilience & Fault Tolerance

The client uses `Microsoft.Extensions.Http.Resilience` for handling transient failures with a standard resilience pipeline:

### Retry Policy
- **3 retries** with exponential backoff (2s base delay)
- Jitter enabled to prevent thundering herd
- Handles transient HTTP errors (5xx, 408, network failures)
- Automatic retry on timeout

### Circuit Breaker
- Opens at **50% failure ratio** with minimum **5 requests**
- Stays open for **30 seconds**
- Sampling duration: **30 seconds**
- Prevents cascading failures
- Automatic reset after break duration

### Timeout
- **10 seconds** per attempt
- Separate from overall HTTP client timeout

### Logging

The client provides comprehensive structured logging:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
```

**Logged Events:**
- Client initialization
- Cluster registration attempts (success/failure)
- Node discovery operations
- Decryption failures
- HTTP errors and retries
- Circuit breaker state changes

**Log Levels:**
- `Information`: Successful operations, node counts
- `Warning`: Failed decryptions, registration failures
- `Error`: HTTP errors, unexpected exceptions
- `Debug`: Individual node details, discovery start

## Security

- All node payloads are encrypted using AES-256-GCM
- All requests are signed using ECDSA with P-256 curve
- Nonce-based replay attack protection
- Server never decrypts node payloads

## License

MIT
