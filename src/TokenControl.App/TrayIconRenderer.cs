namespace TokenControl.App;

/// <summary>Loads the app's static icon (the Notion agent's avatar) embedded in the assembly.</summary>
internal static class TrayIconRenderer
{
    private const string ResourceName = "TokenControl.App.Assets.TokenControl.ico";

    /// <summary>Tray-sized icon; the .ico carries several sizes so Windows gets a sharp one at any DPI.</summary>
    public static Icon TrayIcon() => Load(SystemInformation.SmallIconSize);

    /// <summary>Icon for windows (login form).</summary>
    public static Icon AppIcon() => Load(SystemInformation.IconSize);

    public static Icon Load(Size size)
    {
        using var stream = typeof(TrayIconRenderer).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}");
        return new Icon(stream, size);
    }
}
