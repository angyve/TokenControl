namespace TokenControl.App;

/// <summary>
/// Small rolling activity log (%LOCALAPPDATA%\TokenControl\activity.log) for diagnosing problems.
/// Never pass secrets here: no tokens, no cookies.
/// </summary>
internal static class AppLog
{
    private const int MaxLines = 500;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenControl", "activity.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var lines = File.Exists(LogPath) ? File.ReadAllLines(LogPath).ToList() : [];
            lines.Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}");
            File.WriteAllLines(LogPath, lines.Skip(Math.Max(0, lines.Count - MaxLines)));
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }
}
