using Microsoft.Data.Sqlite;

namespace TokenControl.Core.Notion;

/// <summary>Reads the Notion desktop app's <c>token_v2</c> session cookie.</summary>
public sealed class NotionSessionReader(string notionDataDir)
{
    public static NotionSessionReader ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notion"));

    public string LocalStatePath => Path.Combine(notionDataDir, "Local State");
    public string CookiesPath => Path.Combine(notionDataDir, "Partitions", "notion", "Network", "Cookies");

    public bool IsInstalled => File.Exists(LocalStatePath) && File.Exists(CookiesPath);

    /// <summary>Returns the decrypted session, or null if no usable token_v2 cookie exists.</summary>
    public NotionSession? ReadSession()
    {
        var key = ChromiumCookieDecryptor.ReadMasterKey(LocalStatePath);

        // Work on a copy: the running Notion app may hold a lock on the live database.
        var copy = Path.Combine(Path.GetTempPath(), $"tokencontrol-{Guid.NewGuid():N}.db");
        try
        {
            using (var src = new FileStream(CookiesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(copy))
                src.CopyTo(dst);

            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = copy,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            conn.Open();

            var schemaVersion = ReadSchemaVersion(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT host_key, encrypted_value FROM cookies
                WHERE name = 'token_v2'
                ORDER BY CASE host_key
                    WHEN 'app.notion.com' THEN 0
                    WHEN '.app.notion.com' THEN 1
                    WHEN 'www.notion.so' THEN 2
                    WHEN '.notion.so' THEN 3
                    ELSE 4 END,
                    expires_utc DESC
                LIMIT 4
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var encrypted = (byte[])reader["encrypted_value"];
                var token = ChromiumCookieDecryptor.Decrypt(encrypted, key, schemaVersion);
                if (!string.IsNullOrWhiteSpace(token))
                    return new NotionSession(token, (string)reader["host_key"]);
            }
            return null;
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static int ReadSchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'version'";
        return int.TryParse(cmd.ExecuteScalar() as string, out var v) ? v : 0;
    }
}
