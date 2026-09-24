using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using TokenControl.Core.Usage;

namespace TokenControl.App;

/// <summary>
/// The flyout shown when clicking the tray icon: usage bars with a pace marker, drawn by hand
/// so it looks like a Windows 11 flyout rather than a stock context menu.
/// </summary>
internal sealed class Popup : Form
{
    public const string IconFontName = "Segoe Fluent Icons";
    private const int LogicalWidth = 340;

    private enum Hit { None, Settings, Refresh, Quit, Action }

    public event Action? RefreshRequested;
    public event Action? SignInRequested;
    public event Action? SignOutRequested;
    public event Action? QuitRequested;

    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 30_000 };
    private readonly List<(Rectangle Rect, Hit Hit)> _hits = [];
    private Theme _theme = Theme.Current();
    private Fonts? _fonts;
    private float _scale = 1;
    private Hit _hover;
    private ContextMenuStrip? _menu;
    private DateTime _hiddenAt;
    private bool _wasActivated;

    private UsageSnapshot? _snapshot;
    private FetchResult? _result;
    private bool _refreshing;

    public Popup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        _tick.Tick += (_, _) => Invalidate();
        // If Windows refused to give us focus we never get activated; don't vanish on the resulting deactivate.
        Activated += (_, _) => _wasActivated = true;
        Deactivate += (_, _) => { if (_wasActivated && _menu is not { Visible: true }) HidePopup(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) HidePopup(); };
    }

    /// <summary>True right after the popup closed because the tray icon was clicked: that click should not reopen it.</summary>
    public bool JustHidden => (DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 300;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public void SetState(UsageSnapshot? snapshot, FetchResult? result, bool refreshing)
    {
        _snapshot = snapshot;
        _result = result;
        _refreshing = refreshing;
        if (Visible) Relayout(keepBottom: true);
    }

    public void ShowNearTray()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        _theme = Theme.Current();
        BackColor = _theme.Background;
        SetScale(DpiAt(cursor) / 96f);

        var size = new Size(S(LogicalWidth), Measure());
        var area = screen.WorkingArea;
        var margin = S(12);
        var x = cursor.X > area.Left + area.Width / 2
            ? area.Right - size.Width - margin
            : Math.Clamp(cursor.X - size.Width / 2, area.Left + margin, area.Right - size.Width - margin);
        var y = area.Bottom < screen.Bounds.Bottom || area.Top == screen.Bounds.Top
            ? area.Bottom - size.Height - margin
            : area.Top + margin;
        Bounds = new Rectangle(x, y, size.Width, size.Height);

        ApplyWindowChrome();
        _wasActivated = false;
        Show();
        SetForegroundWindow(Handle);
        Activate();
        _tick.Start();
        Invalidate();
    }

    public void HidePopup()
    {
        if (!Visible) return;
        _tick.Stop();
        _hover = Hit.None;
        _hiddenAt = DateTime.UtcNow;
        Hide();
    }

    private void Relayout(bool keepBottom)
    {
        var height = Measure();
        if (height != Height)
        {
            var bottom = Bottom;
            Height = height;
            if (keepBottom) Top = bottom - height;
        }
        Invalidate();
    }

    // The popup moves between monitors itself; WinForms' automatic rescale would fight our layout.
    protected override void OnDpiChanged(DpiChangedEventArgs e) => e.Cancel = true;

    private void SetScale(float scale)
    {
        if (_fonts is not null && Math.Abs(scale - _scale) < 0.01f) return;
        _scale = scale;
        _fonts?.Dispose();
        _fonts = new Fonts(scale);
    }

    private int S(float logical) => (int)Math.Round(logical * _scale);

    private int Measure()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        return Render(g, S(LogicalWidth), DateTimeOffset.Now);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(_theme.Background);
        Render(e.Graphics, ClientSize.Width, DateTimeOffset.Now);
    }

    /// <summary>Draws everything and returns the total height; also records clickable areas.</summary>
    private int Render(Graphics g, int width, DateTimeOffset now)
    {
        var f = _fonts ?? new Fonts(_scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _hits.Clear();
        var pad = S(18);
        var inner = width - pad * 2;

        // Header: app name + settings/refresh buttons.
        var headerH = S(50);
        DrawText(g, "TokenControl", f.Title, _theme.Text, new Rectangle(pad, 0, inner, headerH), TextFormatFlags.VerticalCenter);
        var btn = S(32);
        var refresh = new Rectangle(width - pad + S(7) - btn, (headerH - btn) / 2, btn, btn);
        var settings = refresh with { X = refresh.X - btn - S(2) };
        IconButton(g, f, settings, "", Hit.Settings);
        IconButton(g, f, refresh, _refreshing ? "" : "", Hit.Refresh);
        var y = headerH;
        Divider(g, y, width);
        y += S(16);

        if (_snapshot is { } snap)
            y = RenderSnapshot(g, f, snap, pad, inner, y, now);
        else
            y = RenderStatus(g, f, pad, inner, y);

        y += S(6);
        Divider(g, y, width);

        // Footer: freshness + quit.
        var footerH = S(42);
        var quitSize = TextRenderer.MeasureText(g, "Salir", f.Small, Size.Empty, TextFormatFlags.NoPadding);
        var quit = new Rectangle(width - pad - quitSize.Width - S(8), y + (footerH - S(26)) / 2, quitSize.Width + S(16), S(26));
        if (_hover == Hit.Quit) Fill(g, _theme.Hover, quit, S(4));
        DrawText(g, "Salir", f.Small, _hover == Hit.Quit ? _theme.Text : _theme.Dim, quit, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        _hits.Add((quit, Hit.Quit));

        var (status, statusColor) = FooterStatus(now);
        DrawText(g, status, f.Small, statusColor, new Rectangle(pad, y, quit.Left - pad - S(8), footerH),
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        return y + footerH;
    }

    private int RenderSnapshot(Graphics g, Fonts f, UsageSnapshot snap, int pad, int inner, int y, DateTimeOffset now)
    {
        // Provider line: colored dot, name, plan pill.
        var rowH = S(24);
        var dot = S(9);
        Fill(g, Theme.NotionDot, new Rectangle(pad, y + (rowH - dot) / 2, dot, dot), dot / 2);
        var nameX = pad + dot + S(9);
        DrawText(g, snap.Provider, f.Heading, _theme.Text, new Rectangle(nameX, y, inner, rowH), TextFormatFlags.VerticalCenter);

        if (snap.Plan.Length > 0)
        {
            var text = TextRenderer.MeasureText(g, snap.Plan, f.Small, Size.Empty, TextFormatFlags.NoPadding);
            var pill = new Rectangle(pad + inner - text.Width - S(20), y + (rowH - S(22)) / 2, text.Width + S(20), S(22));
            Fill(g, _theme.Pill, pill, pill.Height / 2);
            DrawText(g, snap.Plan, f.Small, _theme.Dim, pill, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        y += rowH;

        var account = snap.Workspace is { Length: > 0 } ws && ws != snap.Account ? $"{snap.Account} · {ws}" : snap.Account;
        DrawText(g, account, f.Small, _theme.Faint, new Rectangle(nameX, y, pad + inner - nameX, S(18)), TextFormatFlags.EndEllipsis);
        y += S(18) + S(16);

        foreach (var w in snap.Windows)
            y = RenderWindow(g, f, w, pad, inner, y, now) + S(16);
        return y - S(10);
    }

    private int RenderWindow(Graphics g, Fonts f, UsageWindow w, int pad, int inner, int y, DateTimeOffset now)
    {
        var pace = UsagePaceCalculator.For(w, now);
        var color = Theme.For(w.Severity);

        var rowH = S(20);
        DrawText(g, w.Title, f.Body, _theme.Text, new Rectangle(pad, y, inner, rowH), TextFormatFlags.VerticalCenter);
        DrawText(g, Texts.Percent(w.Fraction), f.BodyBold, color, new Rectangle(pad, y, inner, rowH),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        y += rowH + S(7);

        var barH = S(6);
        var bar = new Rectangle(pad, y, inner, barH);
        Fill(g, _theme.Track, bar, barH / 2);
        if (pace is { AheadOfPace: true } && pace.Projected > w.Fraction)
            Fill(g, Color.FromArgb(_theme.IsDark ? 90 : 80, color), bar with { Width = BarWidth(inner, pace.Projected, barH) }, barH / 2);
        if (w.Fraction > 0)
            Fill(g, color, bar with { Width = BarWidth(inner, w.Fraction, barH) }, barH / 2);
        if (pace is not null && w.Id != "credits")
        {
            var mx = pad + (int)Math.Round(inner * pace.Expected);
            var mw = Math.Max(2, S(2));
            Fill(g, _theme.Marker, new Rectangle(Math.Clamp(mx - mw / 2, pad, pad + inner - mw), y - S(4), mw, barH + S(8)), 0);
        }
        y += barH + S(8);

        var captionH = S(17);
        DrawText(g, Texts.ResetCaption(w, now), f.Small, _theme.Faint, new Rectangle(pad, y, inner, captionH), TextFormatFlags.VerticalCenter);
        var paceText = Texts.PaceCaption(w, pace, now);
        var paceColor = pace?.EmptiesAt is not null || w.IsExhausted ? Theme.Amber : _theme.Faint;
        DrawText(g, paceText, f.Small, paceColor, new Rectangle(pad, y, inner, captionH), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        return y + captionH;
    }

    private static int BarWidth(int inner, double fraction, int barH) =>
        Math.Clamp((int)Math.Round(inner * fraction), barH, inner);

    /// <summary>No data yet: loading, signed out or failed, with a call to action where one helps.</summary>
    private int RenderStatus(Graphics g, Fonts f, int pad, int inner, int y)
    {
        var dot = S(9);
        var rowH = S(24);
        Fill(g, Theme.NotionDot, new Rectangle(pad, y + (rowH - dot) / 2, dot, dot), dot / 2);
        DrawText(g, "Notion AI", f.Heading, _theme.Text, new Rectangle(pad + dot + S(9), y, inner, rowH), TextFormatFlags.VerticalCenter);
        y += rowH + S(8);

        var (message, action) = _result switch
        {
            FetchResult.SignedOut s => (s.Message, "Iniciar sesión en Notion"),
            FetchResult.Failed x => (x.Message, "Reintentar"),
            _ => ("Cargando…", null),
        };
        const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        var textH = TextRenderer.MeasureText(g, message, f.Body, new Size(inner, 0), wrap).Height;
        DrawText(g, message, f.Body, _theme.Dim, new Rectangle(pad, y, inner, textH), wrap);
        y += textH + S(14);

        if (action is not null)
        {
            var size = TextRenderer.MeasureText(g, action, f.BodyBold, Size.Empty, TextFormatFlags.NoPadding);
            var button = new Rectangle(pad, y, size.Width + S(32), S(34));
            var back = _hover == Hit.Action ? ControlPaint.Light(_theme.Accent, 0.15f) : _theme.Accent;
            Fill(g, back, button, S(5));
            DrawText(g, action, f.BodyBold, _theme.AccentText, button, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _hits.Add((button, Hit.Action));
            y += button.Height + S(14);
        }
        return y;
    }

    private (string Text, Color Color) FooterStatus(DateTimeOffset now)
    {
        if (_refreshing) return ("Actualizando…", _theme.Faint);
        if (_snapshot is not { } snap) return ("", _theme.Faint);
        var ago = "Actualizado " + Texts.Ago(snap.FetchedAt, now);
        return _result is FetchResult.Failed f ? ($"{f.Message} · {Texts.Ago(snap.FetchedAt, now)}", Theme.Amber) : (ago, _theme.Faint);
    }

    private void IconButton(Graphics g, Fonts f, Rectangle r, string glyph, Hit hit)
    {
        if (_hover == hit) Fill(g, _theme.Hover, r, S(5));
        DrawText(g, glyph, f.Icon, _hover == hit ? _theme.Text : _theme.Dim, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        _hits.Add((r, hit));
    }

    private void Divider(Graphics g, int y, int width)
    {
        using var pen = new Pen(_theme.Divider);
        g.DrawLine(pen, 0, y, width, y);
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, Rectangle r, TextFormatFlags flags) =>
        TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

    private static void Fill(Graphics g, Color color, Rectangle r, int radius)
    {
        using var brush = new SolidBrush(color);
        if (radius <= 0) { g.FillRectangle(brush, r); return; }
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitAt(e.Location);
        Cursor = hit == Hit.None ? Cursors.Default : Cursors.Hand;
        if (hit == _hover) return;
        _hover = hit;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover == Hit.None) return;
        _hover = Hit.None;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        switch (HitAt(e.Location))
        {
            case Hit.Refresh:
                RefreshRequested?.Invoke();
                break;
            case Hit.Settings:
                ShowSettingsMenu();
                break;
            case Hit.Quit:
                QuitRequested?.Invoke();
                break;
            case Hit.Action when _result is FetchResult.SignedOut:
                HidePopup();
                SignInRequested?.Invoke();
                break;
            case Hit.Action:
                RefreshRequested?.Invoke();
                break;
        }
    }

    private Hit HitAt(Point p) => _hits.FirstOrDefault(h => h.Rect.Contains(p)).Hit;

    private void ShowSettingsMenu()
    {
        _menu?.Dispose();
        _menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(_theme), ShowImageMargin = true };
        _menu.Font = _fonts?.Body ?? Font;

        _menu.Items.Add("Actualizar ahora", null, (_, _) => RefreshRequested?.Invoke());
        var startup = new ToolStripMenuItem("Iniciar con Windows") { Checked = StartupRegistration.IsEnabled, CheckOnClick = true };
        startup.CheckedChanged += (_, _) => StartupRegistration.Set(startup.Checked);
        _menu.Items.Add(startup);
        _menu.Items.Add(new ToolStripSeparator());
        if (_result is FetchResult.SignedOut)
            _menu.Items.Add("Iniciar sesión en Notion…", null, (_, _) => { HidePopup(); SignInRequested?.Invoke(); });
        else
            _menu.Items.Add("Cerrar sesión de Notion", null, (_, _) => SignOutRequested?.Invoke());
        foreach (ToolStripItem item in _menu.Items) item.Padding = new Padding(0, S(3), 0, S(3));

        _menu.Closed += (_, _) => { if (!ContainsFocus && Form.ActiveForm != this) HidePopup(); };
        _menu.HandleCreated += (_, _) => RoundCorners(_menu.Handle);
        var anchor = _hits.First(h => h.Hit == Hit.Settings).Rect;
        _menu.Padding = new Padding(0, S(4), 0, S(4));
        _menu.Show(this, new Point(anchor.Right, anchor.Bottom + S(2)), ToolStripDropDownDirection.BelowLeft);
    }

    private void ApplyWindowChrome()
    {
        RoundCorners(Handle);
        var dark = _theme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        var border = ColorTranslator.ToWin32(_theme.Border);
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    private static void RoundCorners(IntPtr handle)
    {
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    private static float DpiAt(Point p)
    {
        try
        {
            var monitor = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
            return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi : 96;
        }
        catch (DllNotFoundException)
        {
            return 96;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            _menu?.Dispose();
            _fonts?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class Fonts : IDisposable
    {
        public Font Title { get; }
        public Font Heading { get; }
        public Font Body { get; }
        public Font BodyBold { get; }
        public Font Small { get; }
        public Font Icon { get; }

        public Fonts(float scale)
        {
            Title = Make("Segoe UI Semibold", 15, scale);
            Heading = Make("Segoe UI Semibold", 14.5f, scale);
            Body = Make("Segoe UI", 13, scale);
            BodyBold = Make("Segoe UI Semibold", 13, scale);
            Small = Make("Segoe UI", 12, scale);
            Icon = Make(IconFontName, 15, scale, fallback: "Segoe MDL2 Assets");
        }

        private static Font Make(string family, float px, float scale, string? fallback = null)
        {
            var font = new Font(family, px * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            if (fallback is null || font.Name == family) return font;
            font.Dispose();
            return new Font(fallback, px * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            Title.Dispose();
            Heading.Dispose();
            Body.Dispose();
            BodyBold.Dispose();
            Small.Dispose();
            Icon.Dispose();
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
