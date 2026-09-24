// End-to-end check: read Notion's session cookie, call the private usage endpoints, print real numbers.
// Usage: TokenControl.Spike [--raw]   (--raw also dumps the usage JSON responses; the token is never printed)
using System.Globalization;
using System.Text.Json;
using TokenControl.Core.Notion;

var raw = args.Contains("--raw");
var reader = NotionSessionReader.ForCurrentUser();

Step("Notion desktop data found");
if (!reader.IsInstalled) return Fail($"missing {reader.LocalStatePath} or {reader.CookiesPath}");
Ok($"cookies last written {File.GetLastWriteTime(reader.CookiesPath):yyyy-MM-dd HH:mm}");

Step("Decrypt token_v2");
NotionSession? session;
try { session = reader.ReadSession(); }
catch (Exception e) { return Fail($"{e.GetType().Name}: {e.Message}"); }
if (session is null) return Fail("no token_v2 cookie — sign in to the Notion desktop app");
Ok($"host {session.Host}, length {session.TokenV2.Length}, starts with \"{session.TokenV2[..4]}…\" → API {session.ApiBase}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var api = new NotionApiClient(http, session);

Step("POST /getSpaces");
JsonElement spaces;
try { spaces = await api.GetSpacesAsync(); }
catch (Exception e) { return Fail(e.Message); }
var workspaces = NotionUsageParser.ParseWorkspaces(spaces);
Ok($"account {NotionUsageParser.ParseAccountEmail(spaces) ?? "?"}, {workspaces.Count} workspace(s)");
foreach (var ws in workspaces)
    Console.WriteLine($"    - {ws.Name} [{ws.TierLabel}]{(ws.CarriesAllowance ? " ← allowance" : "")}");
if (workspaces.Count == 0) return Fail("no workspaces parsed");

foreach (var ws in workspaces.OrderByDescending(w => w.CarriesAllowance))
{
    Step($"Usage for \"{ws.Name}\"");
    JsonElement rate;
    try { rate = await api.GetCreditRateLimitStatusAsync(ws.Id); }
    catch (Exception e) { Console.WriteLine($"  ✗ getCreditRateLimitStatus: {e.Message}"); continue; }

    JsonElement? credits = null;
    try { credits = await api.GetAIUsageEligibilityAsync(ws.Id); }
    catch (Exception e) { Console.WriteLine($"  (credits unavailable: {e.Message})"); }

    if (raw)
    {
        Console.WriteLine(JsonSerializer.Serialize(rate, new JsonSerializerOptions { WriteIndented = true }));
        if (credits is { } c) Console.WriteLine(JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
    }

    var usage = NotionUsageParser.ParseUsage(rate, credits, DateTimeOffset.Now);
    Console.WriteLine($"    status:          {usage.Status ?? "-"}");
    Console.WriteLine($"    rolling ({usage.RollingWindowLabel ?? "?"}): {Meter(usage.Rolling)}");
    Console.WriteLine($"    monthly:         {Meter(usage.Monthly)}");
    Console.WriteLine($"    monthly credits: {Meter(usage.MonthlyCredits)}");
    Console.WriteLine($"    credit balance:  {usage.CreditBalance?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
}
return 0;

static string Meter(UsageMeter? m) => m is null
    ? "-"
    : $"{m.Used:0.##} / {m.Limit:0.##} ({m.Fraction:P0})" + (m.ResetsAt is { } r ? $", resets {r.ToLocalTime():yyyy-MM-dd HH:mm}" : "");

static void Step(string title) => Console.WriteLine($"\n▶ {title}");
static void Ok(string detail) => Console.WriteLine($"  ✓ {detail}");
static int Fail(string detail) { Console.WriteLine($"  ✗ {detail}"); return 1; }
