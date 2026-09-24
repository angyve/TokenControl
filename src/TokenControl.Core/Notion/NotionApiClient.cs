using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TokenControl.Core.Notion;

/// <summary>Calls the private endpoints the Notion client uses internally (/api/v3).</summary>
public sealed class NotionApiClient(HttpClient http, NotionSession session)
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    public Task<JsonElement> GetSpacesAsync(CancellationToken ct = default) =>
        PostAsync("getSpaces", new { }, ct);

    public Task<JsonElement> GetCreditRateLimitStatusAsync(string spaceId, CancellationToken ct = default) =>
        PostAsync("getCreditRateLimitStatus", new { spaceId }, ct);

    public Task<JsonElement> GetAIUsageEligibilityAsync(string spaceId, CancellationToken ct = default) =>
        PostAsync("getAIUsageEligibilityV2", new { spaceId }, ct);

    private async Task<JsonElement> PostAsync(string endpoint, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(session.ApiBase, endpoint))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", $"token_v2={session.TokenV2}");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new NotionApiException(endpoint, response.StatusCode, detail.Length > 300 ? detail[..300] : detail);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
}

public sealed class NotionApiException(string endpoint, HttpStatusCode status, string detail)
    : Exception($"Notion {endpoint} returned {(int)status} {status}: {detail}")
{
    public HttpStatusCode Status { get; } = status;
    public bool IsUnauthorized => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
