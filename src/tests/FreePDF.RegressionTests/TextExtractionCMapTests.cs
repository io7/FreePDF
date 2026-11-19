// Original text extraction tests: ToUnicode CMaps, Type0 fonts and raw string bytes. MIT; see LICENSE.
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    static string ShowHex(string hex) => "BT /F1 12 Tf 72 700 Td <" + hex + "> Tj ET";

    [Fact]
    public void ToUnicode_wins_over_a_simple_fonts_encoding()
    {
        var pdf = TextPdf.Page(Show("AB"), ("/F1", d =>
        {
            var font = TextPdf.Font(d, "/Type1", "/Helvetica", new PdfName("/WinAnsiEncoding"));
            font.Elements["/ToUnicode"] = TextPdf.Stream(d, TextPdf.CMap("1 beginbfchar <41> <03B1> endbfchar",
                "1 begincodespacerange <00> <FF> endcodespacerange"));
            return font;
        }));
        Assert.Equal("αB", TextPdf.Text(pdf));
    }

    [Fact]
    public void Identity_H_with_bfrange_bfchar_and_array_destinations()
    {
        var cmap = "1 beginbfrange <0001> <0003> <0041> endbfrange\n"
                 + "1 beginbfrange <0004> <0005> [<00660069> <D83DDE00>] endbfrange\n"
                 + "1 beginbfchar <0006> <00E6> endbfchar";
        Assert.Equal("ABCfi\U0001F600æ", TextPdf.Text(TextPdf.Page(ShowHex("000100020003000400050006"), TextPdf.Composite(cmap))));
    }

    [Fact]
    public void Composite_font_without_ToUnicode_gives_replacement_characters()
        => Assert.Equal("��", TextPdf.Text(TextPdf.Page(ShowHex("00410042"), TextPdf.Composite(""))));

    [Fact]
    public void Codes_FE_FF_and_FF_FE_are_character_codes_not_UTF16_markers()
    {
        var cmap = "2 beginbfchar <FEFF> <00E6> <FFFE> <00F8> endbfchar\n1 beginbfchar <0001> <0041> endbfchar";
        Assert.Equal("æA", TextPdf.Text(TextPdf.Page(ShowHex("FEFF0001"), TextPdf.Composite(cmap))));
        Assert.Equal("øA", TextPdf.Text(TextPdf.Page("BT /F1 12 Tf 72 700 Td (ÿþ\u0000\u0001) Tj ET", TextPdf.Composite(cmap))));
    }

    [Fact]
    public void Embedded_encoding_CMap_with_one_and_two_byte_codes()
    {
        var encoding = TextPdf.CMap("1 begincidrange <8140> <817F> 100 endcidrange",
            "2 begincodespacerange <00> <80> <8140> <FFFF> endcodespacerange");
        var toUnicode = TextPdf.CMap("1 beginbfchar <41> <0058> endbfchar\n1 beginbfchar <8142> <0059> endbfchar",
            "2 begincodespacerange <00> <80> <8140> <FFFF> endcodespacerange");
        var pdf = TextPdf.Page("BT /F1 10 Tf 0 0 Td <41 8142> Tj ET", TextPdf.Composite(toUnicode, d => TextPdf.Stream(d, encoding),
            (d, cidFont) => cidFont.Elements["/W"] = TextPdf.Items(d, new PdfInteger(102), TextPdf.Numbers(d, 2000))));
        var run = Assert.Single(TextPdf.Runs(pdf));
        Assert.Equal("XY", run.Text);
        // The code 41 has CID 0 (no cidrange covers it): DW 1000 = 10 pt; 8142 is CID 102: 2000/1000 × 10 pt.
        Assert.Equal(30, run.EndX - run.X, 6);
    }

    [Fact]
    public void Overlapping_ranges_still_find_the_wider_earlier_range()
    {
        var cmap = "2 beginbfrange <0000> <00FF> <0000> <0010> <0010> <0042> endbfrange";
        Assert.Equal("a", TextPdf.Text(TextPdf.Page(ShowHex("0061"), TextPdf.Composite(cmap))));
    }

    [Fact]
    public void CMap_comments_dictionaries_usecmap_and_junk_are_skipped()
    {
        var cmap = "% a comment <0001> <0099>\n/Identity-H usecmap\n<< /Registry (Adobe) /Ordering (Identity) >> pop\n"
                 + "1 beginbfchar <0001> <0041> endbfchar\nendcmap ] ) > junk";
        Assert.Equal("A", TextPdf.Text(TextPdf.Page(ShowHex("0001"), TextPdf.Composite(cmap))));
    }
}
