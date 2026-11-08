using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using TokenControl.Core.Claude;

namespace TokenControl.App;

/// <summary>Shared WebView2 profile for the sign-in window and the background requests.</summary>
internal static class WebViewProfile
{
    private static Task<CoreWebView2Environment>? _env;

    public static Task<CoreWebView2Environment> EnvironmentAsync() => _env ??= CoreWebView2Environment.CreateAsync(
        userDataFolder: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenControl", "WebView2"));
}

/// <summary>
/// Runs claude.ai requests as fetch() calls inside an invisible WebView2. Cloudflare challenges plain HTTP
/// clients that carry a session cookie, but a real Chromium page passes (and solves any challenge itself).
/// </summary>
internal sealed class WebViewClaudeTransport : IClaudeTransport, IDisposable
{
    // Lightweight same-origin document to run fetch() from.
    private static readonly Uri HostPage = new("https://claude.ai/robots.txt");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Form _host = new() { ShowInTaskbar = false };
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ClaudeResponse>> _pending = new();
    private CoreWebView2Controller? _controller;
    private Task? _ready;
    private string? _cookieValue;

    public CoreWebView2? Web => _controller?.CoreWebView2;

    /// <summary>Puts this session's cookie into the profile (the sign-in window may have been reset meanwhile).</summary>
    public IClaudeTransport For(ClaudeSession session)
    {
        _pendingSession = session;
        return this;
    }

    private ClaudeSession? _pendingSession;

    public async Task<ClaudeResponse> GetAsync(string path, CancellationToken ct)
    {
        await (_ready ??= InitializeAsync());
        var web = Web ?? throw new HttpRequestException("WebView2 not available");
        if (_pendingSession is { } s && _cookieValue != s.SessionKey)
        {
            SetCookie(web, s);
            _cookieValue = s.SessionKey;
        }

        if (web.Source is not { } src || !src.StartsWith("https://claude.ai/", StringComparison.Ordinal))
            await NavigateHostAsync(web);

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ClaudeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            var script = $$"""
                (async () => {
                  const id = {{JsonSerializer.Serialize(id)}};
                  try {
                    const r = await fetch({{JsonSerializer.Serialize(path)}}, { credentials: 'include', headers: { accept: 'application/json' } });
                    const body = await r.text();
                    chrome.webview.postMessage({ id, status: r.status, challenged: r.headers.get('cf-mitigated') === 'challenge', body });
                  } catch (e) {
                    chrome.webview.postMessage({ id, error: String(e) });
                  }
                })();
                """;
            await web.ExecuteScriptAsync(script);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var registration = timeout.Token.Register(() => tcs.TrySetException(
                ct.IsCancellationRequested ? new TaskCanceledException() : new HttpRequestException("claude.ai request timed out")));
            var response = await tcs.Task;
            // A challenge means the host page needs to re-run Cloudflare's check; reload it before the next poll.
            if (response.Challenged) _ = NavigateHostAsync(web);
            return response;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public void ClearCookies()
    {
        Web?.CookieManager.DeleteAllCookies();
        _cookieValue = null;
        _pendingSession = null;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var env = await WebViewProfile.EnvironmentAsync();
            _controller = await env.CreateCoreWebView2ControllerAsync(_host.Handle);
        }
        catch (WebView2RuntimeNotFoundException e)
        {
            throw new HttpRequestException("Microsoft Edge WebView2 is not installed", e);
        }
        _controller.IsVisible = false;
        var web = _controller.CoreWebView2;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.IsScriptEnabled = true;
        web.WebMessageReceived += OnMessage;
    }

    private static void SetCookie(CoreWebView2 web, ClaudeSession session)
    {
        var cookie = web.CookieManager.CreateCookie("sessionKey", session.SessionKey, ".claude.ai", "/");
        cookie.IsSecure = true;
        cookie.IsHttpOnly = true;
        cookie.Expires = DateTime.Now.AddDays(30);
        web.CookieManager.AddOrUpdateCookie(cookie);
    }

    private static async Task NavigateHostAsync(CoreWebView2 web)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDone(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult();
        web.NavigationCompleted += OnDone;
        try
        {
            web.Navigate(HostPage.ToString());
            await Task.WhenAny(done.Task, Task.Delay(Timeout));
            // Cloudflare's interstitial redirects back by itself; give it a moment if it was shown.
            if (web.Source?.StartsWith(HostPage.ToString(), StringComparison.Ordinal) != true)
                await Task.Delay(TimeSpan.FromSeconds(5));
        }
        finally
        {
            web.NavigationCompleted -= OnDone;
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idProp) || idProp.GetString() is not { } id
                || !_pending.TryGetValue(id, out var tcs)) return;

            if (root.TryGetProperty("error", out var err))
                tcs.TrySetException(new HttpRequestException($"fetch failed: {err.GetString()}"));
            else
                tcs.TrySetResult(new ClaudeResponse(
                    root.GetProperty("status").GetInt32(),
                    root.TryGetProperty("challenged", out var c) && c.ValueKind == JsonValueKind.True,
                    root.GetProperty("body").GetString() ?? ""));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Not one of our messages.
        }
    }

    public void Dispose()
    {
        _controller?.Close();
        _host.Dispose();
    }
}
