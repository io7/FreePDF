// Original synthetic controls for the glyph-table total of the glyph check. See LICENSE.
using System.Text;
using FreePDF.Lab.Fixtures;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;
using static FreePDF.RegressionTests.SyntheticTrueType;

namespace FreePDF.RegressionTests;

public partial class AccessibilityScannerTests
{
    // A 255-character name that differs from the others in its first five characters.
    static string LongName(int i) => "n" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + new string('x', 250);

    // The glyph names kept from programs share 1,048,576 entries per scan, and a name takes one entry more for every 16
    // characters. Each program below keeps about 4,096 names of 255 characters:
    // - CFF reserves its 4,098 glyphs, then 15 more per long name: 65,538 entries, so 15 programs fit and 16 do not;
    // - Type 1 reserves 16 per long name (4,095 of them) and 1 each for .notdef and A: 65,522, so 16 fit and 17 do not;
    // - a TrueType 'post' table reserves its 4,096 used strings, then 15 more per string: 65,536, so 16 fit and 17 do not.
    // Counting one entry per name, every case would fit.
    [Theory]
    [InlineData("cff", 15, PdfAccessibilityCheckStatus.Passed)]
    [InlineData("cff", 16, PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("type1", 16, PdfAccessibilityCheckStatus.Passed)]
    [InlineData("type1", 17, PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("truetype", 16, PdfAccessibilityCheckStatus.Passed)]
    [InlineData("truetype", 17, PdfAccessibilityCheckStatus.NotChecked)]
    public void Long_names_take_more_of_the_glyph_table_total(string kind, int programs, PdfAccessibilityCheckStatus expected)
    {
        var names = Enumerable.Range(0, 4096).Select(LongName).ToArray();
        var report = kind switch
        {
            // Glyph 1 is A, shown under the built-in Standard Encoding.
            "cff" => ScanManyFonts(programs, SyntheticCff.Named(new[] { "#34" }.Concat(names).ToArray()), "/FontFile3", "/Type1C", "/Type1"),
            "type1" => ScanManyFonts(programs, SyntheticType1.Build(new[] { "A" }.Concat(names.Skip(1)).ToArray()), "/FontFile", null, "/Type1"),
            // No cmap: /Differences names glyph 1 by its 'post' name.
            _ => ScanManyFonts(programs, Build(4097, postNames: new string?[] { null }.Concat(names).ToArray()), "/FontFile2", null,
                "/TrueType", "/WinAnsiEncoding", "65 /" + names[0]),
        };
        Assert.Equal(expected, Outcome(report, "GLYPH"));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
    }

    // Names that only a built-in encoding keeps count too. Each CFF program has glyph A and 192 encoding supplements
    // that name absent 255-character glyphs: 2 + 192 * 16 = 3,074 entries, so 341 programs fit and 342 do not. Each
    // Type 1 program's built-in array names A and 255 long names (1 + 255 * 16), and its CharStrings .notdef and A: 4,083
    // entries, so 256 fit and 257 do not.
    [Theory]
    [InlineData("cff", 341, PdfAccessibilityCheckStatus.Passed)]
    [InlineData("cff", 342, PdfAccessibilityCheckStatus.NotChecked)]
    [InlineData("type1", 256, PdfAccessibilityCheckStatus.Passed)]
    [InlineData("type1", 257, PdfAccessibilityCheckStatus.NotChecked)]
    public void Encoding_names_count_toward_the_glyph_table_total(string kind, int programs, PdfAccessibilityCheckStatus expected)
    {
        byte[] program;
        if (kind == "cff")
        {
            var supplements = Enumerable.Range(0, 256).Where(code => code < 0x40 || code >= 0x80)
                .Select(code => (code, LongName(code))).ToArray();
            program = SyntheticCff.Named(new[] { "#34" }, codes: new[] { 0x41 }, supplements: supplements);
        }
        else
        {
            var pairs = Enumerable.Range(0, 256).Select(code => code + " " + (code == 0x41 ? "A" : LongName(code)));
            program = SyntheticType1.Build(new[] { "A" }, string.Join(" ", pairs));
        }
        var report = ScanManyFonts(programs, program, kind == "cff" ? "/FontFile3" : "/FontFile", kind == "cff" ? "/Type1C" : null, "/Type1");
        Assert.Equal(expected, Outcome(report, "GLYPH"));
    }

    // Each font has its own program stream with the same bytes, so each program is read, and shows <41>.
    static PdfAccessibilityReport ScanManyFonts(int count, byte[] program, string fontFileKey, string? fileSubtype, string subtype,
        string? encoding = null, string? differences = null)
    {
        return Scan(Fixture(d =>
        {
            var fonts = new PdfDictionary(d);
            var content = new StringBuilder("/Artifact BMC BT ");
            for (int i = 0; i < count; i++)
            {
                var file = new PdfDictionary(d);
                file.CreateStream(program);
                if (fileSubtype != null) file.Elements.SetName("/Subtype", fileSubtype);
                d.Internals.AddObject(file);
                var descriptor = new PdfDictionary(d);
                descriptor.Elements.SetInteger("/Flags", 32);
                descriptor.Elements[fontFileKey] = file.Reference!;
                var font = new PdfDictionary(d);
                font.Elements.SetName("/Subtype", subtype);
                font.Elements["/FontDescriptor"] = descriptor;
                if (differences != null)
                {
                    var dictionary = new PdfDictionary(d);
                    if (encoding != null) dictionary.Elements.SetName("/BaseEncoding", encoding);
                    var parts = differences.Split(' ');
                    dictionary.Elements["/Differences"] = new PdfArray(d, new PdfInteger(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)), new PdfName(parts[1]));
                    font.Elements["/Encoding"] = dictionary;
                }
                else if (encoding != null) font.Elements.SetName("/Encoding", encoding);
                fonts.Elements["/F" + i] = font;
                content.Append($"/F{i} 12 Tf <41> Tj ");
            }
            var page = d.Pages[0];
            page.Elements.GetDictionary("/Resources")!.Elements["/Font"] = fonts;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content + "ET EMC"));
        }));
    }
}
