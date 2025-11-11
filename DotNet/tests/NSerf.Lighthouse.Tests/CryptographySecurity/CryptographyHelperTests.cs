using FluentAssertions;
using NSerf.Lighthouse.Tests.TestHelpers;
using NSerf.Lighthouse.Utilities;
using System.Security.Cryptography;
using System.Text;

namespace NSerf.Lighthouse.Tests.CryptographySecurity;

/// <summary>
/// CRITICAL: Tests for cryptographic operations - security foundation
/// </summary>
public class CryptographyHelperTests
{
    [Fact]
    public void CryptoService_VerifySignature_ValidRequest_ReturnsTrue()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        const string data = "test-data-to-sign";
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var signature = privateKey.SignData(dataBytes, HashAlgorithmName.SHA256);

        // Act
        var result = CryptographyHelper.VerifySignature(publicKey, dataBytes, signature);

        // Assert
        result.Should().BeTrue("valid signature should be verified successfully");
    }

    [Fact]
    public void CryptoService_VerifySignature_TamperedPayload_ReturnsFalse()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        const string originalData = "original-data";
        const string tamperedData = "tampered-data";
        
        var originalBytes = Encoding.UTF8.GetBytes(originalData);
        var tamperedBytes = Encoding.UTF8.GetBytes(tamperedData);
        
        // Sign original data
        var signature = privateKey.SignData(originalBytes, HashAlgorithmName.SHA256);

        // Act - Verify with tampered data
        var result = CryptographyHelper.VerifySignature(publicKey, tamperedBytes, signature);

        // Assert
        result.Should().BeFalse("tampered payload should fail signature verification");
    }

    [Fact]
    public void CryptoService_VerifySignature_TamperedSignature_ReturnsFalse()
    {
        // Arrange
        var (publicKey, privateKey) = TestDataGenerator.GenerateEcdsaKeyPair();
        const string data = "test-data";
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var signature = privateKey.SignData(dataBytes, HashAlgorithmName.SHA256);
        
        // Tamper with signature
        signature[0] ^= 0xFF;

        // Act
        var result = CryptographyHelper.VerifySignature(publicKey, dataBytes, signature);

        // Assert
        result.Should().BeFalse("tampered signature should fail verification");
    }

    [Fact]
    public void CryptoService_VerifySignature_WrongPublicKey_ReturnsFalse()
    {
        // Arrange
        var (_, privateKey1) = TestDataGenerator.GenerateEcdsaKeyPair();
        var (publicKey2, _) = TestDataGenerator.GenerateEcdsaKeyPair();
        
        const string data = "test-data";
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var signature = privateKey1.SignData(dataBytes, HashAlgorithmName.SHA256);

        // Act - Verify with wrong public key
        var result = CryptographyHelper.VerifySignature(publicKey2, dataBytes, signature);

        // Assert
        result.Should().BeFalse("signature from different key should fail verification");
    }

    [Fact]
    public void CryptoService_DecryptPayload_ValidCiphertext_ReturnsPlaintext()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");
        var ciphertext = CryptographyHelper.Encrypt(plaintext, key, out var nonce);

        // Act
        var decrypted = CryptographyHelper.Decrypt(ciphertext, key, nonce);

        // Assert
        decrypted.Should().BeEquivalentTo(plaintext, "decryption should return original plaintext");
        Encoding.UTF8.GetString(decrypted).Should().Be("Hello, World!");
    }

    [Fact]
    public void CryptoService_DecryptPayload_TamperedCiphertext_ThrowsException()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var plaintext = Encoding.UTF8.GetBytes("Test data");
        var ciphertext = CryptographyHelper.Encrypt(plaintext, key, out var nonce);
        
        // Tamper with ciphertext
        ciphertext[10] ^= 0xFF;

        // Act & Assert
        var act = () => CryptographyHelper.Decrypt(ciphertext, key, nonce);
        act.Should().Throw<CryptographicException>("tampered ciphertext should fail authentication");
    }

    [Fact]
    public void CryptoService_DecryptPayload_WrongKey_ThrowsException()
    {
        // Arrange
        var key1 = TestDataGenerator.GenerateAesKey();
        var key2 = TestDataGenerator.GenerateAesKey();
        var plaintext = Encoding.UTF8.GetBytes("Secret data");
        var ciphertext = CryptographyHelper.Encrypt(plaintext, key1, out var nonce);

        // Act & Assert
        var act = () => CryptographyHelper.Decrypt(ciphertext, key2, nonce);
        act.Should().Throw<CryptographicException>("wrong key should fail decryption");
    }

    [Fact]
    public void CryptoService_DecryptPayload_WrongNonce_ThrowsException()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var plaintext = Encoding.UTF8.GetBytes("Test data");
        var ciphertext = CryptographyHelper.Encrypt(plaintext, key, out _);
        var wrongNonce = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        // Act & Assert
        var act = () => CryptographyHelper.Decrypt(ciphertext, key, wrongNonce);
        act.Should().Throw<CryptographicException>("wrong nonce should fail decryption");
    }

    [Fact]
    public void CryptoService_EncryptPayload_ReEncryptedPayloadMatchesOriginalAfterDecryption()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var originalPlaintext = Encoding.UTF8.GetBytes("Original message");

        // Act - Encrypt, decrypt, re-encrypt, decrypt again
        var ciphertext1 = CryptographyHelper.Encrypt(originalPlaintext, key, out var nonce1);
        var decrypted1 = CryptographyHelper.Decrypt(ciphertext1, key, nonce1);
        
        var ciphertext2 = CryptographyHelper.Encrypt(decrypted1, key, out var nonce2);
        var decrypted2 = CryptographyHelper.Decrypt(ciphertext2, key, nonce2);

        // Assert
        decrypted1.Should().BeEquivalentTo(originalPlaintext, "first decryption should match original");
        decrypted2.Should().BeEquivalentTo(originalPlaintext, "second decryption should match original");
        Encoding.UTF8.GetString(decrypted2).Should().Be("Original message");
    }

    [Fact]
    public void CryptoService_Encrypt_GeneratesUniqueNonces()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var plaintext = Encoding.UTF8.GetBytes("Test");
        var nonces = new List<byte[]>();

        // Act - Encrypt multiple times
        for (var i = 0; i < 100; i++)
        {
            CryptographyHelper.Encrypt(plaintext, key, out var nonce);
            nonces.Add(nonce);
        }

        // Assert - All nonces should be unique
        var uniqueNonces = nonces.Select(Convert.ToBase64String).Distinct().Count();
        uniqueNonces.Should().Be(100, "each encryption should generate a unique nonce");
    }

    [Fact]
    public void CryptoService_Encrypt_NonceIs4Bytes()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var plaintext = Encoding.UTF8.GetBytes("Test");

        // Act
        CryptographyHelper.Encrypt(plaintext, key, out var nonce);

        // Assert
        nonce.Should().HaveCount(4, "nonce must be exactly 4 bytes as per specification");
    }

    [Fact]
    public void CryptoService_ValidatePublicKey_ValidP256Key_ReturnsTrue()
    {
        // Arrange
        var (publicKey, _) = TestDataGenerator.GenerateEcdsaKeyPair();

        // Act
        var result = CryptographyHelper.ValidatePublicKey(publicKey);

        // Assert
        result.Should().BeTrue("valid P-256 SPKI public key should be accepted");
    }

    [Fact]
    public void CryptoService_ValidatePublicKey_InvalidFormat_ReturnsFalse()
    {
        // Arrange
        var invalidKey = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        var result = CryptographyHelper.ValidatePublicKey(invalidKey);

        // Assert
        result.Should().BeFalse("invalid key format should be rejected");
    }

    [Fact]
    public void CryptoService_ValidatePublicKey_EmptyKey_ReturnsFalse()
    {
        // Arrange
        var emptyKey = Array.Empty<byte>();

        // Act
        var result = CryptographyHelper.ValidatePublicKey(emptyKey);

        // Assert
        result.Should().BeFalse("empty key should be rejected");
    }

    [Fact]
    public void CryptoService_Decrypt_InvalidNonceSize_ThrowsException()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var ciphertext = new byte[32];
        var invalidNonce = new byte[3]; // Wrong size

        // Act & Assert
        var act = () => CryptographyHelper.Decrypt(ciphertext, key, invalidNonce);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*4 bytes*", "nonce must be exactly 4 bytes");
    }

    [Fact]
    public void CryptoService_EncryptDecrypt_LargePayload_WorksCorrectly()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var largePlaintext = new byte[10 * 1024]; // 10KB
        RandomNumberGenerator.Create().GetBytes(largePlaintext);

        // Act
        var ciphertext = CryptographyHelper.Encrypt(largePlaintext, key, out var nonce);
        var decrypted = CryptographyHelper.Decrypt(ciphertext, key, nonce);

        // Assert
        decrypted.Should().BeEquivalentTo(largePlaintext, "large payloads should encrypt/decrypt correctly");
    }

    [Fact]
    public void CryptoService_EncryptDecrypt_EmptyPayload_WorksCorrectly()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var emptyPlaintext = Array.Empty<byte>();

        // Act
        var ciphertext = CryptographyHelper.Encrypt(emptyPlaintext, key, out var nonce);
        var decrypted = CryptographyHelper.Decrypt(ciphertext, key, nonce);

        // Assert
        decrypted.Should().BeEmpty("empty payload should encrypt/decrypt correctly");
    }

    [Fact]
    public void CryptoService_EncryptDecrypt_UnicodeContent_WorksCorrectly()
    {
        // Arrange
        var key = TestDataGenerator.GenerateAesKey();
        var unicodeText = "Hello 世界 🌍 مرحبا";
        var plaintext = Encoding.UTF8.GetBytes(unicodeText);

        // Act
        var ciphertext = CryptographyHelper.Encrypt(plaintext, key, out var nonce);
        var decrypted = CryptographyHelper.Decrypt(ciphertext, key, nonce);

        // Assert
        var decryptedText = Encoding.UTF8.GetString(decrypted);
        decryptedText.Should().Be(unicodeText, "unicode content should be preserved");
    }
}
