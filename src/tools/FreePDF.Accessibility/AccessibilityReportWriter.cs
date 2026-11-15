// Original FreePDF implementation. No remote resources or executable document content.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfSharp.UniversalAccessibility;

namespace FreePDF.Accessibility;

internal static class AccessibilityReportWriter
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static string Write(PdfAccessibilityReport report, string name, string format) => format switch
    {
        "json" => JsonSerializer.Serialize(report, Json),
        "sarif" => Sarif(report, name),
        "html" => Html(report, name),
        _ => throw new ArgumentException("Unknown report format.")
    };

    static string Sarif(PdfAccessibilityReport report, string name)
    {
        var uri = Uri.EscapeDataString(name);
        var rules = report.Checks.Select(c => new
        {
            id = c.Id, shortDescription = new { text = c.Title },
            fullDescription = new { text = c.Guidance }, helpUri = c.Reference,
            properties = new { outcome = c.Status.ToString(), c.Occurrences }
        });
        var results = report.Findings.Select(f => new
        {
            ruleId = f.RuleId,
            level = f.Status == PdfAccessibilityCheckStatus.Failed ? "error" : f.Status == PdfAccessibilityCheckStatus.NotChecked ? "warning" : "note",
            message = new { text = f.Message },
            locations = new[] { new { physicalLocation = new { artifactLocation = new { uri } }, logicalLocations = new[] { new { fullyQualifiedName = f.Location } } } },
            properties = new { outcome = f.Status.ToString(), page = f.Page }
        });
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[] { new
            {
                tool = new { driver = new { name = "FreePDF accessibility preflight", version = report.RulesetVersion, informationUri = "https://github.com/io7/FreePDF", rules } },
                artifacts = new[] { new { location = new { uri }, hashes = new Dictionary<string, string> { ["sha-256"] = report.DocumentSha256 } } },
                invocations = new[] { new { executionSuccessful = report.ScanCompleted } },
                results,
                properties = new { report.Profile, report.Coverage, report.Status, report.Limitations }
            } }
        }, Json);
    }

    static string Html(PdfAccessibilityReport report, string name)
    {
        static string E(object? s) => WebUtility.HtmlEncode(s?.ToString() ?? "");
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>FreePDF accessibility preflight</title><style>body{font:1rem/1.6 system-ui,sans-serif;max-width:76rem;margin:auto;padding:2rem;color:#17202b;background:#fff}a{color:#174d94}table{border-collapse:collapse;width:100%;margin-bottom:2rem}th,td{padding:.7rem;border:1px solid #9aa3ae;text-align:left;vertical-align:top;overflow-wrap:anywhere}caption{text-align:left;font-weight:bold;font-size:1.3rem}code{overflow-wrap:anywhere}.table{overflow-x:auto}:focus-visible{outline:3px solid #164b8d}</style></head><body><main><h1>Accessibility preflight</h1>");
        html.Append("<p><strong>").Append(E(report.Status)).Append(" — partial coverage</strong></p><p>").Append(E(name)).Append(" · ").Append(E(report.PageCount)).Append(" pages · rules ").Append(E(report.RulesetVersion)).Append("</p><p>SHA-256: <code>").Append(E(report.DocumentSha256)).Append("</code></p><nav aria-label=\"Report sections\"><a href=\"#findings\">Findings and review</a> · <a href=\"#checks\">Check coverage</a> · <a href=\"#order\">Structure order</a> · <a href=\"#limits\">Limitations</a></nav><h2 id=\"findings\">Findings and required review</h2>");
        foreach (var f in report.Findings)
        {
            var check = report.Checks.Single(c => c.Id == f.RuleId);
            html.Append("<article><h3>").Append(E(f.Status)).Append(": ").Append(E(check.Title)).Append("</h3><p>").Append(E(f.Message)).Append("</p><p>").Append(E(check.Guidance)).Append("</p><p><code>").Append(E(f.RuleId)).Append("</code> · page ").Append(E(f.Page)).Append(" · <code>").Append(E(f.Location)).Append("</code></p></article>");
        }
        html.Append("<h2 id=\"checks\">Check coverage</h2><div class=\"table\"><table><caption>Implemented checks only</caption><thead><tr><th scope=\"col\">Rule</th><th scope=\"col\">Check</th><th scope=\"col\">Outcome</th><th scope=\"col\">Occurrences</th></tr></thead><tbody>");
        foreach (var c in report.Checks) html.Append("<tr><th scope=\"row\">").Append(E(c.Id)).Append("</th><td><a href=\"").Append(E(c.Reference)).Append("\">").Append(E(c.Title)).Append("</a></td><td>").Append(E(c.Status)).Append("</td><td>").Append(E(c.Occurrences)).Append("</td></tr>");
        html.Append("</tbody></table></div><h2 id=\"order\">Structure order</h2><p>This is the tag-tree traversal order, not a screen-reader transcript. Compare it with the visible content and test with assistive technology.</p><ol>");
        foreach (var e in report.StructureOrder) html.Append("<li><strong>").Append(E(e.Role)).Append("</strong> · page ").Append(E(e.Page)).Append(" · ").Append(E(e.AlternativeText)).Append(" <code>").Append(E(e.Path)).Append("</code></li>");
        html.Append("</ol><h2 id=\"limits\">Limitations</h2><ul>");
        foreach (var limitation in report.Limitations) html.Append("<li>").Append(E(limitation)).Append("</li>");
        return html.Append("</ul></main></body></html>").ToString();
    }
}
