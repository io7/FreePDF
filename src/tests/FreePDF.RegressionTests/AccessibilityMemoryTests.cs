// Original synthetic checks for the accessibility scan's memory bounds. MIT.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class AccessibilityScannerTests
{
    [Fact]
    public void Merged_saved_states_restore_in_the_right_order()
    {
        // The last two q save the same state and share one entry. Each Q must restore exactly one level:
        // (a) is shown with /FReview and (b) with /Missing. Popping the shared entry at once would show (a) with /Missing.
        var report = Scan(WithReviewFont("/Artifact BMC BT /Missing 10 Tf q /FReview 10 Tf q q Q Q (a) Tj Q (b) Tj ET EMC"));
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-FONT" && f.Message.Contains("no embedded font program"));
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-FONT" && f.Message.Contains("missing from the active resources"));
    }

    [Fact]
    public void Distinct_saved_states_are_capped()
    {
        var content = new StringBuilder("/Artifact BMC ");
        for (int i = 0; i <= 65536 / 2; i++) content.Append("q /A 1 Tf q /B 1 Tf ");
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content.ToString()))));
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-READ" && f.Message.Contains("Graphics state nesting"));
    }

    [Fact]
    public void Repeated_saves_without_a_state_change_are_not_capped()
    {
        var content = "/Artifact BMC " + String.Concat(Enumerable.Repeat("q ", 200_000)) + "EMC";
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content))));
        Assert.DoesNotContain(report.Findings, f => f.Message.Contains("Graphics state nesting"));
    }

    [Fact]
    public void Deeply_nested_xmp_is_not_checked()
    {
        var report = Scan(Fixture(d =>
        {
            var xml = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" + String.Concat(Enumerable.Repeat("<a>", 100)) + "1"
                + String.Concat(Enumerable.Repeat("</a>", 100)) + "</x:xmpmeta>";
            var metadata = new PdfDictionary(d);
            metadata.Elements.SetName("/Type", "/Metadata");
            metadata.Elements.SetName("/Subtype", "/XML");
            metadata.CreateStream(Encoding.UTF8.GetBytes(xml));
            d.Internals.AddObject(metadata);
            d.Catalog.Elements["/Metadata"] = metadata.Reference!;
        }));
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-UAID" && f.Message == "XMP nesting exceeds MaxDepth.");
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-TITLE" && f.Message == "XMP nesting exceeds MaxDepth.");
    }

    [Fact]
    public void MaxDuration_ends_the_scan_as_incomplete_and_keeps_earlier_failures()
    {
        var bytes = Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(
            Encoding.ASCII.GetBytes(String.Concat(Enumerable.Repeat("0 0 1 1 re f ", 2000)))));
        var report = Scan(bytes, new PdfAccessibilityScanOptions { MaxDuration = TimeSpan.FromTicks(1) });
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-READ" && f.Message == "Scan exceeded MaxDuration.");
        Fails(report, "CONTENT");
        Assert.Equal(PdfAccessibilityScanStatus.Failed, Scan(bytes, new PdfAccessibilityScanOptions { MaxDuration = null }).Status);
    }

    [Fact]
    public void MaxDuration_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Scan(Fixture(), new PdfAccessibilityScanOptions { MaxDuration = TimeSpan.Zero }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_shared_blank_alt_falls_back_to_actual_text(bool actualText)
    {
        var report = Scan(Fixture(d =>
        {
            var figure = Child(Child(d.Catalog.Elements.GetDictionary("/StructTreeRoot")!));
            var blank = new PdfStringObject(d, "   ");
            figure.Elements["/Alt"] = blank.Reference!;
            if (actualText) figure.Elements.SetString("/ActualText", "A red rectangle");
        }));
        Assert.Equal(actualText ? PdfAccessibilityCheckStatus.Passed : PdfAccessibilityCheckStatus.Failed, Outcome(report, "ALT"));
        Assert.Contains(report.StructureOrder, e => e.Role == "/Figure" && e.AlternativeText == (actualText ? "A red rectangle" : ""));
    }

    [Theory]
    [InlineData("<pdfuaid:part>1</pdfuaid:part>")]
    [InlineData("<pdfuaid:part rdf:parseType=\"Resource\"><rdf:value>1</rdf:value></pdfuaid:part>")]
    public void The_pdfua_declaration_is_read_from_its_own_text_or_rdf_value(string part)
    {
        var report = Scan(Fixture(d =>
        {
            var xml = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
                + "<rdf:Description rdf:about=\"\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\">" + part
                + "</rdf:Description></rdf:RDF></x:xmpmeta>";
            var metadata = new PdfDictionary(d);
            metadata.Elements.SetName("/Type", "/Metadata");
            metadata.Elements.SetName("/Subtype", "/XML");
            metadata.CreateStream(Encoding.UTF8.GetBytes(xml));
            d.Internals.AddObject(metadata);
            d.Catalog.Elements["/Metadata"] = metadata.Reference!;
        }));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "UAID"));
    }

    [Fact]
    public void A_failure_before_a_content_parse_error_is_kept()
    {
        // Operators are inspected as they are parsed, so untagged painting before the error is a definite failure.
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(
            Encoding.ASCII.GetBytes("0 0 1 1 re f " + new string('[', 40)))));
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.True(report.HasFailures);
        Fails(report, "CONTENT");
    }
}
