// Original synthetic glyph controls for Type 1 programs (Matterhorn 31-011 and 31-030). See LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class AccessibilityScannerTests
{
    static readonly byte[] Type1AbEuro = SyntheticType1.Build(new[] { "A", "B", "Euro" });

    static PdfAccessibilityReport ScanType1(string show, byte[] program, string? encoding = null, string? differences = null,
        string? baseEncoding = null)
        => ScanSimple(show, "/Type1", encoding, differences, baseEncoding, program: program);

    // ISO 32000-1 9.6.6.2: the glyph name comes from /Differences, else the named base encoding, else the program's
    // built-in encoding, and the CharStrings dictionary has a glyph of that name or not. veraPDF 1.30.3 agrees; where
    // the built-in encoding names no glyph it reports rule 7.21.4.1-2, and the native rule reports .notdef.
    [Theory]
    [InlineData("winansi", "<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("winansi", "<80> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("winansi", "<43> Tj", PdfAccessibilityCheckStatus.Failed, "<43> (glyph /C) selects a glyph that the embedded font program does not contain")]
    [InlineData("winansi", "<01> Tj", PdfAccessibilityCheckStatus.Failed, "<01> selects the .notdef glyph")]
    [InlineData("winansi", "3 Tr <43> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("built-in", "<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("built-in", "<80> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("differences", "<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("differences", "<42> Tj", PdfAccessibilityCheckStatus.Failed, "(glyph /uniE000)")]
    [InlineData("differences", "<43> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("custom-built-in", "<61> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("custom-built-in", "<62> Tj", PdfAccessibilityCheckStatus.Failed, "(glyph /Z)")]
    [InlineData("custom-built-in", "<41> Tj", PdfAccessibilityCheckStatus.Failed, ".notdef")]
    [InlineData("unknown-built-in", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked, null)]
    [InlineData("unknown-built-in-winansi", "<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    public void Type1_codes_select_glyphs_by_name(string kind, string show, PdfAccessibilityCheckStatus expected, string? message)
    {
        var report = kind switch
        {
            "winansi" => ScanType1(show, Type1AbEuro, "/WinAnsiEncoding"),
            "built-in" => ScanType1(show, Type1AbEuro),
            "differences" => ScanType1(show, Type1AbEuro, differences: "66 /uniE000 /.notdef"),
            "custom-built-in" => ScanType1(show, SyntheticType1.Build(new[] { "A" }, "97 A 98 Z")),
            "unknown-built-in" => ScanType1(show, SyntheticType1.Build(new[] { "A" }, "ISOLatin1Encoding")),
            _ => ScanType1(show, SyntheticType1.Build(new[] { "A" }, "ISOLatin1Encoding"), "/WinAnsiEncoding"),
        };
        Assert.Equal(expected, Outcome(report, "GLYPH"));
        if (message != null) Assert.Contains(message, GlyphFinding(report).Message);
    }

    // The encrypted part may be hexadecimal, and the RD, ND and NP procedures may have other names (-|, |- and |).
    [Theory]
    [InlineData("hex", "<42> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("hex", "<43> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("aliases", "<42> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("aliases", "<43> Tj", PdfAccessibilityCheckStatus.Failed)]
    public void Type1_program_forms_are_read(string kind, string show, PdfAccessibilityCheckStatus expected)
    {
        var program = kind == "hex"
            ? SyntheticType1.Build(new[] { "A", "B" }, hex: true)
            : SyntheticType1.Build(new[] { "A", "B" }, aliases: ("-|", "|-", "|"));
        Assert.Equal(expected, Outcome(ScanType1(show, program, "/WinAnsiEncoding"), "GLYPH"));
    }

    // Each Type 1 and CFF program is read once per scan: 300 fonts sharing each 4 MiB program stay far below the 256 MiB
    // total for all programs, which reading the program for each font would pass.
    [Theory]
    [InlineData("/FontFile")]
    [InlineData("/FontFile3")]
    public void Fonts_sharing_a_type1_or_cff_program_read_it_once(string key)
    {
        var padding = new byte[4 * 1024 * 1024];
        var program = key == "/FontFile"
            ? SyntheticType1.Build(new[] { "A" }).Concat(padding).ToArray()
            : SyntheticCff.Named(new[] { "#34" }).Concat(padding).ToArray();
        var report = Scan(Fixture(d =>
        {
            var file = new PdfDictionary(d);
            file.CreateStream(program);
            if (key == "/FontFile3") file.Elements.SetName("/Subtype", "/Type1C");
            d.Internals.AddObject(file);
            var descriptor = new PdfDictionary(d);
            descriptor.Elements.SetName("/Type", "/FontDescriptor");
            descriptor.Elements[key] = file.Reference!;
            d.Internals.AddObject(descriptor);
            var fonts = new PdfDictionary(d);
            var content = new StringBuilder("/Artifact BMC BT ");
            for (int i = 0; i < 300; i++)
            {
                var font = new PdfDictionary(d);
                font.Elements.SetName("/Subtype", "/Type1");
                font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
                font.Elements["/FontDescriptor"] = descriptor.Reference!;
                d.Internals.AddObject(font);
                fonts.Elements["/F" + i] = font.Reference!;
                content.Append($"/F{i} 12 Tf <41> Tj ");
            }
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content + "ET EMC"));
        }));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "GLYPH"));
    }

    [Theory]
    [InlineData("pfb")]
    [InlineData("clear-text")]
    [InlineData("cut")]
    [InlineData("length-past-end")]
    [InlineData("damaged")]
    public void Type1_programs_the_check_cannot_read_leave_the_text_unassessed(string kind)
    {
        var good = SyntheticType1.Build(new[] { "A" });
        byte[] program = kind switch
        {
            // A PFB file's segment headers are not a PDF font program.
            "pfb" => new byte[] { 0x80, 0x01, 0x10, 0, 0, 0 }.Concat(good).ToArray(),
            "clear-text" => SyntheticType1.Build(new[] { "A" }, encrypt: false),
            // The last 102 characters close the dictionaries; 110 cut into the last CharStrings entry, so no end shows
            // that every glyph was read.
            "cut" => SyntheticType1.Build(new[] { "A", "B", "C" }, cutTail: 110),
            "length-past-end" => SyntheticType1.Build(new[] { "A" }, charStringLength: 100000),
            _ => good.Take(good.Length / 2).Concat(Enumerable.Repeat((byte)0x5A, good.Length / 2)).ToArray(),
        };
        var report = ScanType1("<43> Tj", program, "/WinAnsiEncoding");
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "GLYPH"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
    }
}
