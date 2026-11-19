// Original text extraction tests: simple fonts and their encodings. MIT; see LICENSE.
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    static string Show(string text) => "BT /F1 12 Tf 72 700 Td (" + text + ") Tj ET";

    [Fact]
    public void WinAnsiEncoding_decodes_Latin_1_letters_and_the_euro()
        => Assert.Equal("Café 3 €", TextPdf.Text(TextPdf.Page(Show("Café 3 \u0080"),
            TextPdf.Standard("/Helvetica", _ => new PdfName("/WinAnsiEncoding")))));

    [Fact]
    public void Standard_font_without_Encoding_uses_StandardEncoding()
        => Assert.Equal("It’s", TextPdf.Text(TextPdf.Page(Show("It's"), TextPdf.Standard("/Helvetica"))));

    [Fact]
    public void MacRomanEncoding_decodes_its_codes()
        => Assert.Equal("é¤", TextPdf.Text(TextPdf.Page(Show("\u008EÛ"),
            TextPdf.Standard("/Times-Roman", _ => new PdfName("/MacRomanEncoding")))));

    [Fact]
    public void Differences_map_glyph_names_with_the_Adobe_Glyph_List_rules()
    {
        var pdf = TextPdf.Page(Show("ABCDEF"), TextPdf.Standard("/Helvetica", d =>
        {
            var encoding = new PdfDictionary(d);
            encoding.Elements.SetName("/BaseEncoding", "/WinAnsiEncoding");
            encoding.Elements["/Differences"] = TextPdf.Items(d, new PdfInteger(65), new PdfName("/Euro"), new PdfName("/uni00E6"),
                new PdfName("/f_f_i"), new PdfName("/u1F600"), new PdfName("/a.sc"), new PdfName("/g123"));
            return encoding;
        }));
        Assert.Equal("€æffi\U0001F600a�", TextPdf.Text(pdf));
    }

    [Fact]
    public void Symbol_and_ZapfDingbats_use_their_built_in_encodings()
    {
        Assert.Equal("α", TextPdf.Text(TextPdf.Page(Show("a"), TextPdf.Standard("/Symbol"))));
        Assert.Equal("✁", TextPdf.Text(TextPdf.Page(Show("!"), TextPdf.Standard("/ZapfDingbats"))));
    }

    [Fact]
    public void Subset_prefix_is_removed_from_the_font_name()
    {
        var run = Assert.Single(TextPdf.Runs(TextPdf.Page(Show("x"), TextPdf.Standard("/ABCDEF+Helvetica"))));
        Assert.Equal("Helvetica", run.FontName);
    }

    [Fact]
    public void Text_shown_before_a_font_is_set_is_skipped()
        => Assert.Equal("y", TextPdf.Text(TextPdf.Page("BT 72 700 Td (x) Tj /F1 12 Tf (y) Tj ET", TextPdf.Standard("/Helvetica"))));
}
