// Original synthetic Unicode mapping controls. See LICENSE.
using System.Text;
using FreePDF.Lab.Fixtures;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class AccessibilityScannerTests
{
    const string TwoCodes = "1 begincodespacerange <0000> <FFFF> endcodespacerange ";
    const string MappedA = "1 beginbfchar <0041> <0061> endbfchar";

    static PdfAccessibilityReport ScanIdentity(string content, string? entries = MappedA,
        string encoding = "/Identity-H", string ordering = "Identity", bool inForm = false)
    {
        return Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            var resources = page.Elements.GetDictionary("/Resources")!;
            var info = new PdfDictionary(d);
            info.Elements.SetString("/Registry", "Adobe");
            info.Elements.SetString("/Ordering", ordering);
            var child = new PdfDictionary(d);
            child.Elements.SetName("/Subtype", "/CIDFontType2");
            child.Elements["/CIDSystemInfo"] = info;
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Subtype", "/Type0");
            font.Elements.SetName("/Encoding", encoding);
            font.Elements["/DescendantFonts"] = new PdfArray(d, child);
            if (entries != null)
            {
                var cmap = new PdfDictionary(d);
                cmap.CreateStream(Encoding.ASCII.GetBytes("begincmap " + TwoCodes + entries + " endcmap"));
                d.Internals.AddObject(cmap);
                font.Elements["/ToUnicode"] = cmap.Reference!;
            }
            if (inForm)
            {
                var form = AccessibilityFormFixture.AddForm(d, resources, "/Inner", "BT /Text 12 Tf " + content + " ET");
                resources = form.Elements.GetDictionary("/Resources")!;
                content = "/Inner Do";
            }
            else content = "BT /Text 12 Tf " + content + " ET";
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Text"] = font;
            resources.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC " + content + " EMC"));
        }));
    }

    [Theory]
    [InlineData("<0041> Tj")]
    [InlineData("[<0041> -10 <0042>] TJ")]
    [InlineData("<0041> '")]
    [InlineData("0 0 <0041> \"")]
    public void Shown_codes_resolve_through_a_range(string content)
        => Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(ScanIdentity(content,
            "1 beginbfrange <0041> <0042> [<D83DDE00> <00660069>] endbfrange"), "UNICODE"));

    [Fact]
    public void Bom_bytes_in_glyph_codes_are_preserved()
        => Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(ScanIdentity("<FEFF0041> Tj",
            "2 beginbfchar <FEFF> <0042> <0041> <FFFD> endbfchar"), "UNICODE"));

    [Fact]
    public void Unused_codes_are_ignored_but_a_missing_code_in_a_form_is_located()
    {
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(ScanIdentity("<0041> Tj"), "UNICODE"));
        var report = ScanIdentity("[<0041> 2 <0042>] TJ <0042> Tj", inForm: true);
        var finding = Assert.Single(report.Findings.Where(f => f.RuleId == "FPDF-A11Y-UNICODE"));
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, finding.Status);
        Assert.Equal(1, finding.Page);
        Assert.Equal("/Resources/XObject/Inner/Resources/Font/Text", finding.Location);
        Assert.Contains("<0042>", finding.Message);
    }

    [Fact]
    public void Missing_map_fails_even_for_invisible_text()
        => Fails(ScanIdentity("3 Tr <0041> Tj", entries: null), "UNICODE");

    [Fact]
    public void Empty_text_and_numeric_adjustments_do_not_require_a_mapping()
        => Assert.Equal(PdfAccessibilityCheckStatus.NotApplicable,
            Outcome(ScanIdentity("() Tj [20 <> -30] TJ", entries: null), "UNICODE"));

    [Theory]
    [InlineData("1 beginbfchar <0041> <D800> endbfchar")]
    [InlineData("1 beginbfchar <0041> <0Z61> endbfchar")]
    [InlineData("/Other usecmap")]
    [InlineData("2 beginbfchar <0041> <0061> endbfchar")]
    public void Inconclusive_map_is_not_checked(string entries)
        => Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(ScanIdentity("<0041> Tj", entries), "UNICODE"));

    [Fact]
    public void Unsupported_encodings_and_character_collections_remain_unassessed()
    {
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(ScanIdentity("<0041> Tj", encoding: "/90ms-RKSJ-H"), "UNICODE"));
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(ScanIdentity("<0042> Tj", ordering: "Japan1"), "UNICODE"));
    }

    [Theory]
    [InlineData("/WinAnsiEncoding", "<80> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/MacRomanEncoding", "<80> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/MacExpertEncoding", "<80> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("/MacExpertEncoding", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    public void Simple_fonts_use_an_explicit_map_or_a_known_encoding(string encoding, string show,
        PdfAccessibilityCheckStatus expected)
    {
        var report = Scan(Fixture(d =>
        {
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Subtype", "/TrueType");
            font.Elements.SetName("/Encoding", encoding);
            var cmap = new PdfDictionary(d);
            cmap.CreateStream(Encoding.ASCII.GetBytes("begincmap 1 begincodespacerange <00> <ff> endcodespacerange "
                + "1 beginbfchar <41> <0041> endbfchar endcmap"));
            d.Internals.AddObject(cmap);
            font.Elements["/ToUnicode"] = cmap.Reference!;
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Text"] = font;
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Text 12 Tf " + show + " ET EMC"));
        }));
        Assert.Equal(expected, Outcome(report, "UNICODE"));
    }

    [Fact]
    public void Truncated_code_keeps_a_confirmed_mapping_failure()
    {
        var report = ScanIdentity("<004200> Tj");
        Assert.False(report.ScanCompleted);
        Fails(report, "UNICODE");
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-UNICODE" && f.Status == PdfAccessibilityCheckStatus.NotChecked);
    }
}
