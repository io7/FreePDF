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

    // Every Figure element points to the same indirect /Alt string.
    static PdfAccessibilityReport SharedAlternativeText(int elements, int length)
    {
        using var document = new PdfDocument();
        document.AddPage();
        // The constructor registers the indirect string with the document.
        var alt = new PdfStringObject(document, new string('a', length));
        var root = new PdfDictionary(document);
        root.Elements.SetName("/Type", "/StructTreeRoot");
        document.Internals.AddObject(root);
        var kids = new PdfArray(document);
        for (int i = 0; i < elements; i++)
        {
            var figure = new PdfDictionary(document);
            figure.Elements.SetName("/Type", "/StructElem");
            figure.Elements.SetName("/S", "/Figure");
            figure.Elements["/P"] = root.Reference!;
            figure.Elements["/Alt"] = alt.Reference!;
            document.Internals.AddObject(figure);
            kids.Elements.Add(figure.Reference!);
        }
        root.Elements["/K"] = kids;
        document.Catalog.Elements["/StructTreeRoot"] = root.Reference!;
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        stream.Position = 0;
        return PdfAccessibilityScanner.Scan(stream);
    }

    [Fact]
    public void SharedAlternativeTextIsCutInWrittenReportsButNotInTheApi()
    {
        var native = SharedAlternativeText(elements: 200, length: 5000);
        Assert.Equal(200, native.StructureOrder.Count);
        Assert.All(native.StructureOrder, e => Assert.Equal(5000, e.AlternativeText.Length));
        var expected = new string('a', AccessibilityReportWriter.MaxAlternativeTextLength) + "...";
        using var json = JsonDocument.Parse(AccessibilityReportWriter.Write(native, "a.pdf", "json"));
        Assert.All(json.RootElement.GetProperty("structureOrder").EnumerateArray(),
            e => Assert.Equal(expected, e.GetProperty("alternativeText").GetString()));
        using var combined = JsonDocument.Parse(IndependentReportWriter.Write(native, new IndependentValidation(), "a.pdf", "json"));
        Assert.All(combined.RootElement.GetProperty("native").GetProperty("structureOrder").EnumerateArray(),
            e => Assert.Equal(expected, e.GetProperty("alternativeText").GetString()));
        var html = AccessibilityReportWriter.Write(native, "a.pdf", "html");
        Assert.Contains(expected, html);
        Assert.DoesNotContain(new string('a', AccessibilityReportWriter.MaxAlternativeTextLength + 1), html);
    }

    [Fact]
    public void StructureEntriesKeepTheirJsonShape()
    {
        var native = SharedAlternativeText(elements: 2, length: 12);
        var plain = new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        Assert.Equal(JsonSerializer.Serialize(native, plain), AccessibilityReportWriter.Write(native, "a.pdf", "json"));
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
        Assert.True(json.RootElement.GetProperty("hasFailures").GetBoolean());
        Assert.Equal("Failed", json.RootElement.GetProperty("native").GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("humanReviewRequired").GetBoolean());
        Assert.Equal(48, json.RootElement.GetProperty("requirementsInventory").GetProperty("humanRequirementIds").GetArrayLength());
    }

    [Fact]
    public void AnIndependentFailureIsReportedEvenWhenTheNativeScanCannotComplete()
    {
        using var input = new MemoryStream("not a PDF"u8.ToArray());
        var native = PdfAccessibilityScanner.Scan(input);
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, native.Status);
        Assert.False(native.HasFailures);

        var independent = new IndependentValidation { Status = IndependentValidationStatus.Failed };
        using var sarif = JsonDocument.Parse(IndependentReportWriter.Write(native, independent, "test.pdf", "sarif"));
        var combined = sarif.RootElement.GetProperty("properties");
        Assert.Equal("Incomplete", combined.GetProperty("status").GetString());
        Assert.True(combined.GetProperty("hasFailures").GetBoolean());
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
