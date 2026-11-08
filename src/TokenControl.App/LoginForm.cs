using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TokenControl.Core.Claude;

namespace TokenControl.App;

/// <summary>
/// Embedded claude.ai sign-in. TokenControl keeps its own session, independent of any browser
/// or the Claude apps; we just watch for the sessionKey cookie and validate it.
/// </summary>
internal sealed class LoginForm : Form
{
    private const string LoginUrl = "https://claude.ai/login";
    private const string CookieOrigin = "https://claude.ai";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1500 };
    private readonly Func<ClaudeSession, Task<bool>> _validate;
    private bool _checking;

    public ClaudeSession? Session { get; private set; }

    public LoginForm(Func<ClaudeSession, Task<bool>> validate)
    {
        _validate = validate;
        Text = "TokenControl · Inicia sesión en Claude";
        ClientSize = new Size(1000, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = TrayIconRenderer.AppIcon();
        Controls.Add(_web);

        Load += async (_, _) => await InitializeAsync();
        _poll.Tick += async (_, _) => await CheckForSessionAsync();
        FormClosed += (_, _) => _poll.Dispose();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var env = await WebViewProfile.EnvironmentAsync();
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Falta el componente Microsoft Edge WebView2, necesario para iniciar sesión.\n" +
                "Instálalo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelve a intentarlo.",
                "TokenControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        // Always start clean: a stale cookie left in the profile would otherwise be "found" instantly.
        _web.CoreWebView2.CookieManager.DeleteAllCookies();
        _web.CoreWebView2.Navigate(LoginUrl);
        _poll.Start();
    }

    private async Task CheckForSessionAsync()
    {
        if (_checking || Session is not null || _web.CoreWebView2 is null) return;
        _checking = true;
        try
        {
            var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync(CookieOrigin);
            var key = cookies.FirstOrDefault(c => c.Name == "sessionKey" && c.Value.Length > 0);
            if (key is null) return;

            var candidate = new ClaudeSession(key.Value);
            if (!await _validate(candidate)) return;

            Session = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ObjectDisposedException)
        {
            // Form closed while a check was in flight.
        }
        finally
        {
            _checking = false;
        }
    }
}
