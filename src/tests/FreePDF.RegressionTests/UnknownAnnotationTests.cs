// Annotations of a subtype FreePDF does not model must not stop the document from opening. Found by the corpus
// round trip (veraPDF corpus, including files that pass PDF/UA-1). Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class UnknownAnnotationTests
{
    static byte[] PageWithAnnotation(string annotation) => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Annots [4 0 R] >>",
        annotation });

    [Theory]
    [InlineData("<< /Type /Annot /Subtype /SomePrivateCustomAnnotationType /Rect [10 10 50 50] /Contents (kept) >>", "/SomePrivateCustomAnnotationType")]
    [InlineData("<< /Type /Annot /Subtype /FREETEXT /Rect [10 10 50 50] /Contents (kept) >>", "/FREETEXT")]
    [InlineData("<< /Type /Annot /Rect [10 10 50 50] /Contents (kept) >>", "")]
    public void An_annotation_of_an_unknown_subtype_opens_and_survives_saving(string annotation, string subtype)
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(PageWithAnnotation(annotation)), PdfDocumentOpenMode.Modify))
        {
            Assert.Equal(1, document.Pages[0].Annotations.Count);
            var unknown = Assert.IsType<PdfUnknownAnnotation>(document.Pages[0].Annotations[0]);
            Assert.Equal(subtype, unknown.Subtype);
            document.Save(saved);
        }

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.Pages[0].Annotations.Count);
        var kept = reopened.Pages[0].Annotations[0];
        if (subtype == "") Assert.False(kept.Elements.ContainsKey(PdfAnnotation.Keys.Subtype), "Saving must not add a /Subtype.");
        else Assert.Equal(subtype, kept.Elements.GetName(PdfAnnotation.Keys.Subtype));
        Assert.Equal("kept", kept.Elements.GetString(PdfAnnotation.Keys.Contents));
    }

    [Fact]
    public void An_annotation_whose_subtype_is_a_number_opens_and_survives_saving()
    {
        // Opening threw InvalidOperationException ("expected to be of type PdfName") before. Found while testing
        // AppendPages, which types copied annotations the same way.
        var bytes = PageWithAnnotation("<< /Type /Annot /Subtype 7 /Rect [10 10 50 50] /Contents (kept) >>");

        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
        {
            Assert.Equal(1, document.Pages[0].Annotations.Count);
            var unknown = Assert.IsType<PdfUnknownAnnotation>(document.Pages[0].Annotations[0]);
            Assert.Equal("", unknown.Subtype);
            document.Save(saved);
        }

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.Pages[0].Annotations.Count);
        var kept = reopened.Pages[0].Annotations[0];
        Assert.Equal(7, kept.Elements.GetInteger(PdfAnnotation.Keys.Subtype));
        Assert.Equal("kept", kept.Elements.GetString(PdfAnnotation.Keys.Contents));
    }
}
