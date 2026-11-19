// Original text extraction tests: Form XObjects, resources and damaged content. MIT; see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    // A Form XObject /Fm{n} on the page with the given content and optional own font resources.
    static PdfDictionary AddForm(PdfDocument d, int n, string content, bool ownFont, double[]? matrix = null)
    {
        var form = new PdfDictionary(d);
        form.Elements.SetName("/Type", "/XObject");
        form.Elements.SetName("/Subtype", "/Form");
        form.Elements["/BBox"] = TextPdf.Numbers(d, 0, 0, 595, 842);
        if (matrix != null)
            form.Elements["/Matrix"] = TextPdf.Numbers(d, matrix);
        if (ownFont)
        {
            var resources = new PdfDictionary(d);
            var fonts = new PdfDictionary(d);
            fonts.Elements["/F1"] = TextPdf.Indirect(d, TextPdf.Font(d, "/Type1", "/Courier", new PdfName("/WinAnsiEncoding")));
            resources.Elements["/Font"] = fonts;
            form.Elements["/Resources"] = resources;
        }
        form.CreateStream(TextPdf.Bytes(content));
        TextPdf.Indirect(d, form);
        var page = d.Pages[0];
        var xobjects = page.Resources.Elements.GetDictionary("/XObject") ?? new PdfDictionary(d);
        xobjects.Elements["/Fm" + n] = form.Reference!;
        page.Resources.Elements["/XObject"] = xobjects;
        return form;
    }

    static byte[] WithForms(string pageContent, Action<PdfDocument> forms)
    {
        var document = TextPdf.Document(pageContent, WinAnsiHelvetica());
        forms(document);
        return TextPdf.Save(document);
    }

    [Fact]
    public void Form_text_is_read_with_its_matrix_and_own_resources()
    {
        var pdf = WithForms("q 1 0 0 1 0 -100 cm /Fm1 Do Q", d => AddForm(d, 1, "BT /F1 10 Tf 72 700 Td (form) Tj ET", true, new double[] { 1, 0, 0, 1, 100, 0 }));
        var run = Assert.Single(TextPdf.Runs(pdf));
        Assert.Equal("form", run.Text);
        Assert.Equal("Courier", run.FontName);
        Assert.Equal(172, run.X, 6);
        Assert.Equal(600, run.Y, 6);
    }

    [Fact]
    public void Form_without_resources_uses_the_callers()
        => Assert.Equal("inner", TextPdf.Text(WithForms("/Fm1 Do", d => AddForm(d, 1, "BT /F1 10 Tf 72 700 Td (inner) Tj ET", false))));

    [Fact]
    public void Form_that_draws_itself_is_read_once()
        => Assert.Equal("loop", TextPdf.Text(WithForms("/Fm1 Do", d => AddForm(d, 1, "BT /F1 10 Tf 72 700 Td (loop) Tj ET /Fm1 Do", false))));

    [Fact]
    public void Forms_nested_beyond_MaxFormDepth_are_refused()
    {
        var pdf = WithForms("/Fm1 Do", d =>
        {
            AddForm(d, 1, "/Fm2 Do", false);
            AddForm(d, 2, "/Fm3 Do", false);
            AddForm(d, 3, "BT /F1 10 Tf 72 700 Td (deep) Tj ET", false);
        });
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal("deep", PdfTextExtractor.GetText(document.Pages[0]));
        var refused = Assert.Throws<PdfReaderException>(() => PdfTextExtractor.GetText(document.Pages[0], options: new PdfTextExtractionOptions { MaxFormDepth = 2 }));
        Assert.Contains("MaxFormDepth", refused.Message);
    }

    [Fact]
    public void Resources_inherited_from_the_page_tree_are_used()
    {
        var document = TextPdf.Document(Show("tree"), WinAnsiHelvetica());
        var page = document.Pages[0];
        var resources = page.Elements["/Resources"];
        page.Elements.Remove("/Resources");
        document.Pages.Elements["/Resources"] = resources;
        page.Elements["/Parent"] = document.Pages.Reference!;
        Assert.Equal("tree", PdfTextExtractor.GetText(page));
    }

    [Fact]
    public void Content_split_over_several_streams_is_read_as_one()
    {
        var document = TextPdf.Document("BT /F1 12 Tf 72 700 Td", WinAnsiHelvetica());
        document.Pages[0].Contents.AppendContent().CreateStream(TextPdf.Bytes("(joined) Tj ET"));
        Assert.Equal("joined", TextPdf.Text(TextPdf.Save(document)));
    }

    [Fact]
    public void Unknown_operators_are_skipped_and_a_syntax_error_keeps_earlier_text()
    {
        Assert.Equal("ok", TextPdf.Text(TextPdf.Page("BX 1 2 vendorop EX BT /F1 12 Tf 72 700 Td (ok) Tj ET", WinAnsiHelvetica())));
        Assert.Equal("kept", TextPdf.Text(TextPdf.Page("BT /F1 12 Tf 72 700 Td (kept) Tj <41", WinAnsiHelvetica())));
    }

    [Fact]
    public void Q_restores_the_font_and_the_CTM()
    {
        // The 9 pt font and the identity CTM from before q come back; the text after Q sets neither.
        var run = Assert.Single(TextPdf.Runs(TextPdf.Page("BT /F1 9 Tf ET q 2 0 0 2 0 0 cm BT /F1 12 Tf ET Q BT 72 700 Td (x) Tj ET", WinAnsiHelvetica())));
        Assert.Equal(72, run.X, 6);
        Assert.Equal(9, run.FontSize, 6);
    }
}
