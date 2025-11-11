using System.Security.Cryptography;

namespace NSerf.Lighthouse.Utilities;

/// <summary>
/// Static utility class for cryptographic operations (ECDSA signature verification and AES-256-GCM encryption/decryption)
/// </summary>
public static class CryptographyHelper
{
    private const int NonceSize = 4;
    private const int TagSize = 16;

    /// <summary>
    /// Verifies an ECDSA signature using P-256 curve and SHA-256 hashing
    /// </summary>
    public static bool VerifySignature(byte[] publicKey, byte[] data, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with a 4-byte nonce
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key, out byte[] nonce)
    {
        nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var gcmNonce = new byte[12];
        Array.Copy(nonce, 0, gcmNonce, 0, NonceSize);

        using var aesGcm = new AesGcm(key, TagSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        aesGcm.Encrypt(gcmNonce, plaintext, ciphertext, tag);

        var result = new byte[ciphertext.Length + tag.Length];
        Array.Copy(ciphertext, 0, result, 0, ciphertext.Length);
        Array.Copy(tag, 0, result, ciphertext.Length, tag.Length);

        return result;
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with a 4-byte nonce
    /// </summary>
    public static byte[] Decrypt(byte[] ciphertext, byte[] key, byte[] nonce)
    {
        if (nonce.Length != NonceSize)
        {
            throw new ArgumentException($"Nonce must be exactly {NonceSize} bytes", nameof(nonce));
        }

        var gcmNonce = new byte[12];
        Array.Copy(nonce, 0, gcmNonce, 0, NonceSize);

        var actualCiphertext = new byte[ciphertext.Length - TagSize];
        var tag = new byte[TagSize];
        Array.Copy(ciphertext, 0, actualCiphertext, 0, actualCiphertext.Length);
        Array.Copy(ciphertext, actualCiphertext.Length, tag, 0, TagSize);

        using var aesGcm = new AesGcm(key, TagSize);
        var plaintext = new byte[actualCiphertext.Length];

        aesGcm.Decrypt(gcmNonce, actualCiphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>
    /// Validates that a public key is in SPKI format and uses the P-256 curve
    /// </summary>
    public static bool ValidatePublicKey(byte[] publicKey)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            
            var parameters = ecdsa.ExportParameters(false);
            return parameters.Curve.Oid.FriendlyName == "nistP256" || 
                   parameters.Curve.Oid.Value == "1.2.840.10045.3.1.7";
        }
        catch
        {
            return false;
        }
    }
}
