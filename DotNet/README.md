# NSerf.Lighthouse

A discovery server for NSerf cluster coordination. This is created to enable zero hardcoding of node addresses and ports in the application code, so it can be used in any distributed system. Lighthouse enables nodes in distributed NSerf clusters to discover each other through a centralized registry with cryptographic authentication, the server does not decrypt the payload and only returns the list of encrypted payloads.

## Overview

NSerf.Lighthouse provides a REST API for cluster node registration and discovery. Nodes register with their cluster credentials and receive information about other nodes in the same cluster and version, enabling automatic peer discovery in distributed systems.

## Key Features

- **Cryptographic Security**: ECDSA signature verification for all requests
- **Replay Attack Protection**: Nonce-based validation with configurable sliding window (default: 24 hours)
- **Encrypted Payloads**: AES-256-GCM encryption for sensitive node information
- **Version Isolation**: Nodes discover only peers in the same cluster version
- **Automatic Eviction**: Background service maintains node limits per cluster version (default: 5 nodes)
- **Rate Limiting**: Configurable request throttling (default: 100 req/sec)
- **PostgreSQL Storage**: Persistent cluster and node data
- **Swagger/OpenAPI**: Interactive API documentation

## Architecture

### Security Model

1. **Cluster Registration**: Clusters register with a unique ID and ECDSA public key
2. **Node Discovery**: Nodes send encrypted payloads with ECDSA signatures
3. **Signature Verification**: Server validates signatures using cluster public keys
4. **Nonce Validation**: Prevents replay attacks by tracking used nonces
5. **Payload Encryption**: Sensitive node details (name, address, port) remain encrypted end-to-end

### API Endpoints

#### POST /clusters
Register a new cluster with its ECDSA public key.

**Request:**
```json
{
  "clusterId": "guid",
  "publicKey": "base64-encoded-ecdsa-public-key"
}
```

**Responses:**
- 201 Created: Cluster registered successfully
- 200 OK: Cluster already exists with same key (idempotent)
- 409 Conflict: Cluster exists with different key

#### POST /discover
Discover nodes in a cluster version.

**Request:**
```json
{
  "clusterId": "guid",
  "versionName": "prod",
  "versionNumber": 1,
  "payload": "base64-encrypted-node-details",
  "nonce": "base64-nonce",
  "signature": "base64-ecdsa-signature"
}
```

**Responses:**
- 200 OK: Returns list of encrypted node payloads
- 401 Unauthorized: Invalid signature
- 403 Forbidden: Replay attack detected
- 404 Not Found: Cluster not registered

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=lighthouse;Username=lighthouse;Password=lighthouse"
  },
  "NonceValidation": {
    "WindowDuration": "24:00:00"
  },
  "NodeEviction": {
    "MaxNodesPerClusterVersion": 5
  },
  "RateLimiting": {
    "Disabled": false
  }
}
```

### Environment Variables (Docker)

- `ConnectionStrings__DefaultConnection`: PostgreSQL connection string
- `NonceValidation__WindowDuration`: Nonce tracking window (format: HH:MM:SS)
- `NodeEviction__MaxNodesPerClusterVersion`: Maximum nodes per cluster version
- `RateLimiting__Disabled`: Set to "true" to disable rate limiting

## Running the Application

### Using Docker Compose

```bash
docker compose up --build
```

### Local Development

**Prerequisites:**
- .NET 8.0 SDK
- PostgreSQL 16+

**Steps:**

1. Start docker compose:
```bash
docker compose up -d 
```

2. Access Swagger UI at http://localhost:5000/

## Testing

- **Unit Tests**: Service and controller logic
- **Integration Tests**: End-to-end API testing with PostgreSQL
- **Security Tests**: Replay attack prevention and nonce validation
- **Concurrency Tests**: High-load stress testing
- **Edge Case Tests**: Boundary conditions and error handling

### Running Tests

```bash
# Run all tests
dotnet test

# Running integration tests requires docker running, so don't forget to start docker desktop/daemon
dotnet test --filter "FullyQualifiedName~Integration" 




## License

This project is part of the NSerf distributed coordination toolkit.
