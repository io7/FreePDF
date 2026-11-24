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
    [InlineData("/MacExpertEncoding", "<80> Tj", PdfAccessibilityCheckStatus.Passed)]
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

    // The entries are out of order, so the lookup works on the sorted ranges.
    const string ThreeRanges = "1 beginbfrange <0050> <0052> <0070> endbfrange "
        + "1 beginbfrange <0040> <0045> <0061> endbfrange 1 beginbfchar <0030> <0031> endbfchar";

    [Theory]
    [InlineData("<0030> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<0040> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<0043> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<0045> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<0052> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<0001> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("<0046> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("<0060> Tj", PdfAccessibilityCheckStatus.Failed)]
    public void Codes_are_looked_up_in_every_range(string content, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanIdentity(content, ThreeRanges), "UNICODE"));

    [Theory]
    [InlineData("2 beginbfchar <0041> <0061> <0041> <0062> endbfchar")]
    [InlineData("1 beginbfchar <0042> <0061> endbfchar 1 beginbfrange <0040> <0045> <0062> endbfrange")]
    public void Codes_mapped_twice_are_not_checked(string entries)
        => Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(ScanIdentity("<0041> Tj", entries), "UNICODE"));

    [Fact]
    public void Every_font_is_checked_and_each_map_is_decoded_once()
    {
        // 300 fonts with their own maps. Reading each map once fits the budget. Reading a map again for
        // every string, as a bounded cache did for the fonts past its limit, would exceed it.
        const int count = 300;
        var entries = new StringBuilder("100 beginbfchar ");
        for (int code = 0; code < 100; code++) entries.Append($"<{code:X4}> <{code + 0x100:X4}> ");
        var cmapText = "begincmap " + TwoCodes + entries + "endbfchar endcmap";
        var report = Scan(Fixture(d =>
        {
            var fonts = new PdfDictionary(d);
            var content = new StringBuilder("/Artifact BMC BT ");
            for (int i = 0; i < count; i++)
            {
                var info = new PdfDictionary(d);
                info.Elements.SetString("/Registry", "Adobe");
                info.Elements.SetString("/Ordering", "Identity");
                var child = new PdfDictionary(d);
                child.Elements.SetName("/Subtype", "/CIDFontType2");
                child.Elements["/CIDSystemInfo"] = info;
                var font = new PdfDictionary(d);
                font.Elements.SetName("/Subtype", "/Type0");
                font.Elements.SetName("/Encoding", "/Identity-H");
                font.Elements["/DescendantFonts"] = new PdfArray(d, child);
                var cmap = new PdfDictionary(d);
                cmap.CreateStream(Encoding.ASCII.GetBytes(cmapText));
                d.Internals.AddObject(cmap);
                font.Elements["/ToUnicode"] = cmap.Reference!;
                d.Internals.AddObject(font);
                fonts.Elements["/F" + i] = font.Reference!;
                content.Append($"/F{i} 12 Tf ");
                for (int s = 0; s < 40; s++) content.Append("<0041> Tj ");
            }
            // Only the last font shows a code its map leaves out.
            content.Append("<0100> Tj ET EMC");
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content.ToString()));
        }), new PdfAccessibilityScanOptions { MaxDecodedBytes = 1024 * 1024 });
        Assert.True(report.ScanCompleted);
        var finding = Assert.Single(report.Findings.Where(f => f.RuleId == "FPDF-A11Y-UNICODE"));
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, finding.Status);
        Assert.Equal($"/Resources/Font/F{count - 1}", finding.Location);
        Assert.Contains("<0100>", finding.Message);
    }

    [Theory]
    [InlineData("/FlateDecode", "x\u009Cÿÿÿÿ\u0000\u00137")]
    [InlineData("/ASCII85Decode", "!!!~x")]
    public void Damaged_map_leaves_only_its_font_unassessed(string filter, string data)
    {
        var report = Scan(Fixture(d =>
        {
            // Without a map, the glyph name outside the Adobe Glyph List would be a confirmed failure. The damaged
            // map might hold the code, so it can only stay unassessed.
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Subtype", "/Type1");
            var encoding = new PdfDictionary(d);
            encoding.Elements["/Differences"] = new PdfArray(d, new PdfInteger(65), new PdfName("/g123"));
            font.Elements["/Encoding"] = encoding;
            var cmap = new PdfDictionary(d);
            cmap.CreateStream(data.Select(c => (byte)c).ToArray());
            cmap.Elements.SetName("/Filter", filter);
            d.Internals.AddObject(cmap);
            font.Elements["/ToUnicode"] = cmap.Reference!;
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Text"] = font;
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Text 12 Tf <41> Tj ET EMC"));
        }));
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "UNICODE"));
        // The scan went on past the map: the reader check ran last, and the font check still reports its failure.
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "CONTENT"));
        Fails(report, "FONT");
    }

    // ISO 14289-1 7.21.7 and Matterhorn 31-028/31-029: no mapped value may be U+0000, U+FEFF or U+FFFE.
    [Theory]
    [InlineData("1 beginbfchar <0041> <0000> endbfchar")]
    [InlineData("1 beginbfchar <0041> <FEFF> endbfchar")]
    [InlineData("1 beginbfchar <0041> <FFFE> endbfchar")]
    [InlineData("1 beginbfchar <0041> <00410000> endbfchar")]
    [InlineData("1 beginbfrange <0040> <0041> <FEFE> endbfrange")]
    [InlineData("1 beginbfrange <0041> <0042> [<FFFE> <0062>] endbfrange")]
    public void Shown_code_mapped_to_a_forbidden_value_fails(string entries)
    {
        var report = ScanIdentity("<0041> Tj <0041> Tj", entries);
        var finding = Assert.Single(report.Findings.Where(f => f.RuleId == "FPDF-A11Y-UNICODE"));
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, finding.Status);
        Assert.Contains("<0041>", finding.Message);
        Assert.Contains("U+FEFF", finding.Message);
    }

    [Theory]
    [InlineData("2 beginbfchar <0041> <0061> <0042> <0000> endbfchar")]
    [InlineData("1 beginbfrange <0041> <0042> <FEFE> endbfrange")]
    [InlineData("1 beginbfrange <0040> <0042> <00FD> endbfrange")]
    public void Forbidden_values_of_codes_not_shown_are_ignored(string entries)
        => Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(ScanIdentity("<0041> Tj", entries), "UNICODE"));

    [Fact]
    public void A_forbidden_value_fails_in_a_simple_font_too()
        => Fails(ScanSimple("<41> Tj", encoding: "/WinAnsiEncoding", toUnicode: OneByteMap("1 beginbfchar <41> <0000> endbfchar")), "UNICODE");

    // Each case was compared with veraPDF 1.30.3 (PDF/UA-1, rule 7.21.7) on 2026-11-24. NotChecked marks cases the
    // checker cannot settle without the font program or a readable map.
    [Theory]
    [InlineData("/Type1", "65 /A", null, "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /space", null, "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /radicalex", null, "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /a1", null, "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type3", "65 /A", null, "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/TrueType", "65 /A", "/WinAnsiEncoding", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "64 /at /A", null, "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /uni0041", null, "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("/Type1", "65 /u1F600", null, "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("/Type1", "65 /f_i", null, "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("/Type1", "65 /A.sc", null, "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("/Type1", "65 /g123", "/WinAnsiEncoding", "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("/TrueType", "65 /g123", "/WinAnsiEncoding", "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("/Type1", "65 /.notdef", null, "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("/Type1", "65 /A", null, "<42> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("/TrueType", "65 /A", null, "<42> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("/Type1", "65 /A", "/WinAnsiEncoding", "<42> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /A", "/WinAnsiEncoding", "<81> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /A", "/MacRomanEncoding", "<42> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /A", "/MacExpertEncoding", "<80> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "65 /A", "/StandardEncoding", "<42> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("/Type1", "300 /A", null, "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    public void Differences_names_and_base_encodings_decide_simple_codes(string subtype, string differences,
        string? baseEncoding, string show, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanSimple(show, subtype, differences: differences, baseEncoding: baseEncoding), "UNICODE"));

    [Theory]
    [InlineData("/Type1", "/WinAnsiEncoding", "<81> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "/MacExpertEncoding", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/TrueType", "/MacExpertEncoding", "<80> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/TrueType", "/MacRomanEncoding", "<FF> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Type1", "/StandardEncoding", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("/TrueType", null, "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    public void A_named_encoding_maps_every_code(string subtype, string? encoding, string show, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanSimple(show, subtype, encoding), "UNICODE"));

    [Fact]
    public void A_glyph_name_outside_the_lists_is_reported_with_its_code_and_name()
    {
        var report = ScanSimple("<41> Tj", "/Type1", differences: "65 /g123");
        var finding = Assert.Single(report.Findings.Where(f => f.RuleId == "FPDF-A11Y-UNICODE"));
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, finding.Status);
        Assert.Contains("<41>", finding.Message);
        Assert.Contains("/g123", finding.Message);
        Assert.Equal("/Resources/Font/Text", finding.Location);
    }

    // A readable map without the code leaves the decision to the encoding. A map that cannot be read may hold
    // the code, so it stops a failure but no longer hides a named encoding.
    [Theory]
    [InlineData("65 /A", null, true, PdfAccessibilityCheckStatus.Passed)]
    [InlineData("65 /g123", null, true, PdfAccessibilityCheckStatus.Failed)]
    [InlineData("65 /g123", null, false, PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData(null, "/WinAnsiEncoding", true, PdfAccessibilityCheckStatus.Passed)]
    [InlineData(null, "/WinAnsiEncoding", false, PdfAccessibilityCheckStatus.Passed)]
    public void A_map_without_the_code_falls_back_to_the_encoding(string? differences, string? encoding, bool readable,
        PdfAccessibilityCheckStatus expected)
    {
        var map = readable ? OneByteMap("1 beginbfchar <42> <0042> endbfchar") : "begincmap junk ( endcmap";
        Assert.Equal(expected, Outcome(ScanSimple("<41> Tj", "/Type1", encoding, differences, toUnicode: map), "UNICODE"));
    }

    [Theory]
    [InlineData("/Helvetica", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Symbol", "<61> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("/Helvetica", "<80> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    public void Unembedded_standard_fonts_use_their_built_in_encoding(string baseFont, string show, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanSimple(show, "/Type1", descriptor: false, baseFont: baseFont), "UNICODE"));

    static string OneByteMap(string entries)
        => "begincmap 1 begincodespacerange <00> <FF> endcodespacerange " + entries + " endcmap";

    // A simple font without a font program. Differences are written as in PDF: "65 /A /B 70 /C".
    static PdfAccessibilityReport ScanSimple(string show, string subtype = "/TrueType", string? encoding = null,
        string? differences = null, string? baseEncoding = null, string? toUnicode = null, bool descriptor = true,
        string baseFont = "/SyntheticSimple")
    {
        return Scan(Fixture(d =>
        {
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", subtype);
            font.Elements.SetName("/BaseFont", baseFont);
            if (descriptor)
            {
                var fd = new PdfDictionary(d);
                fd.Elements.SetName("/Type", "/FontDescriptor");
                fd.Elements.SetName("/FontName", baseFont);
                fd.Elements.SetInteger("/Flags", 32);
                d.Internals.AddObject(fd);
                font.Elements["/FontDescriptor"] = fd.Reference!;
            }
            if (encoding != null) font.Elements.SetName("/Encoding", encoding);
            if (differences != null || baseEncoding != null)
            {
                var dictionary = new PdfDictionary(d);
                dictionary.Elements.SetName("/Type", "/Encoding");
                if (baseEncoding != null) dictionary.Elements.SetName("/BaseEncoding", baseEncoding);
                if (differences != null)
                {
                    var array = new PdfArray(d);
                    foreach (var part in differences.Split(' '))
                    {
                        if (part.StartsWith("/", StringComparison.Ordinal)) array.Elements.Add(new PdfName(part));
                        else array.Elements.Add(new PdfInteger(int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    dictionary.Elements["/Differences"] = array;
                }
                font.Elements["/Encoding"] = dictionary;
            }
            if (toUnicode != null)
            {
                var cmap = new PdfDictionary(d);
                cmap.CreateStream(Encoding.ASCII.GetBytes(toUnicode));
                d.Internals.AddObject(cmap);
                font.Elements["/ToUnicode"] = cmap.Reference!;
            }
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Text"] = font;
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Text 12 Tf " + show + " ET EMC"));
        }));
    }
}
