// Original FreePDF implementation. See LICENSE.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfSharp.UniversalAccessibility;

namespace FreePDF.Accessibility;

internal static class IndependentReportWriter
{
    internal static string Write(PdfAccessibilityReport native, IndependentValidation independent, string name, string format)
    {
        var status = IndependentValidation.Combine(native, independent);
        if (format == "json")
            return JsonSerializer.Serialize(new
            {
                schemaVersion = "2.0", status, hasFailures = IndependentValidation.HasFailures(native, independent),
                coverage = "Partial", native.DocumentSha256,
                humanReviewRequired = true, native, independentValidation = independent,
                requirementsInventory = RequirementInventory.Summary
            }, AccessibilityReportWriter.Json);
        if (format == "sarif") return Sarif(native, independent, name);
        string baseReport = AccessibilityReportWriter.Write(native, name, format);
        if (format == "text")
        {
            var text = new StringBuilder("Combined result: " + status + ". Human review remains required.\n\nNative preflight\n");
            text.Append(baseReport).AppendLine().AppendLine("Independent validation (veraPDF)");
            text.Append("Result: ").AppendLine(independent.Status.ToString());
            text.Append("Version: ").AppendLine(Safe(independent.ValidatorVersion ?? "unavailable"));
            text.Append("Profile: ").AppendLine(Safe(independent.ProfileName ?? independent.RequestedProfile));
            if (independent.IncompleteReason != null) text.AppendLine(Safe(independent.IncompleteReason));
            foreach (var finding in independent.Findings.Take(20))
                text.Append("FAIL ").Append(Safe(finding.Specification)).Append(" / ").Append(Safe(finding.Clause))
                    .Append(" / ").Append(Safe(finding.TestNumber)).Append(": ").AppendLine(Safe(finding.Description));
            if (independent.Findings.Count > 20) text.AppendLine("Additional independent findings are available in JSON, SARIF or HTML output.");
            text.AppendLine("Machine validation does not resolve the human-review tasks. Use --requirements to inspect the complete Matterhorn inventory.");
            return text.ToString();
        }
        if (format == "html")
        {
            var section = new StringBuilder("<section aria-labelledby=\"independent\"><h2 id=\"independent\">Independent validation: veraPDF</h2>");
            section.Append("<p><strong>Combined result: ").Append(E(status)).Append(". Human review required.</strong></p>");
            section.Append("<p>Independent result: ").Append(E(independent.Status)).Append(" · version ").Append(E(independent.ValidatorVersion ?? "unavailable"))
                .Append(" · profile ").Append(E(independent.ProfileName ?? independent.RequestedProfile)).Append("</p>");
            if (independent.IncompleteReason != null) section.Append("<p>").Append(E(independent.IncompleteReason)).Append("</p>");
            section.Append("<p>These results cover machine checks only. They do not clear native findings or human-review tasks.</p><ul>");
            foreach (var finding in independent.Findings)
                section.Append("<li><strong>").Append(E(finding.Specification)).Append(" / ").Append(E(finding.Clause)).Append(" / ").Append(E(finding.TestNumber))
                    .Append("</strong>: ").Append(E(finding.Description)).Append("<ul>")
                    .Append(string.Concat(finding.Locations.Select(l => "<li><code>" + E(l) + "</code></li>"))).Append("</ul></li>");
            section.Append("</ul><p>Requirements inventory ").Append(E(RequirementInventory.Version)).Append(" accounts for 137 Matterhorn conditions. Applicability and human decisions remain unassessed. Use <code>freepdf-a11y --requirements --format json</code> for the inventory.</p></section><h2>Native preflight</h2>");
            return baseReport.Replace("<h1>Accessibility preflight</h1>", "<h1>Accessibility assessment</h1>" + section);
        }
        throw new ArgumentException("Unknown report format.");
    }

    static string Sarif(PdfAccessibilityReport native, IndependentValidation independent, string name)
    {
        var document = JsonNode.Parse(AccessibilityReportWriter.Write(native, name, "sarif"))!;
        var findings = independent.Findings.Select(f => new
        {
            ruleId = "VERAPDF/" + f.Specification + "/" + f.Clause + "/" + f.TestNumber,
            level = "error", message = new { text = f.Description },
            locations = new[] { new { physicalLocation = new { artifactLocation = new { uri = Uri.EscapeDataString(name) } } } },
            properties = new { f.FailedChecks, contexts = f.Locations }
        }).ToList<object>();
        findings.Add(new
        {
            ruleId = "VERAPDF/REVIEW", level = "note",
            message = new { text = "Human review remains required. Machine validation does not establish WCAG or ADA compliance." }
        });
        if (independent.Status == IndependentValidationStatus.Incomplete)
            findings.Add(new { ruleId = "VERAPDF/INCOMPLETE", level = "warning", message = new { text = independent.IncompleteReason } });
        var run = new
        {
            tool = new { driver = new { name = "veraPDF", version = independent.ValidatorVersion ?? "unknown", informationUri = "https://verapdf.org/" } },
            artifacts = new[] { new { location = new { uri = Uri.EscapeDataString(name) }, hashes = new Dictionary<string, string> { ["sha-256"] = independent.DocumentSha256 } } },
            invocations = new[] { new { executionSuccessful = independent.Status != IndependentValidationStatus.Incomplete } },
            results = findings,
            properties = new { independentValidation = independent, requirementsInventory = RequirementInventory.Summary }
        };
        document["runs"]!.AsArray().Add(JsonSerializer.SerializeToNode(run, AccessibilityReportWriter.Json));
        document["properties"] = JsonSerializer.SerializeToNode(new
        {
            status = IndependentValidation.Combine(native, independent), hasFailures = IndependentValidation.HasFailures(native, independent),
            humanReviewRequired = true, coverage = "Partial"
        }, AccessibilityReportWriter.Json);
        return document.ToJsonString(AccessibilityReportWriter.Json);
    }

    static string Safe(string value) => AccessibilityReportWriter.SafeText(value);
    static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
}
