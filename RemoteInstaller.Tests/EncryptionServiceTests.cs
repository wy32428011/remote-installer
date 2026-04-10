using RemoteInstaller.Models;
using RemoteInstaller.Services;
using Xunit;

namespace RemoteInstaller.Tests;

/// <summary>
/// 加密服务测试
/// </summary>
public class EncryptionServiceTests
{
    [Fact]
    public void Encrypt_Decrypt_ReturnsOriginalString()
    {
        // Arrange
        var originalText = "TestPassword123!@#";

        // Act
        var encrypted = EncryptionService.Encrypt(originalText);
        var decrypted = EncryptionService.Decrypt(encrypted);

        // Assert
        Assert.Equal(originalText, decrypted);
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEmptyString()
    {
        // Arrange
        var emptyText = string.Empty;

        // Act
        var encrypted = EncryptionService.Encrypt(emptyText);

        // Assert
        Assert.Equal(string.Empty, encrypted);
    }

    [Fact]
    public void Encrypt_NullString_ReturnsEmptyString()
    {
        // Act
        var encrypted = EncryptionService.Encrypt(null!);

        // Assert
        Assert.Equal(string.Empty, encrypted);
    }

    [Fact]
    public void Decrypt_InvalidString_ReturnsEmptyString()
    {
        // Arrange
        var invalidString = "NotAValidBase64String!!!";

        // Act
        var decrypted = EncryptionService.Decrypt(invalidString);

        // Assert
        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_ComplexString()
    {
        // Arrange
        var complexText = "中文测试 Chinese Test 日本語テスト 한국어 테스트";

        // Act
        var encrypted = EncryptionService.Encrypt(complexText);
        var decrypted = EncryptionService.Decrypt(encrypted);

        // Assert
        Assert.Equal(complexText, decrypted);
    }
}
