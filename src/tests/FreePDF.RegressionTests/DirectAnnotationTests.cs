// Annotations stored directly in a page's /Annots array instead of by reference. Saving such a page failed with
// "This dictionary cannot be used anymore": the typed annotation replaced the dictionary, but the array kept the
// dead one. Found by the corpus round trip (veraPDF corpus). Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DirectAnnotationTests
{
    static byte[] PageWithDirectAnnotation(string subtype) => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Annots [ << /Type /Annot /Subtype /{subtype} /Rect [10 10 50 50] /F 4 /Contents (direct) >> ] >>" });

    [Theory]
    [InlineData("Circle")]
    [InlineData("Square")]
    [InlineData("Text")]
    [InlineData("Link")]
    public void A_page_with_a_direct_annotation_saves_and_keeps_it(string subtype)
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(PageWithDirectAnnotation(subtype)), PdfDocumentOpenMode.Modify))
            document.Save(saved);

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.Pages[0].Annotations.Count);
        var annotation = reopened.Pages[0].Annotations[0];
        Assert.Equal("/" + subtype, annotation.Elements.GetName(PdfAnnotation.Keys.Subtype));
        Assert.Equal("direct", annotation.Elements.GetString(PdfAnnotation.Keys.Contents));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("(text)")]
    public void An_annots_entry_that_is_not_a_dictionary_does_not_stop_the_document(string entry)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Annots [ {entry} << /Type /Annot /Subtype /Square /Rect [10 10 50 50] >> ] >>" });

        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
            document.Save(saved);
        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.PageCount);
    }

    [Fact]
    public void Reading_a_direct_annotation_before_saving_also_works()
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(PageWithDirectAnnotation("Circle")), PdfDocumentOpenMode.Modify))
        {
            Assert.IsType<PdfCircleAnnotation>(document.Pages[0].Annotations[0]);
            document.Save(saved);
        }

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.Pages[0].Annotations.Count);
    }
}
