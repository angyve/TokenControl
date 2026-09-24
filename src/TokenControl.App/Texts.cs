using System.Globalization;
using TokenControl.Core.Usage;

namespace TokenControl.App;

/// <summary>User-facing Spanish wording for usage values.</summary>
internal static class Texts
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    public static string Percent(double fraction) => $"{Math.Round(fraction * 100):0} %";

    public static string WindowLine(UsageWindow w, DateTimeOffset now) => w.Id switch
    {
        "credits" => $"{w.Title}: {w.Remaining.ToString("0.##", Es)} de {w.Limit.ToString("0.##", Es)} disponibles",
        _ when w.Used <= 0 && w.ResetsAt is null => $"{w.Title}: sin uso",
        _ => $"{w.Title}: {Percent(w.Fraction)} usado" + (w.ResetsAt is { } r ? $" · se reinicia {When(r, now)}" : ""),
    };

    /// <summary>"en 3 h 20 min" for near resets, "el 17 oct" for far ones.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var left = at - now;
        if (left <= TimeSpan.Zero) return "en un momento";
        if (left < TimeSpan.FromHours(1)) return $"en {Math.Max(1, (int)left.TotalMinutes)} min";
        if (left < TimeSpan.FromHours(24)) return left.Minutes == 0 ? $"en {(int)left.TotalHours} h" : $"en {(int)left.TotalHours} h {left.Minutes} min";
        return "el " + at.ToLocalTime().ToString("d MMM", Es).TrimEnd('.');
    }

    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var ago = now - at;
        return ago < TimeSpan.FromMinutes(1) ? "hace un momento" : $"hace {(int)ago.TotalMinutes} min";
    }

    public static (string Title, string Body) Alert(UsageAlert alert, DateTimeOffset now)
    {
        var w = alert.Window;
        var reset = w.ResetsAt is { } r ? $" Se reinicia {When(r, now)}." : "";
        return alert.Kind switch
        {
            AlertKind.Exhausted => ("Se acabó tu uso de Notion AI", $"Llegaste al límite de {w.Title}.{reset}"),
            AlertKind.RunningLow => ("Te queda poco uso de Notion AI", $"{w.Title}: {Percent(w.Fraction)} usado.{reset}"),
            _ => ("Notion AI está disponible de nuevo", $"Se reinició el límite de {w.Title}."),
        };
    }
}
