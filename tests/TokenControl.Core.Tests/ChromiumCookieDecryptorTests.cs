using System.Security.Cryptography;
using System.Text;
using TokenControl.Core.Notion;

namespace TokenControl.Core.Tests;

public class ChromiumCookieDecryptorTests
{
    private static byte[] EncryptV10(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return [.. "v10"u8, .. nonce, .. cipher, .. tag];
    }

    [Fact]
    public void DecryptsV10AndStripsHostDigestOnSchema24()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var digest = SHA256.HashData(".www.notion.so"u8);
        var blob = EncryptV10([.. digest, .. Encoding.UTF8.GetBytes("v03%3Atoken")], key);

        Assert.Equal("v03%3Atoken", ChromiumCookieDecryptor.Decrypt(blob, key, schemaVersion: 24));
    }

    [Fact]
    public void KeepsWholePlaintextOnOlderSchema()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var blob = EncryptV10(Encoding.UTF8.GetBytes("plain"), key);
        Assert.Equal("plain", ChromiumCookieDecryptor.Decrypt(blob, key, schemaVersion: 18));
    }

    [Fact]
    public void WrongKeyFailsAuthentication()
    {
        var blob = EncryptV10(Encoding.UTF8.GetBytes("plain"), RandomNumberGenerator.GetBytes(32));
        Assert.ThrowsAny<CryptographicException>(() =>
            ChromiumCookieDecryptor.Decrypt(blob, RandomNumberGenerator.GetBytes(32), schemaVersion: 24));
    }

    [Fact]
    public void RejectsAppBoundEncryption() =>
        Assert.Throws<CookieDecryptionException>(() =>
            ChromiumCookieDecryptor.Decrypt([.. "v20"u8, .. new byte[40]], new byte[32], 24));
}
