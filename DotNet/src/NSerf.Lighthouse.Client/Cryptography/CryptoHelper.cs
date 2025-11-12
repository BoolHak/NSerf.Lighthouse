using System.Security.Cryptography;

namespace NSerf.Lighthouse.Client.Cryptography;

/// <summary>
/// Cryptographic helper for ECDSA signing and AES-256-GCM encryption
/// </summary>
public static class CryptoHelper
{
    private const int NonceSize = 4;
    private const int TagSize = 16;

    /// <summary>
    /// Signs data using ECDSA with P-256 curve and SHA-256
    /// </summary>
    public static byte[] SignData(byte[] privateKey, byte[] data)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateKey, out _);
        return ecdsa.SignData(data, HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with 4-byte nonce
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
    /// Decrypts data using AES-256-GCM with 4-byte nonce
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
}
