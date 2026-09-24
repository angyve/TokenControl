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
    private readonly Popup _popup = new();
    private readonly CancellationTokenSource _cts = new();

    private UsageSnapshot? _snapshot;
    private FetchResult? _lastResult;
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LoginForm? _login;
    private Icon? _currentIcon;
    private bool _refreshing;
    private bool _showOnStart = Environment.GetCommandLineArgs().Contains("--show");

    public TrayContext()
    {
        _provider = new NotionProvider(_http, _store);
        _tray = new NotifyIcon { Visible = true };
        _tray.MouseUp += (_, e) => { if (e.Button is MouseButtons.Left or MouseButtons.Right) TogglePopup(); };
        _tray.BalloonTipClicked += (_, _) => { if (_lastResult is FetchResult.SignedOut) ShowLogin(); else TogglePopup(); };

        _popup.RefreshRequested += RefreshSoon;
        _popup.SignInRequested += ShowLogin;
        _popup.SignOutRequested += SignOut;
        _popup.QuitRequested += ExitThread;

        AppLog.Write($"started {Application.ProductVersion}");
        UpdateTray();
        if (_store.Load() is null) ShowLogin();
        _ = RunLoopAsync();
    }

    private void TogglePopup()
    {
        if (_popup.Visible) _popup.HidePopup();
        // Clicking the icon deactivates (and so hides) the popup before this click arrives.
        else if (!_popup.JustHidden) _popup.ShowNearTray();
    }

    private async Task RunLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var delay = await RefreshAsync();
            // Development aid: open the popup right away so it can be inspected without clicking the tray.
            if (_showOnStart) { _showOnStart = false; _popup.ShowNearTray(); }
            _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.WhenAny(Task.Delay(delay, _cts.Token), _wake.Task);
        }
    }

    private void RefreshSoon() => _wake.TrySetResult();

    private async Task<TimeSpan> RefreshAsync()
    {
        if (_refreshing) return PollPolicy.Normal;
        _refreshing = true;
        _popup.SetState(_snapshot, _lastResult, refreshing: true);
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
            _popup.SetState(_snapshot, _lastResult, refreshing: false);
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
        _popup.SetState(_snapshot, _lastResult, _refreshing);
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
        _popup.Dispose();
        _http.Dispose();
        base.ExitThreadCore();
    }
}
