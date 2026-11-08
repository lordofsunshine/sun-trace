using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SunTrace.Core.Enums;
using SunTrace.Core.Interfaces;
using SunTrace.Core.Models;
using SunTrace.Infrastructure.Localization;

namespace SunTrace.Infrastructure.Services;

public sealed class ReportExporter : IReportExporter
{
    private readonly DataPaths _paths;
    private readonly ILogger<ReportExporter> _logger;

    public ReportExporter(DataPaths paths, ILogger<ReportExporter> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<string> ExportHtmlAsync(string appName, IReadOnlyList<ChangeEvent> events, string? publisher, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.ExportsDirectory);
        var path = UniquePath(_paths.ExportsDirectory, appName, ".html");
        var html = BuildHtml(appName, publisher, events);
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("HTML-отчёт сохранён: {Path}.", path);
        return path;
    }

    public async Task<string> ExportJsonAsync(string appName, IReadOnlyList<ChangeEvent> events, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.ExportsDirectory);
        var path = UniquePath(_paths.ExportsDirectory, appName, ".json");
        var payload = new
        {
            application = "SunTrace",
            exportedAt = DateTimeOffset.Now,
            applicationName = appName,
            eventCount = events.Count,
            events = events.Select(e => new
            {
                e.Id,
                e.DetectedAt,
                kind = e.Kind.ToString(),
                componentType = e.ComponentType.ToString(),
                e.AppName,
                e.ComponentName,
                e.Title,
                e.Description,
                confidence = e.Confidence.ToString(),
                importance = e.Importance.ToString(),
                e.ConfidenceReasons,
                e.Source,
                details = new
                {
                    fields = e.Details.Fields.Select(f => new { f.Label, f.Before, f.After }),
                    e.Details.Explanation,
                    e.Details.Effect,
                    technical = e.Details.Technical,
                    e.Details.SamplePaths
                }
            })
        };

        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("JSON-отчёт сохранён: {Path}.", path);
        return path;
    }

    private static string BuildHtml(string appName, string? publisher, IReadOnlyList<ChangeEvent> events)
    {
        var culture = CultureInfo.CurrentUICulture;
        var language = culture.TwoLetterISOLanguageName;
        var safeName = WebUtility.HtmlEncode(appName);
        var safePublisher = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(publisher) ? ReportText.Get("PublisherUnknown") : publisher);
        var initial = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(appName) ? "S" : appName.Trim()[0].ToString().ToUpperInvariant());
        var ordered = events.OrderByDescending(e => e.DetectedAt).ToList();
        var period = ordered.Count == 0
            ? "-"
            : $"{ordered[^1].DetectedAt.ToLocalTime().ToString("d MMMM yyyy", culture)} - {ordered[0].DetectedAt.ToLocalTime().ToString("d MMMM yyyy", culture)}";

        var added = events.Count(e => e.Kind == ChangeKind.Added);
        var removed = events.Count(e => e.Kind == ChangeKind.Removed);
        var modified = events.Count(e => e.Kind is ChangeKind.Modified or ChangeKind.Paused or ChangeKind.Restored);
        var attention = events.Count(e => e.Importance == ImportanceLevel.Attention);
        var builder = new StringBuilder();

        builder.Append("""
            <!DOCTYPE html>
            <html lang="
            """);
        builder.Append(language);
        builder.Append("""
            ">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            """);
        builder.Append($"<title>SunTrace - {WebUtility.HtmlEncode(ReportText.Get("DocumentTitle"))}: {safeName}</title>");
        builder.Append("""
            <style>
            :root { --bg:#f6f4f1; --surface:#ffffff; --border:#e6e2db; --text:#1f2124; --muted:#6b6f76; --accent:#c77800; --accent-soft:#fdf0dc; --green:#2f8f5b; --green-soft:#e6f3ec; --red:#c2473d; --red-soft:#fae9e7; --yellow:#8a620f; --yellow-soft:#fbf1da; }
            * { box-sizing:border-box; }
            body { margin:0; background:var(--bg); color:var(--text); font-family:"Segoe UI",system-ui,-apple-system,sans-serif; line-height:1.5; }
            .page { max-width:1040px; margin:0 auto; padding:48px 24px 64px; }
            header { display:flex; align-items:flex-start; gap:20px; padding-bottom:24px; border-bottom:1px solid var(--border); }
            .mark { width:54px; height:54px; border-radius:14px; background:var(--accent-soft); display:flex; align-items:center; justify-content:center; flex:0 0 auto; color:var(--accent); font-size:24px; font-weight:700; }
            h1 { font-size:26px; margin:0 0 4px; letter-spacing:-0.01em; }
            .meta { color:var(--muted); font-size:13px; }
            .app-meta { display:grid; grid-template-columns:repeat(4, minmax(0, 1fr)); gap:10px; margin-top:22px; }
            .app-meta div { background:var(--surface); border:1px solid var(--border); border-radius:9px; padding:12px 14px; min-width:0; }
            .app-meta b { display:block; color:var(--muted); font-size:10px; text-transform:uppercase; letter-spacing:.07em; margin-bottom:4px; }
            .app-meta span { display:block; font-size:13px; overflow-wrap:anywhere; }
            .summary { display:flex; gap:12px; flex-wrap:wrap; margin:22px 0 32px; }
            .stat { background:var(--surface); border:1px solid var(--border); border-radius:10px; padding:14px 18px; min-width:150px; }
            .stat b { display:block; font-size:22px; }
            .stat span { color:var(--muted); font-size:12px; }
            .toolbar { display:flex; justify-content:space-between; align-items:baseline; margin-bottom:12px; }
            h2 { font-size:16px; margin:0; }
            .day { margin-top:24px; color:var(--muted); font-size:12px; text-transform:uppercase; letter-spacing:.08em; }
            article { background:var(--surface); border:1px solid var(--border); border-radius:10px; padding:18px 20px; margin-top:10px; }
            article h3 { margin:0 0 6px; font-size:15px; }
            .tags { display:flex; gap:6px; align-items:center; flex-wrap:wrap; }
            .tag { display:inline-block; padding:2px 8px; border-radius:999px; font-size:11px; font-weight:600; }
            .tag.add { background:var(--green-soft); color:var(--green); }
            .tag.remove { background:var(--red-soft); color:var(--red); }
            .tag.modify { background:var(--yellow-soft); color:var(--yellow); }
            .tag.neutral { background:var(--accent-soft); color:#8a5a13; }
            .desc { margin:8px 0 12px; }
            .event-meta { display:flex; gap:16px; flex-wrap:wrap; margin:0 0 4px; color:var(--muted); font-size:12px; }
            table { width:100%; border-collapse:collapse; margin-top:10px; font-size:13px; }
            th, td { text-align:left; padding:8px 10px; border-bottom:1px solid var(--border); vertical-align:top; }
            th { color:var(--muted); font-weight:600; font-size:11px; text-transform:uppercase; letter-spacing:.06em; }
            .value { font-family:"Cascadia Mono",Consolas,monospace; font-size:12px; word-break:break-all; }
            .technical { margin-top:12px; color:var(--muted); font-size:12px; }
            .technical summary { cursor:pointer; }
            @media (max-width:760px) { .app-meta { grid-template-columns:repeat(2, minmax(0, 1fr)); } .page { padding:28px 14px 40px; } }
            </style>
            </head>
            <body>
            <div class="page">
            <header>
              <div class="mark"><span>
            """);
        builder.Append(initial);
        builder.Append("""
            </span></div>
              <div>
            """);
        builder.Append($"<h1>{safeName}</h1>");
        builder.Append($"<div class=\"meta\">{safePublisher} · {WebUtility.HtmlEncode(ReportText.Get("GeneratedAt"))} {DateTime.Now.ToString("d MMMM yyyy, HH:mm", culture)}</div>");
        builder.Append("""
              </div>
            </header>
            <section class="app-meta">
            """);
        builder.Append($"<div><b>{WebUtility.HtmlEncode(ReportText.Get("Publisher"))}</b><span>{safePublisher}</span></div>");
        builder.Append($"<div><b>{WebUtility.HtmlEncode(ReportText.Get("Period"))}</b><span>{period}</span></div>");
        builder.Append($"<div><b>{WebUtility.HtmlEncode(ReportText.Get("Events"))}</b><span>{events.Count}</span></div>");
        builder.Append($"<div><b>{WebUtility.HtmlEncode(ReportText.Get("GeneratedAt"))}</b><span>{DateTime.Now.ToString("d MMMM yyyy", culture)}</span></div>");
        builder.Append("</section>");
        builder.Append("<section class=\"summary\">");
        builder.Append($"<div class=\"stat\"><b>{events.Count}</b><span>{WebUtility.HtmlEncode(ReportText.Get("SummaryEvents"))}</span></div>");
        builder.Append($"<div class=\"stat\"><b>{added}</b><span>{WebUtility.HtmlEncode(ReportText.Get("SummaryAdded"))}</span></div>");
        builder.Append($"<div class=\"stat\"><b>{modified}</b><span>{WebUtility.HtmlEncode(ReportText.Get("SummaryModified"))}</span></div>");
        builder.Append($"<div class=\"stat\"><b>{removed}</b><span>{WebUtility.HtmlEncode(ReportText.Get("SummaryRemoved"))}</span></div>");
        builder.Append($"<div class=\"stat\"><b>{attention}</b><span>{WebUtility.HtmlEncode(ReportText.Get("SummaryAttention"))}</span></div>");
        builder.Append("</section>");
        builder.Append($"<div class=\"toolbar\"><h2>{WebUtility.HtmlEncode(ReportText.Get("HistoryTitle"))}</h2><span class=\"meta\">SunTrace</span></div>");

        foreach (var group in ordered.GroupBy(e => e.DetectedAt.ToLocalTime().Date))
        {
            builder.Append($"<div class=\"day\">{group.Key.ToString("d MMMM yyyy", culture)}</div>");
            foreach (var item in group)
            {
                var tagClass = item.Kind switch
                {
                    ChangeKind.Added => "add",
                    ChangeKind.Removed => "remove",
                    ChangeKind.Modified => "modify",
                    _ => "neutral"
                };
                builder.Append("<article>");
                builder.Append($"<div class=\"tags\"><span class=\"tag {tagClass}\">{WebUtility.HtmlEncode(KindLabel(item.Kind))}</span><span class=\"tag neutral\">{WebUtility.HtmlEncode(ComponentTypeLabel(item.ComponentType))}</span><span class=\"meta\">{item.DetectedAt.ToLocalTime():HH:mm}</span></div>");
                builder.Append($"<h3>{WebUtility.HtmlEncode(item.Title)}</h3>");
                builder.Append($"<div class=\"desc\">{WebUtility.HtmlEncode(item.Description)}</div>");
                builder.Append("<div class=\"event-meta\">");
                if (!string.IsNullOrWhiteSpace(item.AppName))
                {
                    builder.Append($"<span>{WebUtility.HtmlEncode(ReportText.Get("Application"))}: {WebUtility.HtmlEncode(item.AppName)}</span>");
                }
                if (!string.IsNullOrWhiteSpace(item.ComponentName))
                {
                    builder.Append($"<span>{WebUtility.HtmlEncode(ReportText.Get("Component"))}: {WebUtility.HtmlEncode(item.ComponentName)}</span>");
                }
                if (item.AffectedCount > 1)
                {
                    builder.Append($"<span>{WebUtility.HtmlEncode(ReportText.Format("AffectedObjects", item.AffectedCount))}</span>");
                }
                builder.Append("</div>");

                if (item.Details.Fields.Count > 0)
                {
                    builder.Append($"<table><thead><tr><th>{WebUtility.HtmlEncode(ReportText.Get("Parameter"))}</th><th>{WebUtility.HtmlEncode(ReportText.Get("Before"))}</th><th>{WebUtility.HtmlEncode(ReportText.Get("After"))}</th></tr></thead><tbody>");
                    foreach (var field in item.Details.Fields)
                    {
                        builder.Append("<tr>");
                        builder.Append($"<td>{WebUtility.HtmlEncode(field.Label)}</td>");
                        builder.Append($"<td class=\"value\">{WebUtility.HtmlEncode(field.Before ?? "-")}</td>");
                        builder.Append($"<td class=\"value\">{WebUtility.HtmlEncode(field.After ?? "-")}</td>");
                        builder.Append("</tr>");
                    }
                    builder.Append("</tbody></table>");
                }

                builder.Append($"<div class=\"technical\">{WebUtility.HtmlEncode(ReportText.Get("Confidence"))}: {WebUtility.HtmlEncode(ConfidenceLabel(item.Confidence))} · {WebUtility.HtmlEncode(ReportText.Get("Importance"))}: {WebUtility.HtmlEncode(ImportanceLabel(item.Importance))}</div>");
                if (item.Details.Technical.Count > 0)
                {
                    builder.Append($"<details class=\"technical\"><summary>{WebUtility.HtmlEncode(ReportText.Get("Technical"))}</summary><table><tbody>");
                    foreach (var pair in item.Details.Technical)
                    {
                        builder.Append($"<tr><td>{WebUtility.HtmlEncode(pair.Key)}</td><td class=\"value\">{WebUtility.HtmlEncode(pair.Value)}</td></tr>");
                    }
                    builder.Append("</tbody></table></details>");
                }

                builder.Append("</article>");
            }
        }

        if (events.Count == 0)
        {
            builder.Append($"<article><p>{WebUtility.HtmlEncode(ReportText.Get("NoEvents"))}</p></article>");
        }

        builder.Append("""
            </div>
            </body>
            </html>
            """);

        return builder.ToString();
    }

    private static string KindLabel(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => ReportText.Get("KindAdded"),
        ChangeKind.Removed => ReportText.Get("KindRemoved"),
        ChangeKind.Modified => ReportText.Get("KindModified"),
        ChangeKind.Paused => ReportText.Get("KindPaused"),
        ChangeKind.Restored => ReportText.Get("KindRestored"),
        _ => ReportText.Get("KindUnchanged")
    };

    private static string ComponentTypeLabel(ComponentType type) => type switch
    {
        ComponentType.Application => ReportText.Get("ComponentProgram"),
        ComponentType.Startup => ReportText.Get("ComponentStartup"),
        ComponentType.Service => ReportText.Get("ComponentService"),
        ComponentType.ScheduledTask => ReportText.Get("ComponentTask"),
        ComponentType.File => ReportText.Get("ComponentFile"),
        ComponentType.Folder => ReportText.Get("ComponentFolder"),
        ComponentType.Registry => ReportText.Get("ComponentRegistry"),
        _ => ReportText.Get("ComponentGeneric")
    };

    private static string ConfidenceLabel(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.High => ReportText.Get("ConfidenceHigh"),
        ConfidenceLevel.Medium => ReportText.Get("ConfidenceMedium"),
        ConfidenceLevel.Low => ReportText.Get("ConfidenceLow"),
        _ => ReportText.Get("ConfidenceUnknown")
    };

    private static string ImportanceLabel(ImportanceLevel level) => level switch
    {
        ImportanceLevel.Attention => ReportText.Get("ImportanceAttention"),
        ImportanceLevel.Check => ReportText.Get("ImportanceCheck"),
        _ => ReportText.Get("ImportanceNormal")
    };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static string UniquePath(string directory, string appName, string extension)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var name = SafeName(appName);
        var path = Path.Combine(directory, $"{name}-{stamp}{extension}");
        var index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{name}-{stamp}-{index}{extension}");
            index++;
        }

        return path;
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? string.Empty).Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray())
            .Trim()
            .TrimEnd('.', ' ');
        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].TrimEnd('.', ' ');
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "SunTrace";
        }

        var stem = cleaned.Split('.')[0].Trim();
        return ReservedNames.Contains(stem) ? "_" + cleaned : cleaned;
    }
}
