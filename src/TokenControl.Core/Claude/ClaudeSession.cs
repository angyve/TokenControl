using System.Security.Cryptography;
using System.Text.Json;

namespace TokenControl.Core.Claude;

/// <summary>A claude.ai web session: the value of the <c>sessionKey</c> cookie.</summary>
public sealed record ClaudeSession(string SessionKey)
{
    public override string ToString() => $"ClaudeSession {{ SessionKey = <{SessionKey.Length} chars> }}";
}

/// <summary>Persists TokenControl's own claude.ai session, encrypted with DPAPI for the current Windows user.</summary>
public sealed class ClaudeSessionStore(string filePath)
{
    // Distinguishes our blobs from any other DPAPI data under the same user.
    private static readonly byte[] Entropy = "TokenControl.ClaudeSession.v1"u8.ToArray();

    public static ClaudeSessionStore ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenControl", "claude-session.dat"));

    public ClaudeSession? Load()
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(filePath), Entropy, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<Stored>(json);
            return stored is { SessionKey.Length: > 0 } ? new ClaudeSession(stored.SessionKey) : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public void Save(ClaudeSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(new Stored(session.SessionKey));
        File.WriteAllBytes(filePath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        if (File.Exists(filePath)) File.Delete(filePath);
    }

    private sealed record Stored(string SessionKey);
}
