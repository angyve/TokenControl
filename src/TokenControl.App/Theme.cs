using Microsoft.Win32;
using TokenControl.Core.Usage;

namespace TokenControl.App;

/// <summary>Colors for the popup, following the Windows light/dark app setting.</summary>
internal sealed record Theme(
    bool IsDark,
    Color Background,
    Color Text,
    Color Dim,
    Color Faint,
    Color Divider,
    Color Track,
    Color Marker,
    Color Hover,
    Color Pill,
    Color Accent,
    Color AccentText,
    Color Border)
{
    public static readonly Color Green = Color.FromArgb(62, 180, 98);
    public static readonly Color Amber = Color.FromArgb(222, 150, 30);
    public static readonly Color Red = Color.FromArgb(226, 80, 72);
    public static readonly Color NotionDot = Color.FromArgb(58, 112, 176);

    private static readonly Theme Light = new(
        IsDark: false,
        Background: Color.FromArgb(249, 249, 249),
        Text: Color.FromArgb(28, 28, 28),
        Dim: Color.FromArgb(100, 100, 100),
        Faint: Color.FromArgb(150, 150, 150),
        Divider: Color.FromArgb(229, 229, 229),
        Track: Color.FromArgb(228, 228, 228),
        Marker: Color.FromArgb(90, 90, 90),
        Hover: Color.FromArgb(234, 234, 234),
        Pill: Color.FromArgb(232, 232, 232),
        Accent: Color.FromArgb(0, 95, 184),
        AccentText: Color.White,
        Border: Color.FromArgb(214, 214, 214));

    private static readonly Theme Dark = new(
        IsDark: true,
        Background: Color.FromArgb(36, 36, 36),
        Text: Color.FromArgb(242, 242, 242),
        Dim: Color.FromArgb(180, 180, 180),
        Faint: Color.FromArgb(128, 128, 128),
        Divider: Color.FromArgb(58, 58, 58),
        Track: Color.FromArgb(62, 62, 62),
        Marker: Color.FromArgb(200, 200, 200),
        Hover: Color.FromArgb(52, 52, 52),
        Pill: Color.FromArgb(58, 58, 58),
        Accent: Color.FromArgb(96, 205, 255),
        AccentText: Color.Black,
        Border: Color.FromArgb(64, 64, 64));

    public static Theme Current()
    {
        // Development override to preview a theme without changing the Windows setting.
        if (Environment.GetEnvironmentVariable("TOKENCONTROL_THEME") is { } forced)
            return forced.Equals("dark", StringComparison.OrdinalIgnoreCase) ? Dark : Light;

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0 ? Dark : Light;
    }

    public static Color For(Severity s) => s switch
    {
        Severity.Critical => Red,
        Severity.Warning => Amber,
        _ => Green,
    };
}

/// <summary>Themed colors for the settings dropdown so it matches the popup.</summary>
internal sealed class ThemedMenuColors(Theme t) : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => t.Background;
    public override Color ImageMarginGradientBegin => t.Background;
    public override Color ImageMarginGradientMiddle => t.Background;
    public override Color ImageMarginGradientEnd => t.Background;
    public override Color MenuBorder => t.Border;
    public override Color MenuItemBorder => t.Hover;
    public override Color MenuItemSelected => t.Hover;
    public override Color MenuItemSelectedGradientBegin => t.Hover;
    public override Color MenuItemSelectedGradientEnd => t.Hover;
    public override Color SeparatorDark => t.Divider;
    public override Color SeparatorLight => t.Divider;
    public override Color CheckBackground => t.Hover;
    public override Color CheckSelectedBackground => t.Hover;
    public override Color CheckPressedBackground => t.Hover;
    public override Color ButtonSelectedBorder => t.Hover;
}

internal sealed class ThemedMenuRenderer(Theme t) : ToolStripProfessionalRenderer(new ThemedMenuColors(t))
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? t.Text : t.Faint;
        base.OnRenderItemText(e);
    }

    // The stock check image is black, invisible on the dark theme.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var font = new Font(Popup.IconFontName, e.ImageRectangle.Height * 0.75f, GraphicsUnit.Pixel);
        TextRenderer.DrawText(e.Graphics, "", font, e.ImageRectangle, t.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
