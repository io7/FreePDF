// Original text extraction tests: reading order, content order and whole documents. MIT; see LICENSE.
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    [Fact]
    public void Reading_order_sorts_lines_top_down_and_runs_left_to_right()
    {
        var pdf = TextPdf.Page("BT /F1 12 Tf 72 600 Td (second) Tj ET BT /F1 12 Tf 150 700 Td (World) Tj ET BT /F1 12 Tf 72 700 Td (Hello) Tj ET",
            WinAnsiHelvetica());
        Assert.Equal("Hello World\nsecond", TextPdf.Text(pdf));
        Assert.Equal("second\nWorld Hello", TextPdf.Text(pdf, PdfTextOrder.Content));
    }

    [Fact]
    public void Superscript_stays_on_its_line()
        => Assert.Equal("x2 + y", TextPdf.Text(TextPdf.Page("BT /F1 12 Tf 72 700 Td (x) Tj 4 Ts (2) Tj 0 Ts ( + y) Tj ET", WinAnsiHelvetica())));

    [Fact]
    public void MigraDoc_paragraphs_and_pages_come_back_word_for_word()
    {
        TestFonts.Initialize();
        var words = String.Join(" ", Enumerable.Range(1, 120).Select(i => "word" + i));
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph(words).Format.Font.Name = "FreePDF Synthetic";
        section.AddPageBreak();
        section.AddParagraph("Last page").Format.Font.Name = "FreePDF Synthetic";
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var reopened = PdfReader.Open(new MemoryStream(TextPdf.Save(renderer.PdfDocument)), PdfDocumentOpenMode.Import);

        var pages = PdfTextExtractor.GetText(reopened).Split('\f');
        Assert.Equal(3, pages.Length);
        Assert.Equal("", pages[2]);
        Assert.Equal(words, Flat(pages[0]));
        Assert.Equal("Last page", Flat(pages[1]));
    }

    static string Flat(string text) => String.Join(" ", text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries));
}
