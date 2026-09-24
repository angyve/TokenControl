using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TokenControl.Core.Notion;

namespace TokenControl.App;

/// <summary>
/// Embedded Notion sign-in. TokenControl keeps its own session, independent of Chrome
/// or the Notion desktop app; we just watch for the token_v2 cookie and validate it.
/// </summary>
internal sealed class LoginForm : Form
{
    private const string LoginUrl = "https://www.notion.so/login";
    private static readonly string[] CookieOrigins = ["https://app.notion.com", "https://www.notion.so"];

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 1500 };
    private readonly Func<NotionSession, Task<bool>> _validate;
    private bool _checking;

    public NotionSession? Session { get; private set; }

    public LoginForm(Func<NotionSession, Task<bool>> validate)
    {
        _validate = validate;
        Text = "TokenControl · Inicia sesión en Notion";
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
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenControl", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
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
            foreach (var origin in CookieOrigins)
            {
                var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync(origin);
                var token = cookies.FirstOrDefault(c => c.Name == "token_v2" && c.Value.Length > 0);
                if (token is null) continue;

                var candidate = new NotionSession(token.Value, token.Domain);
                if (!await _validate(candidate)) continue;

                Session = candidate;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
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
