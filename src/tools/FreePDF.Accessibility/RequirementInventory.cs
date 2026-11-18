// Original FreePDF code. The embedded Matterhorn-derived data has its own attribution.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreePDF.Accessibility;

internal static class RequirementInventory
{
    static readonly byte[] Bytes = Load();
    internal static readonly JsonDocument Document = JsonDocument.Parse(Bytes);
    internal static string Version => Document.RootElement.GetProperty("inventoryVersion").GetString()!;
    internal static object Summary => new
    {
        version = Version,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(Bytes)),
        profile = "PDF/UA-1", protocol = "Matterhorn 1.1",
        requirementCount = Document.RootElement.GetProperty("requirements").GetArrayLength(),
        documentApplicability = "NotAssessed",
        humanReviewRequired = true,
        humanRequirementIds = Document.RootElement.GetProperty("requirements").EnumerateArray()
            .Where(r => r.GetProperty("method").GetString() == "Human")
            .Select(r => r.GetProperty("id").GetString()).ToArray(),
        note = "Inventory entries are not per-document verdicts. Human tasks and unsupported native checks remain open; a machine-validator pass does not complete them."
    };

    internal static string Write(string format)
    {
        if (format == "json") return Encoding.UTF8.GetString(Bytes);
        if (format != "text") throw new ArgumentException("The requirements inventory supports text or json output.");
        var text = new StringBuilder($"FreePDF requirements inventory {Version} — PDF/UA-1 / Matterhorn 1.1\n");
        text.AppendLine("137 conditions: 87 machine, 48 human, 2 referring to other conditions. This is not a document verdict.");
        foreach (var row in Document.RootElement.GetProperty("requirements").EnumerateArray())
            text.Append(row.GetProperty("id").GetString()).Append(" [native: ")
                .Append(row.GetProperty("nativeCoverage").GetString()).Append("] ")
                .AppendLine(AccessibilityReportWriter.SafeText(row.GetProperty("condition").GetString()!));
        text.AppendLine("Source: Copyright 2021 PDF Association, Matterhorn Protocol 1.1, CC-BY-4.0. FreePDF added mappings and coverage metadata; see --format json for references and attribution.");
        return text.ToString();
    }

    static byte[] Load()
    {
        using var stream = typeof(RequirementInventory).Assembly.GetManifestResourceStream("FreePDF.Accessibility.requirements.json")
            ?? throw new InvalidOperationException("Requirements inventory is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
