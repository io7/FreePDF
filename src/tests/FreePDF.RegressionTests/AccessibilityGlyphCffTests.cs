// Original synthetic glyph controls for CFF programs (Matterhorn 31-011 and 31-030). See LICENSE.
using System.Text;
using FreePDF.Lab.Fixtures;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class AccessibilityScannerTests
{
    // Glyphs 1 to 3 are CIDs 1, 5 and 7.
    static readonly byte[] CidKeyed = SyntheticCff.Cid(new[] { 1, 5, 7 });

    static PdfAccessibilityReport ScanCff(string content, byte[] program, string fileSubtype = "/CIDFontType0C")
        => ScanCid(content, program, fontFileKey: "/FontFile3", childSubtype: "/CIDFontType0", fileSubtype: fileSubtype);

    // ISO 32000-1 9.7.4.2: a CID-keyed CFF program's charset gives the glyph of a CID. veraPDF 1.30.3 agrees: CID 0 is
    // rule 7.21.8-1, a CID that the charset lacks 7.21.8-1 and 7.21.4.1-2, even where a glyph of that index exists.
    [Theory]
    [InlineData("<0001> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("<0005> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("<0007> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("<0000> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("<0002> Tj", PdfAccessibilityCheckStatus.Failed, "does not contain")]
    [InlineData("<0009> Tj", PdfAccessibilityCheckStatus.Failed, "does not contain")]
    [InlineData("3 Tr <0002> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("3 Tr <0000> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    public void Cid_keyed_cff_selects_glyphs_through_its_charset(string content, PdfAccessibilityCheckStatus expected, string? message)
    {
        var report = ScanCff(content, CidKeyed);
        Assert.Equal(expected, Outcome(report, "GLYPH"));
        if (message != null) Assert.Contains(message, GlyphFinding(report).Message);
    }

    // Charset formats 1 and 2 give ranges of CIDs.
    [Theory]
    [InlineData(1, "<000B> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData(2, "<000B> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData(1, "<0004> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData(2, "<0004> Tj", PdfAccessibilityCheckStatus.Failed)]
    public void Cid_charset_ranges_are_read(int format, string content, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanCff(content, SyntheticCff.Cid(new[] { 1, 2, 3, 10, 11 }, format)), "GLYPH"));

    // Without CIDFont operators the CIDs are glyph indexes (ISO 32000-1 9.7.4.2), and glyph 0 is .notdef. veraPDF
    // 1.30.3 passes glyph 0 here (no 7.21.8-1); the native rule follows ISO 14289-1.
    [Theory]
    [InlineData("<0002> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("<0000> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("<0003> Tj", PdfAccessibilityCheckStatus.Failed, "does not contain")]
    public void Cff_without_cid_operators_uses_cids_as_glyph_indexes(string content, PdfAccessibilityCheckStatus expected, string? message)
    {
        var report = ScanCff(content, SyntheticCff.Named(new[] { "#34", "#35" }));
        Assert.Equal(expected, Outcome(report, "GLYPH"));
        if (message != null) Assert.Contains(message, GlyphFinding(report).Message);
    }

    [Theory]
    [InlineData("damaged")]
    [InlineData("cff2")]
    [InlineData("two-fonts")]
    [InlineData("synthetic")]
    [InlineData("truncated")]
    [InlineData("predefined-charset")]
    [InlineData("opentype")]
    [InlineData("no-subtype")]
    public void Cff_programs_the_check_cannot_read_leave_the_text_unassessed(string kind)
    {
        var report = kind switch
        {
            "damaged" => ScanCff("<0000> Tj", Encoding.ASCII.GetBytes("not a font program")),
            "cff2" => ScanCff("<0000> Tj", SyntheticCff.Named(new[] { "#34" }, major: 2)),
            "two-fonts" => ScanCff("<0000> Tj", SyntheticCff.Named(new[] { "#34" }, fonts: 2)),
            "synthetic" => ScanCff("<0000> Tj", SyntheticCff.Named(new[] { "#34" }, syntheticBase: true)),
            // FDSelect, FDArray and Private DICT take the last 33 bytes; 36 cuts into the CharStrings INDEX.
            "truncated" => ScanCff("<0000> Tj", CidKeyed.Take(CidKeyed.Length - 36).ToArray()),
            // CIDFonts must not use predefined charsets (Adobe TN 5176, section 13).
            "predefined-charset" => ScanCff("<0000> Tj", SyntheticCff.Cid(new[] { 1 }, charsetId: 0)),
            "opentype" => ScanCff("<0000> Tj", CidKeyed, "/OpenType"),
            _ => ScanCid("<0000> Tj", CidKeyed, fontFileKey: "/FontFile3", childSubtype: "/CIDFontType0"),
        };
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "GLYPH"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
    }

    // Glyphs A (standard string 34), B (35) and Euro (a custom string) under the predefined Standard Encoding.
    static readonly byte[] NamedAbEuro = SyntheticCff.Named(new[] { "#34", "#35", "Euro" });

    static PdfAccessibilityReport ScanType1C(string show, byte[] program, string? encoding = null, string? differences = null,
        string? baseEncoding = null)
        => ScanSimple(show, "/Type1", encoding, differences, baseEncoding, program: program, fontFileKey: "/FontFile3",
            fileSubtype: "/Type1C");

    // ISO 32000-1 9.6.6.2: the glyph name comes from /Differences, else the named base encoding, else the program's
    // built-in encoding, and the program has a glyph of that name or not. veraPDF 1.30.3 agrees except where the
    // built-in encoding names no glyph: it passes those codes, and the native rule reports .notdef.
    [Theory]
    [InlineData("winansi", "<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("winansi", "<80> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("winansi", "<43> Tj", PdfAccessibilityCheckStatus.Failed, "<43> (glyph /C) selects a glyph that the embedded font program does not contain")]
    [InlineData("winansi", "<01> Tj", PdfAccessibilityCheckStatus.Failed, "<01> selects the .notdef glyph")]
    [InlineData("winansi", "3 Tr <43> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("macroman", "<AD> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("built-in", "<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("built-in", "<80> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("differences", "<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("differences", "<42> Tj", PdfAccessibilityCheckStatus.Failed, "(glyph /uniE000)")]
    [InlineData("differences", "<43> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("differences-winansi", "<80> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("mac-expert", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked, null)]
    public void Type1C_codes_select_glyphs_by_name(string kind, string show, PdfAccessibilityCheckStatus expected, string? message)
    {
        var report = kind switch
        {
            "winansi" => ScanType1C(show, NamedAbEuro, "/WinAnsiEncoding"),
            "macroman" => ScanType1C(show, NamedAbEuro, "/MacRomanEncoding"),
            "built-in" => ScanType1C(show, NamedAbEuro),
            "differences" => ScanType1C(show, NamedAbEuro, differences: "66 /uniE000 /.notdef"),
            "differences-winansi" => ScanType1C(show, NamedAbEuro, differences: "66 /B", baseEncoding: "/WinAnsiEncoding"),
            _ => ScanType1C(show, NamedAbEuro, differences: "66 /B", baseEncoding: "/MacExpertEncoding"),
        };
        Assert.Equal(expected, Outcome(report, "GLYPH"));
        if (message != null) Assert.Contains(message, GlyphFinding(report).Message);
    }

    // A custom built-in encoding gives codes to glyphs (format 0 lists codes, format 1 ranges), and supplements give a
    // glyph further codes by name. Glyphs A, B and C have codes 61, 62 and 63; supplements give 70 to B and 71 to Z,
    // which the font lacks.
    [Theory]
    [InlineData(0, "<61> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData(1, "<63> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData(0, "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData(1, "<70> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData(0, "<71> Tj", PdfAccessibilityCheckStatus.Failed)]
    public void Type1C_custom_encodings_are_read(int format, string show, PdfAccessibilityCheckStatus expected)
    {
        var program = SyntheticCff.Named(new[] { "#34", "#35", "#36" }, codes: new[] { 0x61, 0x62, 0x63 },
            supplements: new[] { (0x70, "#35"), (0x71, "#59") }, encodingFormat: format);
        Assert.Equal(expected, Outcome(ScanType1C(show, program), "GLYPH"));
    }

    // The predefined ISOAdobe charset names glyph i with standard string i; 40 glyphs end at standard string 39.
    [Theory]
    [InlineData("<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<5A> Tj", PdfAccessibilityCheckStatus.Failed)]
    public void Type1C_iso_adobe_charset_names_glyphs_by_standard_strings(string show, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanType1C(show, SyntheticCff.Named(Array.Empty<string>(), charsetId: 0, glyphCount: 40),
            "/WinAnsiEncoding"), "GLYPH"));

    [Theory]
    [InlineData("expert-charset")]
    [InlineData("expert-encoding")]
    [InlineData("cid-keyed")]
    public void Type1C_programs_the_check_cannot_name_leave_the_text_unassessed(string kind)
    {
        var report = kind switch
        {
            "expert-charset" => ScanType1C("<41> Tj", SyntheticCff.Named(Array.Empty<string>(), charsetId: 1, glyphCount: 5), "/WinAnsiEncoding"),
            // The Expert built-in encoding is not at hand; named encodings still decide.
            "expert-encoding" => ScanType1C("<41> Tj", SyntheticCff.Named(new[] { "#34" }, encodingId: 1)),
            _ => ScanType1C("<41> Tj", CidKeyed, "/WinAnsiEncoding"),
        };
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "GLYPH"));
    }

    [Fact]
    public void Type1C_expert_encoding_still_follows_a_named_encoding()
        => Assert.Equal(PdfAccessibilityCheckStatus.Passed,
            Outcome(ScanType1C("<41> Tj", SyntheticCff.Named(new[] { "#34" }, encodingId: 1), "/WinAnsiEncoding"), "GLYPH"));

    // The glyph tables kept from programs share 1,048,576 entries per scan. Sixteen distinct programs of 65,535 glyphs
    // fit; a seventeenth leaves its font unassessed.
    [Theory]
    [InlineData(16, PdfAccessibilityCheckStatus.Passed)]
    [InlineData(17, PdfAccessibilityCheckStatus.NotChecked)]
    public void Glyph_tables_share_one_total_per_scan(int fonts, PdfAccessibilityCheckStatus expected)
    {
        var program = SyntheticCff.Cid(Enumerable.Range(1, 65534).ToArray(), charsetFormat: 2, emptyGlyphs: true);
        var report = Scan(Fixture(d =>
        {
            var resources = new PdfDictionary(d);
            var content = new StringBuilder("/Artifact BMC BT ");
            for (int i = 0; i < fonts; i++)
            {
                var file = new PdfDictionary(d);
                file.CreateStream(program);
                file.Elements.SetName("/Subtype", "/CIDFontType0C");
                d.Internals.AddObject(file);
                var descriptor = new PdfDictionary(d);
                descriptor.Elements["/FontFile3"] = file.Reference!;
                var child = new PdfDictionary(d);
                child.Elements.SetName("/Subtype", "/CIDFontType0");
                child.Elements["/FontDescriptor"] = descriptor;
                var font = new PdfDictionary(d);
                font.Elements.SetName("/Subtype", "/Type0");
                font.Elements.SetName("/Encoding", "/Identity-H");
                font.Elements["/DescendantFonts"] = new PdfArray(d, child);
                resources.Elements["/F" + i] = font;
                content.Append($"/F{i} 12 Tf <FFFE> Tj ");
            }
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = resources;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content + "ET EMC"));
        }));
        Assert.Equal(expected, Outcome(report, "GLYPH"));
    }
}
