using System.Reflection;
using TokenControl.Core.Notion;
using TokenControl.Core.Usage;

namespace TokenControl.App;

/// <summary>Owns the tray icon, the polling loop, notifications and the sign-in flow.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly NotionSessionStore _store = NotionSessionStore.ForCurrentUser();
    private readonly IUsageProvider _provider;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly CancellationTokenSource _cts = new();

    private UsageSnapshot? _snapshot;
    private FetchResult? _lastResult;
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LoginForm? _login;
    private Icon? _currentIcon;
    private bool _refreshing;

    public TrayContext()
    {
        _provider = new NotionProvider(_http, _store);
        _tray = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        _tray.MouseUp += (_, e) =>
        {
            // NotifyIcon only opens the menu on right-click; open it on left-click too.
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_tray, null);
        };
        _tray.BalloonTipClicked += (_, _) => { if (_lastResult is FetchResult.SignedOut) ShowLogin(); };
        _menu.Opening += (_, _) => RebuildMenu();

        AppLog.Write($"started {Application.ProductVersion}");
        UpdateTray();
        if (_store.Load() is null) ShowLogin();
        _ = RunLoopAsync();
    }

    private async Task RunLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var delay = await RefreshAsync();
            _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.WhenAny(Task.Delay(delay, _cts.Token), _wake.Task);
        }
    }

    private void RefreshSoon() => _wake.TrySetResult();

    private async Task<TimeSpan> RefreshAsync()
    {
        if (_refreshing) return PollPolicy.Normal;
        _refreshing = true;
        try
        {
            var result = await _provider.FetchAsync(_cts.Token);
            var now = DateTimeOffset.Now;
            var wasSignedOut = _lastResult is FetchResult.SignedOut;
            _lastResult = result;
            AppLog.Write(result switch
            {
                FetchResult.Ok ok => "ok " + string.Join(", ", ok.Snapshot.Windows.Select(w => $"{w.Id}={w.Used}/{w.Limit}")),
                FetchResult.SignedOut s => $"signed-out: {s.Message}",
                FetchResult.Failed f => $"failed{(f.Transient ? " (transient)" : "")}: {f.Message}",
                _ => result.ToString(),
            });

            switch (result)
            {
                case FetchResult.Ok ok:
                    foreach (var alert in UsageAlerts.Evaluate(_snapshot, ok.Snapshot))
                    {
                        var (title, body) = Texts.Alert(alert, now);
                        AppLog.Write($"alert {alert.Kind} {alert.Window.Id}");
                        _tray.ShowBalloonTip(10_000, title, body,
                            alert.Kind == AlertKind.Reset ? ToolTipIcon.Info : ToolTipIcon.Warning);
                    }
                    _snapshot = ok.Snapshot;
                    UpdateTray();
                    return PollPolicy.NextDelay(ok.Snapshot, now);

                case FetchResult.SignedOut signedOut:
                    _snapshot = null;
                    UpdateTray();
                    // Only nag on the transition (e.g. session expired), not on every poll.
                    if (!wasSignedOut && _login is null)
                        _tray.ShowBalloonTip(10_000, "TokenControl", signedOut.Message + ". Haz clic aquí.", ToolTipIcon.Info);
                    return PollPolicy.SignedOut;

                default:
                    UpdateTray();
                    return PollPolicy.Normal;
            }
        }
        catch (OperationCanceledException)
        {
            return PollPolicy.Normal;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateTray()
    {
        var bars = _snapshot?.Windows.Where(w => w.ShowInIcon).Select(w => (w.Fraction, w.Severity)).ToList();

        var old = _currentIcon;
        _currentIcon = TrayIconRenderer.Render(bars);
        _tray.Icon = _currentIcon;
        old?.Dispose();

        var tip = _lastResult switch
        {
            FetchResult.SignedOut s => $"TokenControl\n{s.Message}",
            FetchResult.Failed f when _snapshot is null => $"TokenControl\n{f.Message}",
            _ when _snapshot is { } snap => "Notion AI\n" + string.Join(" · ",
                snap.Windows.Where(w => w.ShowInIcon).Select(w => $"{w.Title}: {Texts.Percent(w.Fraction)}")),
            _ => "TokenControl\nCargando…",
        };
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    private void RebuildMenu()
    {
        var now = DateTimeOffset.Now;
        _menu.Items.Clear();

        if (_snapshot is { } snap)
        {
            AddLabel($"Notion AI · {snap.Plan}", bold: true);
            AddLabel(snap.Account);
            _menu.Items.Add(new ToolStripSeparator());
            foreach (var w in snap.Windows)
                AddLabel(Texts.WindowLine(w, now));
            var stale = _lastResult is FetchResult.Failed f ? $" ({f.Message})" : "";
            AddLabel($"Actualizado {Texts.Ago(snap.FetchedAt, now)}{stale}", dim: true);
        }
        else
        {
            AddLabel("Notion AI", bold: true);
            AddLabel(_lastResult switch
            {
                FetchResult.SignedOut s => s.Message,
                FetchResult.Failed f => f.Message,
                _ => "Cargando…",
            });
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Actualizar ahora", null, (_, _) => RefreshSoon());

        if (_lastResult is FetchResult.SignedOut)
            _menu.Items.Add("Iniciar sesión en Notion…", null, (_, _) => ShowLogin());
        else
            _menu.Items.Add("Cerrar sesión de Notion", null, (_, _) => SignOut());

        var startup = new ToolStripMenuItem("Iniciar con Windows") { Checked = StartupRegistration.IsEnabled, CheckOnClick = true };
        startup.CheckedChanged += (_, _) => StartupRegistration.Set(startup.Checked);
        _menu.Items.Add(startup);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Salir", null, (_, _) => ExitThread());

        void AddLabel(string text, bool bold = false, bool dim = false)
        {
            var item = new ToolStripMenuItem(text) { Enabled = !dim };
            if (bold) item.Font = new Font(item.Font, FontStyle.Bold);
            _menu.Items.Add(item);
        }
    }

    private void ShowLogin()
    {
        if (_login is not null) { _login.Activate(); return; }

        _login = new LoginForm(ValidateAsync);
        _login.FormClosed += (_, _) =>
        {
            AppLog.Write(_login.Session is null ? "login window closed without session" : $"login ok ({_login.Session.Host})");
            if (_login.Session is { } session)
            {
                _store.Save(session);
                _lastResult = null;
                UpdateTray();
                RefreshSoon();
            }
            _login.Dispose();
            _login = null;
        };
        _login.Show();
        _login.Activate();
    }

    private async Task<bool> ValidateAsync(NotionSession session)
    {
        try
        {
            await new NotionApiClient(_http, session).GetSpacesAsync(_cts.Token);
            return true;
        }
        catch (Exception e) when (e is NotionApiException or HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void SignOut()
    {
        _store.Clear();
        _snapshot = null;
        _lastResult = new FetchResult.SignedOut("Sesión cerrada");
        UpdateTray();
    }

    protected override void ExitThreadCore()
    {
        _cts.Cancel();
        _login?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _currentIcon?.Dispose();
        _menu.Dispose();
        _http.Dispose();
        base.ExitThreadCore();
    }
}
