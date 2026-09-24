using System.Globalization;
using TokenControl.Core.Usage;

namespace TokenControl.App;

/// <summary>User-facing Spanish wording for usage values.</summary>
internal static class Texts
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    public static string Percent(double fraction) => $"{Math.Round(fraction * 100):0} %";

    /// <summary>"en 3 h 20 min" / "en 23 d 3 h" for near resets, "el 17 oct" for far ones.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var left = at - now;
        if (left <= TimeSpan.Zero) return "en un momento";
        if (left < TimeSpan.FromDays(45)) return "en " + Duration(left);
        return "el " + at.ToLocalTime().ToString("d MMM", Es).TrimEnd('.');
    }

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)span.TotalMinutes)} min";
        if (span < TimeSpan.FromHours(24)) return span.Minutes == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalHours} h {span.Minutes} min";
        return span.Hours == 0 ? $"{(int)span.TotalDays} d" : $"{(int)span.TotalDays} d {span.Hours} h";
    }

    public static string Date(DateTimeOffset at) => at.ToLocalTime().ToString("d MMM yyyy", Es).Replace(".", "");

    /// <summary>Left caption under a bar: when the window resets.</summary>
    public static string ResetCaption(UsageWindow w, DateTimeOffset now) => w.ResetsAt switch
    {
        { } r => "se reinicia " + When(r, now),
        null when w.Used <= 0 => "sin uso reciente",
        _ => "",
    };

    /// <summary>Right caption under a bar: how usage compares to an even pace.</summary>
    public static string PaceCaption(UsageWindow w, UsagePace? pace, DateTimeOffset now)
    {
        if (w.Id == "credits") return $"quedan {w.Remaining.ToString("0.##", Es)} de {w.Limit.ToString("0.##", Es)}";
        if (w.IsExhausted) return "agotado";
        if (pace is null) return "";
        if (pace.EmptiesAt is { } e) return "se agota en " + Duration(e - now);
        return pace.AheadOfPace ? "por encima del ritmo" : "por debajo del ritmo";
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
