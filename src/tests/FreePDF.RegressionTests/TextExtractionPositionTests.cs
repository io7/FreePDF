// Original text extraction tests: text positions, spacing and FreePDF's own font output. MIT; see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    static (string, Func<PdfDocument, PdfDictionary>) WinAnsiHelvetica()
        => TextPdf.Standard("/Helvetica", _ => new PdfName("/WinAnsiEncoding"));

    // The synthetic box font draws every character with one glyph, so in Unicode (Identity-H, code = glyph) its
    // characters cannot be told apart by any reader; Unicode cases use the supplementary font's distinct glyphs.
    [Theory]
    [InlineData(PdfFontEncoding.WinAnsi, "FreePDF Synthetic", "Hello, world")]
    [InlineData(PdfFontEncoding.Unicode, "FreePDF Supplementary", "A ŐőƐ Ă")]
    [InlineData(PdfFontEncoding.Unicode, "FreePDF Supplementary", "A Āā \U0001D400\U0001F600")]
    public void Text_drawn_by_XGraphics_round_trips_with_its_baseline(PdfFontEncoding encoding, string family, string text)
    {
        TestFonts.Initialize();
        var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString(text, new XFont(family, 12, XFontStyleEx.Regular, new XPdfFontOptions(encoding)), XBrushes.Black, 100, 200);
        double height = page.Height.Point;
        var run = Assert.Single(TextPdf.Runs(TextPdf.Save(document)));
        Assert.Equal(text, run.Text);
        Assert.Equal(100, run.X, 2);
        Assert.Equal(height - 200, run.Y, 2);
        Assert.Equal(12, run.FontSize, 2);
    }

    [Fact]
    public void TJ_gaps_wider_than_a_kern_become_spaces()
        => Assert.Equal("Hello World", TextPdf.Text(TextPdf.Page("BT /F1 12 Tf 72 700 Td [(Hel) 40 (lo) -300 (Wor) -80 (ld)] TJ ET", WinAnsiHelvetica())));

    [Fact]
    public void Widths_character_word_spacing_and_scaling_move_the_end()
    {
        // Helvetica: a 556, space 278 (1/1000 em). At 10 pt: 13.9 pt; Tc 1 per glyph (3), Tw 5 for the space; Tz 50 halves it.
        var run = Assert.Single(TextPdf.Runs(TextPdf.Page("BT /F1 10 Tf 1 Tc 5 Tw 50 Tz 0 0 Td (a a) Tj ET", WinAnsiHelvetica())));
        Assert.Equal((13.9 + 3 + 5) * 0.5, run.EndX - run.X, 6);
    }

    [Fact]
    public void Line_operators_and_leading_place_lines()
    {
        var content = "BT /F1 10 Tf 72 700 Td (One) Tj 0 -12 Td (Two) Tj 12 TL T* (Three) Tj (Four) ' 1 0 (Five) \" ET";
        var pdf = TextPdf.Page(content, WinAnsiHelvetica());
        Assert.Equal("One\nTwo\nThree\nFour\nFive", TextPdf.Text(pdf));
        Assert.Equal(new double[] { 700, 688, 676, 664, 652 }, TextPdf.Runs(pdf).Select(r => Math.Round(r.Y, 6)).ToArray());
    }

    [Fact]
    public void CTM_rise_and_text_matrix_scale_the_position_and_size()
    {
        var run = Assert.Single(TextPdf.Runs(TextPdf.Page("2 0 0 2 0 0 cm BT /F1 10 Tf 3 Ts 1.5 0 0 1.5 10 20 Tm (x) Tj ET", WinAnsiHelvetica())));
        Assert.Equal(20, run.X, 6);
        Assert.Equal((20 + 3 * 1.5) * 2, run.Y, 6);
        Assert.Equal(30, run.FontSize, 6);
    }

    [Fact]
    public void Standard_font_without_Widths_uses_the_AFM_widths()
    {
        var run = Assert.Single(TextPdf.Runs(TextPdf.Page("BT /F1 10 Tf 0 0 Td (A) Tj ET", WinAnsiHelvetica())));
        Assert.Equal(6.67, run.EndX - run.X, 6);
    }

    [Fact]
    public void Type3_widths_use_the_font_matrix()
    {
        var pdf = TextPdf.Page("BT /F1 10 Tf 0 0 Td (AA) Tj ET", ("/F1", d =>
        {
            var font = TextPdf.Font(d, "/Type3", "/Glyphs", new PdfName("/WinAnsiEncoding"));
            font.Elements["/FontMatrix"] = TextPdf.Numbers(d, 0.01, 0, 0, 0.01, 0, 0);
            font.Elements.SetInteger("/FirstChar", 65);
            font.Elements["/Widths"] = TextPdf.Numbers(d, 50);
            return font;
        }));
        // 50 glyph units × 0.01 = 0.5 em per glyph; two glyphs at 10 pt.
        var run = Assert.Single(TextPdf.Runs(pdf));
        Assert.Equal(10, run.EndX - run.X, 6);
    }

    [Fact]
    public void Composite_widths_from_W_ranges_and_DW()
    {
        var cmap = "1 beginbfrange <0001> <0005> <0041> endbfrange";
        var pdf = TextPdf.Page("BT /F1 10 Tf 0 0 Td <00010002000300040005> Tj ET", TextPdf.Composite(cmap, descendant: (d, cidFont) =>
        {
            cidFont.Elements.SetInteger("/DW", 400);
            cidFont.Elements["/W"] = TextPdf.Items(d, new PdfInteger(1), TextPdf.Numbers(d, 500, 600), new PdfInteger(3), new PdfInteger(4), new PdfInteger(700));
        }));
        var run = Assert.Single(TextPdf.Runs(pdf));
        Assert.Equal("ABCDE", run.Text);
        Assert.Equal((500 + 600 + 700 + 700 + 400) / 100.0, run.EndX - run.X, 6);
    }
}
