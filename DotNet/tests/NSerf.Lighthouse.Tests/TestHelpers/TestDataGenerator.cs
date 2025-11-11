using System.Security.Cryptography;
using System.Text;

namespace NSerf.Lighthouse.Tests.TestHelpers;

public static class TestDataGenerator
{
    // Fixed timestamp for deterministic tests
    private static readonly DateTime FixedTimestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly long FixedTimestampTicks = FixedTimestamp.Ticks;

    // Generate a valid ECDSA P-256 key pair
    public static (byte[] publicKey, ECDsa privateKey) GenerateEcdsaKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        return (publicKey, ecdsa);
    }

    // Generate a valid AES-256 key
    public static byte[] GenerateAesKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Create().GetBytes(key);
        return key;
    }

    // Sign data with ECDSA
    public static byte[] SignData(ECDsa privateKey, string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        return privateKey.SignData(bytes, HashAlgorithmName.SHA256);
    }

    // Generate a 4-byte nonce
    public static byte[] GenerateNonce()
    {
        var nonce = new byte[4];
        RandomNumberGenerator.Create().GetBytes(nonce);
        return nonce;
    }

    // Fixed nonce for deterministic tests
    public static byte[] FixedNonce()
    {
        return [0x01, 0x02, 0x03, 0x04];
    }

    // Generate a valid cluster ID
    public static Guid GenerateClusterId()
    {
        return Guid.NewGuid();
    }

    // Fixed cluster ID for deterministic tests
    public static Guid FixedClusterId()
    {
        return Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");
    }

    // Generate invalid base64 string
    public static string InvalidBase64()
    {
        return "not-valid-base64!!!";
    }

    // Generate invalid GUID
    public static string InvalidGuid()
    {
        return "not-a-valid-guid";
    }

    // Generate a node payload JSON
    public static string GenerateNodePayloadJson(
        string nodeName = "node-1",
        string versionName = "prod",
        long versionNumber = 1,
        string address = "10.0.0.1",
        int port = 7946,
        long? timestamp = null)
    {
        timestamp ??= FixedTimestampTicks;
        return $@"{{
            ""nodeName"": ""{nodeName}"",
            ""clusterVersionName"": ""{versionName}"",
            ""clusterVersionNumber"": {versionNumber},
            ""address"": ""{address}"",
            ""port"": {port},
            ""timestamp"": {timestamp}
        }}";
    }
}
