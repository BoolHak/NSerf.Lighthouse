using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NSerf.Lighthouse.Client.Cryptography;
using Xunit;

namespace NSerf.Lighthouse.Client.Tests.Cryptography;

public class CryptoHelperTests
{
    [Fact]
    public void SignData_WithValidPrivateKey_ReturnsValidSignature()
    {
        // Arrange
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        var data = Encoding.UTF8.GetBytes("test data");

        // Act
        var signature = CryptoHelper.SignData(privateKey, data);

        // Assert
        signature.Should().NotBeNull();
        signature.Should().NotBeEmpty();
        signature.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SignData_WithSameData_ProducesDifferentSignatures()
    {
        // Arrange
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        var data = Encoding.UTF8.GetBytes("test data");

        // Act
        var signature1 = CryptoHelper.SignData(privateKey, data);
        var signature2 = CryptoHelper.SignData(privateKey, data);

        // Assert - ECDSA signatures are non-deterministic
        signature1.Should().NotBeEquivalentTo(signature2);
    }

    [Fact]
    public void SignData_WithInvalidPrivateKey_ThrowsException()
    {
        // Arrange
        var invalidKey = new byte[32];
        var data = Encoding.UTF8.GetBytes("test data");

        // Act & Assert
        var act = () => CryptoHelper.SignData(invalidKey, data);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Encrypt_WithValidKey_ReturnsEncryptedData()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");

        // Act
        var ciphertext = CryptoHelper.Encrypt(plaintext, key, out var nonce);

        // Assert
        ciphertext.Should().NotBeNull();
        ciphertext.Should().NotBeEmpty();
        ciphertext.Should().NotBeEquivalentTo(plaintext);
        nonce.Should().NotBeNull();
        nonce.Length.Should().Be(4);
    }

    [Fact]
    public void Encrypt_WithSamePlaintext_ProducesDifferentCiphertexts()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");

        // Act
        var ciphertext1 = CryptoHelper.Encrypt(plaintext, key, out var nonce1);
        var ciphertext2 = CryptoHelper.Encrypt(plaintext, key, out var nonce2);

        // Assert - Different nonces should produce different ciphertexts
        nonce1.Should().NotBeEquivalentTo(nonce2);
        ciphertext1.Should().NotBeEquivalentTo(ciphertext2);
    }

    [Fact]
    public void Decrypt_WithValidCiphertext_ReturnsOriginalPlaintext()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var originalPlaintext = Encoding.UTF8.GetBytes("Hello, World!");
        var ciphertext = CryptoHelper.Encrypt(originalPlaintext, key, out var nonce);

        // Act
        var decryptedPlaintext = CryptoHelper.Decrypt(ciphertext, key, nonce);

        // Assert
        decryptedPlaintext.Should().BeEquivalentTo(originalPlaintext);
        Encoding.UTF8.GetString(decryptedPlaintext).Should().Be("Hello, World!");
    }

    [Fact]
    public void Decrypt_WithInvalidNonceSize_ThrowsArgumentException()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");
        var ciphertext = CryptoHelper.Encrypt(plaintext, key, out _);
        var invalidNonce = new byte[8]; // Wrong size

        // Act & Assert
        var act = () => CryptoHelper.Decrypt(ciphertext, key, invalidNonce);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Nonce must be exactly 4 bytes*");
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        // Arrange
        var key1 = new byte[32];
        var key2 = new byte[32];
        RandomNumberGenerator.Fill(key1);
        RandomNumberGenerator.Fill(key2);
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");
        var ciphertext = CryptoHelper.Encrypt(plaintext, key1, out var nonce);

        // Act & Assert
        var act = () => CryptoHelper.Decrypt(ciphertext, key2, nonce);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithWrongNonce_ThrowsCryptographicException()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");
        var ciphertext = CryptoHelper.Encrypt(plaintext, key, out _);
        var wrongNonce = new byte[4];
        RandomNumberGenerator.Fill(wrongNonce);

        // Act & Assert
        var act = () => CryptoHelper.Decrypt(ciphertext, key, wrongNonce);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_WithLargeData()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var largeData = new byte[10000];
        RandomNumberGenerator.Fill(largeData);

        // Act
        var ciphertext = CryptoHelper.Encrypt(largeData, key, out var nonce);
        var decrypted = CryptoHelper.Decrypt(ciphertext, key, nonce);

        // Assert
        decrypted.Should().BeEquivalentTo(largeData);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_WithEmptyData()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var emptyData = Array.Empty<byte>();

        // Act
        var ciphertext = CryptoHelper.Encrypt(emptyData, key, out var nonce);
        var decrypted = CryptoHelper.Decrypt(ciphertext, key, nonce);

        // Assert
        decrypted.Should().BeEquivalentTo(emptyData);
    }

    [Fact]
    public void Encrypt_IncludesAuthenticationTag()
    {
        // Arrange
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var plaintext = Encoding.UTF8.GetBytes("Hello, World!");

        // Act
        var ciphertext = CryptoHelper.Encrypt(plaintext, key, out _);

        // Assert - Ciphertext should be plaintext length + 16 bytes (tag)
        ciphertext.Length.Should().Be(plaintext.Length + 16);
    }
}
