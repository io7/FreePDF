// Damaged files where an entry names an object that already plays another role: /Outlines or /Info pointing at the
// catalog, /Pages pointing at a name. A typed read converted the object, which destroyed it in its first role, so
// opening or saving failed with "This dictionary cannot be used anymore". Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class TypeConfusionTests
{
    static PdfDocument SaveAndReopen(PdfDocument document)
    {
        using var saved = new MemoryStream();
        document.Save(saved);
        return PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
    }

    [Fact]
    public void An_outline_entry_naming_the_catalog_is_treated_as_absent()
    {
        using var document = TestPdf.OpenCatalog("/Outlines 1 0 R");

        Assert.Empty(document.Outlines);
        document.Outlines.Add("Added", document.Pages[0]);

        using var reopened = SaveAndReopen(document);
        Assert.Equal(240, reopened.Pages[0].Width.Point);
        Assert.Equal("Added", Assert.Single(reopened.Outlines).Title);
    }

    [Fact]
    public void An_information_entry_naming_the_catalog_is_treated_as_absent()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" }, _ => "/Info 1 0 R");
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        document.Info.Title = "Set after opening";

        using var reopened = SaveAndReopen(document);
        Assert.Equal(240, reopened.Pages[0].Width.Point);
        Assert.Equal("Set after opening", reopened.Info.Title);
    }

    [Theory]
    [InlineData("[3 0 R 1 0 R]")]
    [InlineData("[3 0 R 2 0 R]")]
    public void A_page_tree_kid_naming_the_catalog_or_the_tree_itself_does_not_destroy_it(string kids)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids {kids} /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" });

        try
        {
            using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
            using var reopened = SaveAndReopen(document);
            Assert.Equal(240, reopened.Pages[0].Width.Point);
        }
        catch (PdfReaderException)
        {
            // Refusing the file is acceptable; any other exception is not.
        }
    }

    [Fact]
    public void A_page_tree_entry_naming_a_name_object_is_a_reader_error()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "/NotAPageTree",
            "<< /Type /Page /MediaBox [0 0 240 200] >>" });

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify));
    }
}
