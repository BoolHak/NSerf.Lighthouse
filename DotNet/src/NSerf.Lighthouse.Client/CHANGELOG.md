# Changelog

All notable changes to NSerf.Lighthouse.Client will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-11-11

### Added
- Initial release of NSerf.Lighthouse.Client
- Cluster registration with ECDSA public key authentication
- Node discovery with encrypted payloads (AES-256-GCM)
- Digital signatures using ECDSA P-256 curve
- Resilience policies using Microsoft.Extensions.Http.Resilience
  - Retry with exponential backoff (3 attempts, 2s base delay, jitter enabled)
  - Circuit breaker (50% failure ratio, 5 min throughput, 30s break)
  - Per-attempt timeout (10s)
- Dependency injection support with `AddLighthouseClient()` extension
- Comprehensive structured logging
- Built-in telemetry and metrics support
- Full XML documentation for IntelliSense
- Source Link support for debugging

### Security
- AES-256-GCM encryption for all node payloads
- ECDSA P-256 digital signatures for request authentication
- Nonce-based replay attack protection
- Server-side signature verification

### Dependencies
- Microsoft.Extensions.Http (>= 8.0.0)
- Microsoft.Extensions.Http.Resilience (>= 8.0.0)
- Microsoft.Extensions.Logging.Abstractions (>= 8.0.0)
- Microsoft.Extensions.Options (>= 8.0.0)
- System.Security.Cryptography.Algorithms (>= 4.3.1)
- System.Text.RegularExpressions (>= 4.3.1)

[1.0.0]: https://github.com/BoolHak/NSerf.Lighthouse/releases/tag/v1.0.0
