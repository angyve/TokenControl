using System.Security.Cryptography;
using System.Text.Json;

namespace TokenControl.Core.Notion;

/// <summary>Persists TokenControl's own Notion session, encrypted with DPAPI for the current Windows user.</summary>
public sealed class NotionSessionStore(string filePath)
{
    // Distinguishes our blobs from any other DPAPI data under the same user.
    private static readonly byte[] Entropy = "TokenControl.NotionSession.v1"u8.ToArray();

    public static NotionSessionStore ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenControl", "session.dat"));

    public NotionSession? Load()
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<Stored>(json);
            return stored is { Token.Length: > 0, Host.Length: > 0 } ? new NotionSession(stored.Token, stored.Host) : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public void Save(NotionSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(new Stored(session.TokenV2, session.Host));
        File.WriteAllBytes(filePath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        if (File.Exists(filePath)) File.Delete(filePath);
    }

    private sealed record Stored(string Token, string Host);
}
