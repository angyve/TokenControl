using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TokenControl.Core.Notion;

/// <summary>
/// Decrypts cookies written by a Chromium/Electron app on Windows:
/// the AES key lives DPAPI-protected in "Local State", values are AES-256-GCM.
/// </summary>
public static class ChromiumCookieDecryptor
{
    private static readonly byte[] DpapiPrefix = "DPAPI"u8.ToArray();
    private const int NonceLength = 12;
    private const int TagLength = 16;
    // Cookie DB schema v24+ prepends SHA-256(host_key) to the plaintext.
    private const int HostDigestLength = 32;

    public static byte[] ReadMasterKey(string localStatePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(localStatePath));
        var encoded = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()
            ?? throw new CookieDecryptionException("Local State has no os_crypt.encrypted_key.");

        var blob = Convert.FromBase64String(encoded);
        if (!blob.AsSpan().StartsWith(DpapiPrefix))
            throw new CookieDecryptionException("Unexpected encrypted_key format (missing DPAPI prefix).");

        return ProtectedData.Unprotect(blob[DpapiPrefix.Length..], null, DataProtectionScope.CurrentUser);
    }

    public static string Decrypt(byte[] encryptedValue, byte[] masterKey, int schemaVersion)
    {
        if (encryptedValue.Length < 3)
            throw new CookieDecryptionException("Cookie value too short.");

        var prefix = Encoding.ASCII.GetString(encryptedValue, 0, 3);
        byte[] plaintext = prefix switch
        {
            "v10" or "v11" => DecryptAesGcm(encryptedValue.AsSpan(3), masterKey),
            // App-bound encryption (Chrome 127+) needs an elevated COM service; Electron apps don't use it today.
            "v20" => throw new CookieDecryptionException("Cookie uses app-bound encryption (v20), which is not supported."),
            // Very old Chromium stored the raw value DPAPI-protected with no prefix.
            _ => ProtectedData.Unprotect(encryptedValue, null, DataProtectionScope.CurrentUser),
        };

        var value = schemaVersion >= 24 && plaintext.Length >= HostDigestLength
            ? plaintext.AsSpan(HostDigestLength)
            : plaintext;
        return Encoding.UTF8.GetString(value);
    }

    private static byte[] DecryptAesGcm(ReadOnlySpan<byte> payload, byte[] key)
    {
        if (payload.Length < NonceLength + TagLength)
            throw new CookieDecryptionException("Encrypted cookie payload too short.");

        var nonce = payload[..NonceLength];
        var cipher = payload[NonceLength..^TagLength];
        var tag = payload[^TagLength..];
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }
}

public sealed class CookieDecryptionException(string message) : Exception(message);
