using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using TokenControl.Core.Usage;

namespace TokenControl.Core.Notion;

public sealed class NotionProvider(HttpClient http, NotionSessionStore store) : IUsageProvider
{
    public string Name => "Notion AI";

    public async Task<FetchResult> FetchAsync(CancellationToken ct = default)
    {
        if (store.Load() is not { } session)
            return new FetchResult.SignedOut("Inicia sesión en Notion");

        var api = new NotionApiClient(http, session);
        try
        {
            var spaces = await api.GetSpacesAsync(ct);
            var workspaces = NotionUsageParser.ParseWorkspaces(spaces);
            var workspace = workspaces.FirstOrDefault(w => w.CarriesAllowance) ?? workspaces.FirstOrDefault();
            if (workspace is null)
                return new FetchResult.Failed("No se encontró ningún workspace de Notion", Transient: false);

            var rateTask = api.GetCreditRateLimitStatusAsync(workspace.Id, ct);
            var creditsTask = TryGetCreditsAsync(api, workspace.Id, ct);
            var rate = await rateTask;
            var usage = NotionUsageParser.ParseUsage(rate, await creditsTask, DateTimeOffset.Now);

            if (usage.NotApplicable)
                return new FetchResult.Failed($"El plan de \"{workspace.Name}\" no tiene límite de uso de IA", Transient: false);

            return new FetchResult.Ok(ToSnapshot(usage, workspace, NotionUsageParser.ParseAccountEmail(spaces)));
        }
        catch (NotionApiException e) when (e.IsUnauthorized)
        {
            return new FetchResult.SignedOut("La sesión de Notion expiró; vuelve a iniciar sesión");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new FetchResult.Failed("Sin conexión con Notion", Transient: true);
        }
        catch (Exception e) when (e is NotionApiException or JsonException)
        {
            return new FetchResult.Failed($"Notion respondió algo inesperado ({e.Message})", Transient: true);
        }
    }

    private static async Task<JsonElement?> TryGetCreditsAsync(NotionApiClient api, string spaceId, CancellationToken ct)
    {
        // Credits are secondary; the allowance meters alone are still useful.
        try { return await api.GetAIUsageEligibilityAsync(spaceId, ct); }
        catch (Exception e) when (e is NotionApiException or HttpRequestException or JsonException) { return null; }
    }

    private static UsageSnapshot ToSnapshot(NotionUsage usage, NotionWorkspace workspace, string? email)
    {
        var windows = new List<UsageWindow>();
        if (usage.Rolling is { } r)
            windows.Add(new("rolling", RollingTitle(usage.RollingWindowLabel), r.Used, r.Limit, r.ResetsAt));
        if (usage.Monthly is { } m)
            windows.Add(new("monthly", "Mes", m.Used, m.Limit, m.ResetsAt));
        if (usage.MonthlyCredits is { } c)
            windows.Add(new("credits", "Créditos premium", c.Used, c.Limit, c.ResetsAt, ShowInIcon: false));

        var plan = CultureInfo.GetCultureInfo("es-MX").TextInfo.ToTitleCase(workspace.TierLabel);
        return new UsageSnapshot("Notion AI", email ?? workspace.Name, $"{workspace.Name} · {plan}", windows, Note: null, DateTimeOffset.Now);
    }

    private static string RollingTitle(string? label) =>
        label is { Length: > 1 } && label.EndsWith('h') && int.TryParse(label[..^1], out var h)
            ? $"{h} horas"
            : "Ventana corta";
}
