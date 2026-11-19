// Original text extraction tests for the review findings of 2026-11-19. MIT; see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    [Fact]
    public void A_font_set_through_an_ExtGState_shows_text()
    {
        var document = TextPdf.Document("/G1 gs BT 72 700 Td (Hello) Tj ET");
        var font = TextPdf.Font(document, "/Type1", "/Helvetica", new PdfName("/WinAnsiEncoding"));
        var state = new PdfDictionary(document);
        state.Elements["/Font"] = TextPdf.Items(document, TextPdf.Indirect(document, font), new PdfInteger(12));
        var states = new PdfDictionary(document);
        states.Elements["/G1"] = state;
        document.Pages[0].Resources.Elements["/ExtGState"] = states;
        var runs = TextPdf.Runs(TextPdf.Save(document));
        Assert.Equal("Hello", Assert.Single(runs).Text);
        Assert.Equal(12, runs[0].FontSize, 6);
    }

    [Fact]
    public void Standard_font_widths_follow_the_Differences_glyph()
    {
        // Code 65 draws "i" (222/1000 em in Helvetica), not "A" (667/1000 em).
        var runs = TextPdf.Runs(TextPdf.Page("BT /F1 10 Tf 0 0 Td (A) Tj (B) Tj ET", TextPdf.Standard("/Helvetica", d =>
        {
            var encoding = new PdfDictionary(d);
            encoding.Elements.SetName("/BaseEncoding", "/WinAnsiEncoding");
            encoding.Elements["/Differences"] = TextPdf.Items(d, new PdfInteger(65), new PdfName("/i"));
            return encoding;
        })));
        Assert.Equal("i", runs[0].Text);
        Assert.Equal(2.22, runs[1].X, 6);
    }

    [Fact]
    public void A_leading_TJ_adjustment_moves_the_run_start()
    {
        var pdf = TextPdf.Page("BT /F1 10 Tf [-2000 (B)] TJ 1 0 0 1 10 0 Tm (A) Tj ET", TextPdf.Standard("/Courier"));
        Assert.Equal(20, TextPdf.Runs(pdf)[0].X, 6);
        Assert.Equal("A B", TextPdf.Text(pdf));
    }

    [Fact]
    public void An_embedded_ToUnicode_CMap_inherits_its_UseCMap()
    {
        var pdf = TextPdf.Page("BT /F1 10 Tf 0 0 Td <00010002> Tj ET", ("/F1", d =>
        {
            var font = TextPdf.Composite("").Item2(d);
            font.Elements["/ToUnicode"] = Derived(d, "1 beginbfchar <0002> <0042> endbfchar", "1 beginbfchar <0001> <0041> endbfchar");
            return font;
        }));
        Assert.Equal("AB", TextPdf.Text(pdf));
    }

    [Fact]
    public void An_embedded_encoding_CMap_inherits_its_UseCMap()
    {
        // Code 1 maps to CID 1 in the base CMap, code 2 to CID 7 in the derived one: widths 0.1 + 0.9 em.
        var pdf = TextPdf.Page("BT /F1 10 Tf 0 0 Td <00010002> Tj ET", TextPdf.Composite("1 beginbfrange <0001> <0002> <0041> endbfrange",
            d => Derived(d, "1 begincidchar <0002> 7 endcidchar", "1 begincidrange <0000> <00FF> 0 endcidrange"),
            (d, cidFont) => cidFont.Elements["/W"] = TextPdf.Items(d, new PdfInteger(1), TextPdf.Numbers(d, 100), new PdfInteger(7), TextPdf.Numbers(d, 900))));
        var run = Assert.Single(TextPdf.Runs(pdf));
        Assert.Equal("AB", run.Text);
        Assert.Equal(10, run.EndX, 6);
    }

    // A CMap stream whose /UseCMap is a second stream; only the base has a code space.
    static PdfReference Derived(PdfDocument d, string derivedBody, string baseBody)
    {
        var derived = new PdfDictionary(d);
        derived.CreateStream(TextPdf.Bytes(TextPdf.CMap(derivedBody, "")));
        derived.Elements["/UseCMap"] = TextPdf.Stream(d, TextPdf.CMap(baseBody));
        return TextPdf.Indirect(d, derived);
    }

    [Fact]
    public void A_content_stream_that_cannot_be_decoded_is_skipped()
    {
        var document = TextPdf.Document(Show("Hello"), WinAnsiHelvetica());
        var broken = document.Pages[0].Contents.AppendContent();
        broken.CreateStream(TextPdf.Bytes("~x"));
        broken.Elements.SetName("/Filter", "/ASCII85Decode");
        Assert.Equal("Hello", TextPdf.Text(TextPdf.Save(document)));
    }

    [Fact]
    public void A_number_out_of_range_ends_the_stream()
        => Assert.Equal("Hello", TextPdf.Text(TextPdf.Page(
            "BT /F1 12 Tf 72 700 Td (Hello) Tj 1" + new string('0', 400) + " 0 Td (World) Tj ET", WinAnsiHelvetica())));

    [Fact]
    public void WinAnsi_duplicate_codes_are_space_and_hyphen()
        => Assert.Equal("a b-c", TextPdf.Text(TextPdf.Page(Show("a b­c"), WinAnsiHelvetica())));

    [Fact]
    public void A_backslash_before_8_or_9_is_dropped()
        => Assert.Equal("a8b9", TextPdf.Text(TextPdf.Page(Show("a\\8b\\9"), WinAnsiHelvetica())));

    [Fact]
    public void Unknown_operators_with_any_regular_characters_are_skipped()
        => Assert.Equal("kept", TextPdf.Text(TextPdf.Page("BX 1 vendor_op 2 @tag x.y EX BT /F1 12 Tf 72 700 Td (kept) Tj ET", WinAnsiHelvetica())));

    [Fact]
    public void Filling_closes_open_subpaths_with_an_unstroked_edge()
    {
        var filled = Segments("0 0 m 10 0 l 10 10 l 20 0 m 30 0 l 30 10 l f");
        Assert.Equal(6, filled.Count);
        Assert.Contains(filled, s => (s.X1, s.Y1, s.X2, s.Y2) == (10.0, 10.0, 0.0, 0.0));
        Assert.Contains(filled, s => (s.X1, s.Y1, s.X2, s.Y2) == (30.0, 10.0, 20.0, 0.0));

        var both = Segments("0 0 m 10 0 l 10 10 l B");
        Assert.Equal(3, both.Count);
        Assert.Equal(2, both.Count(s => s.Stroked));
        Assert.False(both.Single(s => (s.X1, s.Y1) == (10.0, 10.0)).Stroked);

        Assert.Equal(2, Segments("0 0 m 10 0 l 10 10 l S").Count);
    }
}
