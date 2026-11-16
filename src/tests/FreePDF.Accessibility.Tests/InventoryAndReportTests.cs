// Original FreePDF inventory and combined-report contracts. See LICENSE.
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.Accessibility.Tests;

public sealed class InventoryAndReportTests
{
    [Fact]
    public void InventoryAccountsForEverySourceIdIncludingVersion11Addition()
    {
        var document = RequirementInventory.Document.RootElement;
        var rows = document.GetProperty("requirements").EnumerateArray().ToArray();
        // Independent transcription of the table ranges, not a count generated from the inventory.
        int[] lengths = { 7,4,3,1,3,4,2,2,8,1,7,1,8,7,5,3,3,2,4,3,1,1,1,1,1,2,1,18,1,2,30 };
        var expected = lengths.SelectMany((length, checkpoint) => Enumerable.Range(1, length)
            .Select(index => $"MATTERHORN-1.1-{checkpoint + 1:00}-{index:000}")).ToArray();
        Assert.Equal(expected, rows.Select(r => r.GetProperty("id").GetString()));
        Assert.Equal(87, rows.Count(r => r.GetProperty("method").GetString() == "Machine"));
        Assert.Equal(48, rows.Count(r => r.GetProperty("method").GetString() == "Human"));
        Assert.Equal(2, rows.Count(r => r.GetProperty("method").GetString() == "SeeOtherRequirements"));
        Assert.Equal(31, document.GetProperty("checkpoints").GetArrayLength());
        Assert.All(rows, row =>
        {
            Assert.Equal("NotAssessed", row.GetProperty("documentOutcome").GetString());
            Assert.False(String.IsNullOrWhiteSpace(row.GetProperty("condition").GetString()));
            Assert.StartsWith("UA1:", row.GetProperty("pdfUaReference").GetString());
            Assert.Contains("#page=", row.GetProperty("source").GetString()!);
            Assert.False(String.IsNullOrWhiteSpace(row.GetProperty("evidenceProcedure").GetString()));
            foreach (var other in row.GetProperty("see").EnumerateArray()) Assert.Contains(other.GetString(), expected);
            Assert.NotEqual("Complete", row.GetProperty("nativeCoverage").GetString());
        });
        var rules = PdfAccessibilityScanner.Scan(new MemoryStream("invalid PDF"u8.ToArray())).Checks.Select(c => c.Id).ToArray();
        foreach (var row in rows)
            foreach (var rule in row.GetProperty("nativeRuleIds").EnumerateArray()) Assert.Contains(rule.GetString(), rules);
        Assert.Equal("CC-BY-4.0", document.GetProperty("sourceLicense").GetString());
        string[] References(string id) => rows.Single(r => r.GetProperty("id").GetString() == "MATTERHORN-1.1-" + id)
            .GetProperty("see").EnumerateArray().Select(v => v.GetString()!).ToArray();
        Assert.Equal(new[] { "28-010", "28-011", "28-017", "28-018" }.Select(id => "MATTERHORN-1.1-" + id), References("28-002"));
        Assert.Equal(new[] { "28-001", "28-002", "28-003", "28-004" }.Select(id => "MATTERHORN-1.1-" + id), References("28-006"));
        Assert.Equal(new[] { "01-002", "01-005" }.Select(id => "MATTERHORN-1.1-" + id), References("28-018"));
    }

    static PdfAccessibilityReport NativeFailure()
    {
        using var document = new PdfDocument();
        document.AddPage();
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        stream.Position = 0;
        return PdfAccessibilityScanner.Scan(stream);
    }

    [Fact]
    public void IndependentPassCannotClearNativeFailures()
    {
        var native = NativeFailure();
        Assert.Equal(PdfAccessibilityScanStatus.Failed, native.Status);
        Assert.Equal(PdfAccessibilityScanStatus.Failed, IndependentValidation.Combine(native,
            new IndependentValidation { Status = IndependentValidationStatus.MachineChecksPassed }));
    }

    [Fact]
    public void IncompleteIndependentRunTakesPrecedenceButKeepsNativeEvidence()
    {
        var native = NativeFailure();
        var independent = new IndependentValidation { IncompleteReason = "missing executable" };
        using var json = JsonDocument.Parse(IndependentReportWriter.Write(native, independent, "test.pdf", "json"));
        Assert.Equal("Incomplete", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Failed", json.RootElement.GetProperty("native").GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("humanReviewRequired").GetBoolean());
        Assert.Equal(48, json.RootElement.GetProperty("requirementsInventory").GetProperty("humanRequirementIds").GetArrayLength());
    }

    [Fact]
    public void IndependentResultsAreEncodedAndSeparateInEveryFormat()
    {
        var native = NativeFailure();
        var independent = new IndependentValidation
        {
            Status = IndependentValidationStatus.Failed, ValidatorVersion = "1.30.3", DocumentSha256 = native.DocumentSha256,
            Findings = new[] { new IndependentFinding("ISO", "7.3", "1", 1, "<script>alert(1)</script>\n\u001b[31m", new[] { "<object>" }) }
        };
        var html = IndependentReportWriter.Write(native, independent, "<img>.pdf", "html");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Combined result: Failed", html);
        var text = IndependentReportWriter.Write(native, independent, "a.pdf", "text");
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
        Assert.Contains("\\u001b", text);
        using var sarif = JsonDocument.Parse(IndependentReportWriter.Write(native, independent, "a.pdf", "sarif"));
        Assert.Equal(2, sarif.RootElement.GetProperty("runs").GetArrayLength());
        Assert.Equal("veraPDF", sarif.RootElement.GetProperty("runs")[1].GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        Assert.True(sarif.RootElement.GetProperty("properties").GetProperty("humanReviewRequired").GetBoolean());
    }
}
