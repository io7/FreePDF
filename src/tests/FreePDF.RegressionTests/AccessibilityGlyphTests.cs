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

    // A program that inflates past its 32 MiB limit leaves its font unassessed; the scan goes on.
    [Fact]
    public void A_program_past_its_limit_leaves_only_its_font_unassessed()
    {
        using var deflated = new MemoryStream();
        deflated.WriteByte(0x78);
        deflated.WriteByte(0x9C);
        // A valid program that only its size makes unreadable.
        var program = Build(4, post: false, padding: 33 * 1024 * 1024);
        using (var deflate = new System.IO.Compression.DeflateStream(deflated, System.IO.Compression.CompressionLevel.Optimal, true))
            deflate.Write(program, 0, program.Length);
        var report = ScanCid("<0001> Tj", deflated.ToArray(), (d, child) =>
        {
            var file = child.Elements.GetDictionary("/FontDescriptor")!.Elements.GetDictionary("/FontFile2")!;
            file.Elements.SetName("/Filter", "/FlateDecode");
        });
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "GLYPH"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
    }

    // Decoding counts toward the 256 MiB total for all programs also when a program passes its own limit: after eight
    // distinct programs that each inflate past 32 MiB, a valid program is no longer read, so its .notdef stays
    // unassessed.
    [Fact]
    public void Programs_past_their_limit_count_toward_the_total()
    {
        var bomb = Deflated(Build(4, post: false, padding: 33 * 1024 * 1024));
        var report = Scan(Fixture(d =>
        {
            var fonts = new PdfDictionary(d);
            var content = new StringBuilder("/Artifact BMC BT ");
            for (int i = 0; i < 9; i++)
            {
                var file = new PdfDictionary(d);
                // Each bomb is its own stream; the last font has a valid program.
                file.CreateStream(i < 8 ? bomb : FourGlyphs);
                if (i < 8) file.Elements.SetName("/Filter", "/FlateDecode");
                d.Internals.AddObject(file);
                var descriptor = new PdfDictionary(d);
                descriptor.Elements["/FontFile2"] = file.Reference!;
                var child = new PdfDictionary(d);
                child.Elements.SetName("/Subtype", "/CIDFontType2");
                child.Elements["/FontDescriptor"] = descriptor;
                var font = new PdfDictionary(d);
                font.Elements.SetName("/Subtype", "/Type0");
                font.Elements.SetName("/Encoding", "/Identity-H");
                font.Elements["/DescendantFonts"] = new PdfArray(d, child);
                fonts.Elements["/F" + i] = font;
                content.Append($"/F{i} 12 Tf <0000> Tj ");
            }
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content + "ET EMC"));
        }));
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "FPDF-A11Y-GLYPH" && f.Status == PdfAccessibilityCheckStatus.Failed);
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "GLYPH"));
    }

    static byte[] Deflated(byte[] data)
    {
        using var deflated = new MemoryStream();
        deflated.WriteByte(0x78);
        deflated.WriteByte(0x9C);
        using (var deflate = new System.IO.Compression.DeflateStream(deflated, System.IO.Compression.CompressionLevel.Optimal, true))
            deflate.Write(data, 0, data.Length);
        return deflated.ToArray();
    }

    // Fonts often share a descendant, its program and its CIDToGIDMap. Each is read once per scan, so 600 fonts fit a
    // 1 MiB decode budget that reading the 64 KiB map for each font would pass, and the 256 MiB program total that
    // counting the 4 MiB program for each font would pass.
    [Fact]
    public void Fonts_sharing_a_program_and_a_map_read_them_once()
    {
        const int count = 600;
        var program = Build(4, post: false, padding: 4 * 1024 * 1024);
        var report = Scan(Fixture(d =>
        {
            var file = new PdfDictionary(d);
            file.CreateStream(program);
            d.Internals.AddObject(file);
            var descriptor = new PdfDictionary(d);
            descriptor.Elements.SetName("/Type", "/FontDescriptor");
            descriptor.Elements["/FontFile2"] = file.Reference!;
            d.Internals.AddObject(descriptor);
            var map = new PdfDictionary(d);
            var mapBytes = new byte[64 * 1024];
            for (int cid = 0; cid < mapBytes.Length / 2; cid++) mapBytes[2 * cid + 1] = (byte)(cid % 3 + 1);
            map.CreateStream(mapBytes);
            d.Internals.AddObject(map);
            var info = new PdfDictionary(d);
            info.Elements.SetString("/Registry", "Adobe");
            info.Elements.SetString("/Ordering", "Identity");
            var child = new PdfDictionary(d);
            child.Elements.SetName("/Subtype", "/CIDFontType2");
            child.Elements["/CIDSystemInfo"] = info;
            child.Elements["/FontDescriptor"] = descriptor.Reference!;
            child.Elements["/CIDToGIDMap"] = map.Reference!;
            d.Internals.AddObject(child);
            var fonts = new PdfDictionary(d);
            var content = new StringBuilder("/Artifact BMC BT ");
            for (int i = 0; i < count; i++)
            {
                var font = new PdfDictionary(d);
                font.Elements.SetName("/Subtype", "/Type0");
                font.Elements.SetName("/Encoding", "/Identity-H");
                font.Elements["/DescendantFonts"] = new PdfArray(d, child.Reference!);
                d.Internals.AddObject(font);
                fonts.Elements["/F" + i] = font.Reference!;
                content.Append($"/F{i} 12 Tf <0001> Tj ");
            }
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content + "ET EMC"));
        }), new PdfAccessibilityScanOptions { MaxDecodedBytes = 1024 * 1024 });
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "GLYPH"));
    }

    static Cmap Map(int platform, int encoding, int format, params int[] codeGlyphPairs)
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < codeGlyphPairs.Length; i += 2) map[codeGlyphPairs[i]] = codeGlyphPairs[i + 1];
        return new Cmap(platform, encoding, format, map);
    }

    static readonly byte[] UnicodeA = Build(4, new[] { Map(3, 1, 4, 0x41, 1) });

    // ISO 32000-1 9.6.6.4 for simple TrueType fonts. Nonsymbolic: the glyph name's Unicode value in (3,1), its
    // MacRoman code in (1,0), or a name of its own in 'post'. Symbolic: the code in (3,0), also from F000, or in (1,0).
    [Theory]
    [InlineData("winansi-present", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("winansi-absent", "<42> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("glyph-past-program", "<41> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("standard-names-in-post", "<42> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("unnamed-glyphs-in-post", "<42> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("custom-name", "<42> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("custom-name-absent", "<42> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("mac-roman-code", "<E9> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("format-4-glyph-array", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("format-4-glyph-array", "<42> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("format-4-glyph-array", "<43> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("format-12", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("format-6", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("unreadable-format", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("mac-expert-base", "<42> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("ligature-name", "<42> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("standard-encoding", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("notdef-name", "<42> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("symbolic-3-0", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("symbolic-3-0", "<42> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("symbolic-1-0", "<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("symbolic-1-0", "<42> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("symbolic-without-subtable", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    // WinAnsi 80 is Euro. The base encoding gives no name to look up, and 'post' may name the glyph.
    [InlineData("base-code-post-names", "<80> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    // A damaged format 4 subtable points past its own end into the next subtable.
    [InlineData("subtable-overrun", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    // A cut 'post' table cannot show that the name is absent.
    [InlineData("post-cut", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    // A 'post' table shorter than its 32-byte header is present but cannot be read.
    [InlineData("post-header-cut", "<41> Tj", PdfAccessibilityCheckStatus.NotChecked)]
    public void Simple_TrueType_codes_follow_the_font_cmap(string kind, string show, PdfAccessibilityCheckStatus expected)
    {
        var report = kind switch
        {
            "winansi-present" or "winansi-absent" => ScanSimple(show, encoding: "/WinAnsiEncoding", program: UnicodeA),
            "glyph-past-program" => ScanSimple(show, encoding: "/WinAnsiEncoding", program: Build(4, new[] { Map(3, 1, 4, 0x41, 9) })),
            "standard-names-in-post" => ScanSimple(show, encoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 4, 0x41, 1) }, new[] { null, "#36", null, null })),
            "unnamed-glyphs-in-post" => ScanSimple(show, encoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 4, 0x41, 1) }, new[] { null, "first", null, null })),
            "custom-name" => ScanSimple(show, differences: "66 /g123", baseEncoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 4, 0x41, 1) }, new[] { null, null, "g123", null })),
            "custom-name-absent" => ScanSimple(show, differences: "66 /g124", baseEncoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 4, 0x41, 1) }, new[] { null, "#36", "g123", null })),
            // WinAnsi E9 is eacute, MacRoman 8E.
            "mac-roman-code" => ScanSimple(show, encoding: "/WinAnsiEncoding", program: Build(4, new[] { Map(1, 0, 0, 0x8E, 3) })),
            "format-4-glyph-array" => ScanSimple(show, encoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 41, 0x41, 1, 0x42, 0, 0x43, 2) })),
            "format-12" => ScanSimple(show, encoding: "/WinAnsiEncoding", program: Build(4, new[] { Map(3, 1, 12, 0x41, 1) })),
            "format-6" => ScanSimple(show, encoding: "/WinAnsiEncoding", program: Build(4, new[] { Map(1, 0, 6, 0x41, 2) })),
            "unreadable-format" => ScanSimple(show, encoding: "/WinAnsiEncoding", program: Build(4, new[] { Map(3, 1, 99) })),
            "mac-expert-base" => ScanSimple(show, differences: "65 /A", baseEncoding: "/MacExpertEncoding", program: UnicodeA),
            "ligature-name" => ScanSimple(show, differences: "66 /f_i", baseEncoding: "/WinAnsiEncoding", program: UnicodeA),
            // No /Encoding and the Nonsymbolic flag: StandardEncoding gives the name.
            "standard-encoding" => ScanSimple(show, program: UnicodeA),
            "notdef-name" => ScanSimple(show, differences: "66 /.notdef", baseEncoding: "/WinAnsiEncoding", program: UnicodeA),
            "symbolic-3-0" => ScanSimple(show, flags: 4, program: Build(4, new[] { Map(3, 0, 4, 0xF041, 1) })),
            "symbolic-1-0" => ScanSimple(show, flags: 4, program: Build(4, new[] { Map(1, 0, 0, 0x41, 1) })),
            "base-code-post-names" => ScanSimple(show, encoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 4, 0x41, 2) }, new[] { null, "Euro", null, null })),
            "subtable-overrun" => ScanSimple(show, encoding: "/WinAnsiEncoding",
                program: Build(16, new[] { Map(3, 1, 42, 0x41, 0), Map(3, 10, 12, 0x41, 5) })),
            "post-cut" => ScanSimple(show, differences: "65 /g123", baseEncoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 4, 0x42, 2) }, new[] { null, "zzzz", null, null }, postCut: 2)),
            "post-header-cut" => ScanSimple(show, differences: "65 /g123", baseEncoding: "/WinAnsiEncoding",
                program: Build(4, new[] { Map(3, 1, 4, 0x42, 2) }, postCut: 16)),
            _ => ScanSimple(show, flags: 4, program: UnicodeA),
        };
        Assert.Equal(expected, Outcome(report, "GLYPH"));
    }

    [Fact]
    public void A_missing_simple_glyph_is_reported_with_its_name()
    {
        var finding = GlyphFinding(ScanSimple("<42> Tj", differences: "66 /g124", baseEncoding: "/WinAnsiEncoding", program: UnicodeA));
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, finding.Status);
        Assert.Contains("<42> (glyph /g124)", finding.Message);
        Assert.Contains("does not contain", finding.Message);
    }

    // A Type 1 /Differences name of .notdef selects .notdef whether or not the program is embedded. Other Type 1 codes
    // need the program, which this check does not read yet; without one, visible text is left to the font check.
    [Theory]
    [InlineData("65 /.notdef", false, PdfAccessibilityCheckStatus.Failed)]
    [InlineData("65 /A", false, PdfAccessibilityCheckStatus.NotApplicable)]
    [InlineData("65 /A", true, PdfAccessibilityCheckStatus.NotChecked)]
    public void Type1_fonts_are_judged_by_notdef_names(string differences, bool embedded, PdfAccessibilityCheckStatus expected)
        => Assert.Equal(expected, Outcome(ScanSimple("<41> Tj", "/Type1", differences: differences,
            program: embedded ? Encoding.ASCII.GetBytes("%!PS-AdobeFont-1.0: Synthetic") : null), "GLYPH"));

    // A code that only the base encoding names takes its ISO 32000-1 Annex D name: WinAnsi 41 is A, 43 is C, 80 is Euro
    // and C4 is Adieresis.
    [Theory]
    [InlineData("<41> Tj", PdfAccessibilityCheckStatus.Passed)]
    [InlineData("<43> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("<80> Tj", PdfAccessibilityCheckStatus.Failed)]
    [InlineData("<C4> Tj", PdfAccessibilityCheckStatus.Passed)]
    public void Type3_base_encoding_codes_use_their_Annex_D_names(string show, PdfAccessibilityCheckStatus expected)
    {
        var report = Scan(Fixture(d =>
        {
            var procedure = new PdfDictionary(d);
            procedure.CreateStream(Encoding.ASCII.GetBytes("500 0 0 0 500 500 d1 0 0 500 500 re f"));
            d.Internals.AddObject(procedure);
            var procedures = new PdfDictionary(d);
            procedures.Elements["/A"] = procedure.Reference!;
            procedures.Elements["/Adieresis"] = procedure.Reference!;
            procedures.Elements["/b"] = procedure.Reference!;
            var encoding = new PdfDictionary(d);
            encoding.Elements.SetName("/BaseEncoding", "/WinAnsiEncoding");
            encoding.Elements["/Differences"] = new PdfArray(d, new PdfInteger(66), new PdfName("/b"));
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Subtype", "/Type3");
            font.Elements["/CharProcs"] = procedures;
            font.Elements["/Encoding"] = encoding;
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Text"] = font;
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Text 12 Tf " + show + " ET EMC"));
        }));
        Assert.Equal(expected, Outcome(report, "GLYPH"));
    }

    // A Type 3 glyph exists when /CharProcs has a procedure for its name. veraPDF 1.30.3: a missing procedure is
    // rule 7.21.4.1-2, a name of .notdef rule 7.21.8-1.
    [Theory]
    [InlineData("<41> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    [InlineData("<42> Tj", PdfAccessibilityCheckStatus.Failed, "<42> (glyph /b) selects a glyph that the embedded font program does not contain")]
    [InlineData("<43> Tj", PdfAccessibilityCheckStatus.Failed, "<43> selects the .notdef glyph")]
    [InlineData("3 Tr <42> Tj", PdfAccessibilityCheckStatus.Passed, null)]
    public void Type3_glyphs_need_a_procedure(string show, PdfAccessibilityCheckStatus expected, string? message)
    {
        var report = Scan(Fixture(d =>
        {
            var procedure = new PdfDictionary(d);
            procedure.CreateStream(Encoding.ASCII.GetBytes("500 0 0 0 500 500 d1 0 0 500 500 re f"));
            d.Internals.AddObject(procedure);
            var procedures = new PdfDictionary(d);
            procedures.Elements["/a"] = procedure.Reference!;
            var encoding = new PdfDictionary(d);
            encoding.Elements["/Differences"] = new PdfArray(d, new PdfInteger(65), new PdfName("/a"), new PdfName("/b"), new PdfName("/.notdef"));
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type3");
            font.Elements["/CharProcs"] = procedures;
            font.Elements["/Encoding"] = encoding;
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Text"] = font;
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Text 12 Tf " + show + " ET EMC"));
        }));
        Assert.Equal(expected, Outcome(report, "GLYPH"));
        if (message != null) Assert.Contains(message, GlyphFinding(report).Message);
    }
}
