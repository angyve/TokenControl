using System.Net.Http;
using System.Text.Json;
using TokenControl.Core.Usage;

namespace TokenControl.Core.Claude;

/// <summary>Reads the plan limits shown in claude.ai → Settings → Usage (5-hour session, weekly, extra usage).</summary>
public sealed class ClaudeProvider(Func<ClaudeSession, IClaudeTransport> transportFor, ClaudeSessionStore store) : IUsageProvider
{
    public string Name => "Claude";

    // The organization and email don't change between polls; fetch them once per session.
    private (ClaudeSession Session, ClaudeOrganization Org, string? Email)? _cached;

    public async Task<FetchResult> FetchAsync(CancellationToken ct = default)
    {
        if (store.Load() is not { } session)
            return new FetchResult.SignedOut("Inicia sesión en Claude");

        var api = new ClaudeApiClient(transportFor(session));
        try
        {
            if (_cached is not { } cached || cached.Session != session)
            {
                var org = ClaudeUsageParser.PickOrganization(ClaudeUsageParser.ParseOrganizations(await api.GetOrganizationsAsync(ct)));
                if (org is null)
                    return new FetchResult.Failed("Tu cuenta de Claude no tiene ninguna organización", Transient: false);
                _cached = cached = (session, org, await TryGetEmailAsync(api, ct));
            }

            var windows = ClaudeUsageParser.ParseUsage(await api.GetUsageAsync(cached.Org.Id, ct));
            if (windows.Count == 0)
                return new FetchResult.Failed("Claude no informó límites de uso para tu plan", Transient: false);

            return new FetchResult.Ok(new UsageSnapshot("Claude", cached.Email ?? cached.Org.Name, cached.Org.PlanLabel,
                windows, Note: null, DateTimeOffset.Now));
        }
        catch (ClaudeApiException e) when (e.IsUnauthorized)
        {
            _cached = null;
            return new FetchResult.SignedOut("La sesión de Claude expiró; vuelve a iniciar sesión");
        }
        catch (ClaudeApiException e) when (e.Challenged)
        {
            return new FetchResult.Failed("Claude bloqueó la consulta temporalmente; se reintentará", Transient: true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new FetchResult.Failed("Sin conexión con Claude", Transient: true);
        }
        catch (Exception e) when (e is ClaudeApiException or JsonException)
        {
            return new FetchResult.Failed($"Claude respondió algo inesperado ({e.Message})", Transient: true);
        }
    }

    private static async Task<string?> TryGetEmailAsync(ClaudeApiClient api, CancellationToken ct)
    {
        // The email is cosmetic; the meters are still useful without it.
        try { return ClaudeUsageParser.ParseAccountEmail(await api.GetAccountAsync(ct)); }
        catch (Exception e) when (e is ClaudeApiException or HttpRequestException or JsonException) { return null; }
    }
}
