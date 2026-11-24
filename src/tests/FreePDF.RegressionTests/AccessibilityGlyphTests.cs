// Original synthetic glyph controls (Matterhorn 31-011 and 31-030). See LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;
using static FreePDF.RegressionTests.SyntheticTrueType;

namespace FreePDF.RegressionTests;

public partial class AccessibilityScannerTests
{
    static readonly byte[] FourGlyphs = Build(4);

    // A Type0 font with Identity-H over a CIDFontType2 whose descriptor embeds the given program as FontFile2.
    static PdfAccessibilityReport ScanCid(string content, byte[]? program = null, Action<PdfDocument, PdfDictionary>? editChild = null,
        string fontFileKey = "/FontFile2", string childSubtype = "/CIDFontType2")
    {
        return Scan(Fixture(d =>
        {
            var info = new PdfDictionary(d);
            info.Elements.SetString("/Registry", "Adobe");
            info.Elements.SetString("/Ordering", "Identity");
            info.Elements.SetInteger("/Supplement", 0);
            var descriptor = new PdfDictionary(d);
            descriptor.Elements.SetName("/Type", "/FontDescriptor");
            descriptor.Elements.SetName("/FontName", "/Synthetic");
            descriptor.Elements.SetInteger("/Flags", 32);
            if (program != null)
            {
                var file = new PdfDictionary(d);
                file.CreateStream(program);
                d.Internals.AddObject(file);
                descriptor.Elements[fontFileKey] = file.Reference!;
            }
            d.Internals.AddObject(descriptor);
            var child = new PdfDictionary(d);
            child.Elements.SetName("/Type", "/Font");
            child.Elements.SetName("/Subtype", childSubtype);
            child.Elements.SetName("/BaseFont", "/Synthetic");
            child.Elements["/CIDSystemInfo"] = info;
            child.Elements["/FontDescriptor"] = descriptor.Reference!;
            editChild?.Invoke(d, child);
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type0");
            font.Elements.SetName("/BaseFont", "/Synthetic");
            font.Elements.SetName("/Encoding", "/Identity-H");
            font.Elements["/DescendantFonts"] = new PdfArray(d, child);
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Text"] = font;
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Text 12 Tf " + content + " ET EMC"));
        }));
    }

    static PdfAccessibilityFinding GlyphFinding(PdfAccessibilityReport report)
        => Assert.Single(report.Findings.Where(f => f.RuleId == "FPDF-A11Y-GLYPH"));

    // veraPDF 1.30.3 reports glyph 0 as rule 7.21.8-1 and a glyph past the program's count as 7.21.4.1-2.
    [Theory]
    [InlineData("<0001> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<0003> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<0000> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("<0004> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("[<0001> 10 <FFFF>] TJ", PdfAccessibilityCheckStatus.Failed)]
    public void Cid_codes_select_glyphs_of_the_embedded_program(string content, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanCid(content, FourGlyphs), "GLYPH"));

    [Fact]
    public void Glyph_zero_is_reported_as_notdef()
    {
        var finding = GlyphFinding(ScanCid("<0000> Tj", FourGlyphs));
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, finding.Status);
        Assert.Contains("<0000>", finding.Message);
        Assert.Contains(".notdef", finding.Message);
        Assert.Equal("/Resources/Font/Text", finding.Location);
    }

    [Fact]
    public void A_glyph_past_the_program_is_reported_as_missing()
    {
        var finding = GlyphFinding(ScanCid("<0009> Tj", FourGlyphs));
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, finding.Status);
        Assert.Contains("<0009>", finding.Message);
        Assert.Contains("does not contain", finding.Message);
    }

    // ISO 14289-1 7.21.8 applies in every rendering mode; 7.21.4.1 only to text used for rendering.
    [Theory]
    [InlineData("3 Tr <0009> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("3 Tr <0000> Tj", PdfAccessibilityCheckStatus.Failed)]
    public void Invisible_text_is_checked_for_notdef_only(string content, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanCid(content, FourGlyphs), "GLYPH"));

    // ISO 32000-1 9.7.4.2: glyph index for CID c in bytes 2c and 2c + 1. veraPDF 1.30.3 passes a map entry of 0;
    // the native rule follows ISO 14289-1 and reports .notdef.
    [Theory]
    [InlineData("<0001> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("<0002> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("<0003> Tj", PdfAccessibilityCheckStatus.Failed, "does not contain")]
    public void A_CIDToGIDMap_stream_selects_the_glyph(string content, PdfAccessibilityCheckStatus expected, string? message)
    {
        var report = ScanCid(content, FourGlyphs, (d, child) =>
        {
            var map = new PdfDictionary(d);
            map.CreateStream(new byte[] { 0, 1, 0, 0, 0, 3 });   // CID 0 -> 1, CID 1 -> 0, CID 2 -> 3; CID 3 is past the map
            d.Internals.AddObject(map);
            child.Elements["/CIDToGIDMap"] = map.Reference!;
        });
        Assert.Equal(expected, Outcome(report, "GLYPH"));
        if (message != null) Assert.Contains(message, GlyphFinding(report).Message);
    }

    // Visible text in a font without a program fails the font check (Matterhorn 31-009), and 31-011 concerns embedded
    // programs, so the glyph check judges only its .notdef names. Invisible text is not covered by the font check.
    [Theory]
    [InlineData("<0001> Tj", PdfAccessibilityCheckStatus.NotApplicable)]
    [InlineData("3 Tr <0001> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    public void A_font_without_a_program_is_left_to_the_font_check(string content, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanCid(content), "GLYPH"));

    [Theory]
    [InlineData("damaged")]
    [InlineData("OTTO")]
    [InlineData("ttcf")]
    [InlineData("cff")]
    public void Programs_the_check_cannot_read_leave_the_text_unassessed(string kind)
    {
        var report = kind switch
        {
            "damaged" => ScanCid("<0000> Tj", Encoding.ASCII.GetBytes("not a font program")),
            "OTTO" => ScanCid("<0000> Tj", Build(4, tag: "OTTO")),
            "ttcf" => ScanCid("<0000> Tj", Build(4, tag: "ttcf")),
            _ => ScanCid("<0000> Tj", FourGlyphs, fontFileKey: "/FontFile3", childSubtype: "/CIDFontType0"),
        };
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "GLYPH"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
    }
}
