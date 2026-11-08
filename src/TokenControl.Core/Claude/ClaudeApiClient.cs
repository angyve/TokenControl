using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenControl.Core.Claude;

/// <summary>Raw GET against claude.ai; returns the HTTP status, Cloudflare's challenge flag and the body.</summary>
public sealed record ClaudeResponse(int Status, bool Challenged, string Body);

/// <summary>
/// How requests reach claude.ai. Cloudflare challenges plain HTTP clients once a session cookie is attached,
/// so the app routes requests through a real browser engine; tests and tools can use <see cref="HttpClaudeTransport"/>.
/// </summary>
public interface IClaudeTransport
{
    Task<ClaudeResponse> GetAsync(string path, CancellationToken ct);
}

public sealed class HttpClaudeTransport(HttpClient http, ClaudeSession session) : IClaudeTransport
{
    private static readonly Uri Base = new("https://claude.ai");
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public async Task<ClaudeResponse> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Base, path));
        request.Headers.Add("Cookie", $"sessionKey={session.SessionKey}");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);
        var challenged = response.Headers.TryGetValues("cf-mitigated", out var v) && v.Contains("challenge");
        return new ClaudeResponse((int)response.StatusCode, challenged, await response.Content.ReadAsStringAsync(ct));
    }
}

/// <summary>Calls the private JSON endpoints the claude.ai web app uses for its Settings → Usage page.</summary>
public sealed class ClaudeApiClient(IClaudeTransport transport)
{
    public Task<JsonElement> GetOrganizationsAsync(CancellationToken ct = default) => GetAsync("/api/organizations", ct);

    public Task<JsonElement> GetUsageAsync(string orgId, CancellationToken ct = default) =>
        GetAsync($"/api/organizations/{Uri.EscapeDataString(orgId)}/usage", ct);

    public Task<JsonElement> GetAccountAsync(CancellationToken ct = default) => GetAsync("/api/account", ct);

    private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        var response = await transport.GetAsync(path, ct);
        if (response.Status is < 200 or >= 300)
        {
            var detail = response.Challenged ? "" : response.Body;
            throw new ClaudeApiException(path, (HttpStatusCode)response.Status, response.Challenged,
                detail.Length > 200 ? detail[..200] : detail);
        }
        using var doc = JsonDocument.Parse(response.Body);
        return doc.RootElement.Clone();
    }
}

public sealed class ClaudeApiException(string endpoint, HttpStatusCode status, bool challenged, string detail)
    : Exception($"claude.ai {endpoint} returned {(int)status}{(challenged ? " (Cloudflare challenge)" : "")} {detail}".TrimEnd())
{
    public HttpStatusCode Status { get; } = status;
    public bool Challenged { get; } = challenged;
    public bool IsUnauthorized => !Challenged && Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
