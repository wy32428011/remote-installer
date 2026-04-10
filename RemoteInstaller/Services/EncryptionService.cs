using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RemoteInstaller.Services;

/// <summary>
/// 加密服务 - 使用 Windows DPAPI 保护用户凭据
/// </summary>
public static class EncryptionService
{
    // DPAPI entropy bytes - per-machine additional protection
    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("RemoteInstaller_v1");

    /// <summary>
    /// 加密字符串
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(
                plainBytes,
                EntropyBytes,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(encryptedBytes);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 解密字符串
    /// </summary>
    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        try
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            byte[] decryptedBytes = ProtectedData.Unprotect(
                cipherBytes,
                EntropyBytes,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
