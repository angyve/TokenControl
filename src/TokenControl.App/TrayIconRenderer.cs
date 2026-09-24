using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TokenControl.Core.Usage;

namespace TokenControl.App;

/// <summary>Draws the tray icon: one horizontal bar per usage window, fill = usage, color = severity.</summary>
internal static class TrayIconRenderer
{
    private static readonly Color Green = Color.FromArgb(46, 160, 67);
    private static readonly Color Amber = Color.FromArgb(210, 153, 34);
    private static readonly Color Red = Color.FromArgb(229, 83, 75);
    private static readonly Color Brand = Color.FromArgb(94, 106, 210);

    /// <param name="fractions">Usage per bar (0..1) with its severity; null = no data (tracks only).</param>
    public static Icon Render(IReadOnlyList<(double Fraction, Severity Severity)>? fractions)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var track = TaskbarIsLight() ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(110, 255, 255, 255);
            var bars = fractions is { Count: > 0 } ? fractions.Count : 2;
            var gap = Math.Max(2, size / 8);
            var barHeight = (size - gap * (bars + 1)) / (float)bars;

            for (var i = 0; i < bars; i++)
            {
                var rect = new RectangleF(1, gap + i * (barHeight + gap), size - 2, barHeight);
                using (var trackBrush = new SolidBrush(track))
                    FillRounded(g, trackBrush, rect);

                if (fractions is null || i >= fractions.Count) continue;
                var (fraction, severity) = fractions[i];
                if (fraction <= 0) continue;

                // Keep any non-zero usage visible even at 16 px.
                var width = Math.Max(barHeight, rect.Width * (float)fraction);
                using var fill = new SolidBrush(ColorFor(severity));
                FillRounded(g, fill, rect with { Width = width });
            }
        }
        return ToIcon(bmp);
    }

    /// <summary>Static icon for windows (login form): brand-colored bars.</summary>
    public static Icon AppIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Brand);
            FillRounded(g, brush, new RectangleF(2, 6, 28, 8));
            FillRounded(g, brush, new RectangleF(2, 18, 18, 8));
        }
        return ToIcon(bmp);
    }

    private static Color ColorFor(Severity s) => s switch
    {
        Severity.Critical => Red,
        Severity.Warning => Amber,
        _ => Green,
    };

    private static void FillRounded(Graphics g, Brush brush, RectangleF r)
    {
        var radius = Math.Min(r.Height, r.Width) / 2f;
        using var path = new GraphicsPath();
        if (radius < 1) { g.FillRectangle(brush, r); return; }
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    private static Icon ToIcon(Bitmap bmp)
    {
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
