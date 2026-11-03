// Trailer entries that reference objects missing from the file. ISO 32000-1 7.3.10 treats a reference to an
// undefined object as null; the reader threw ArgumentNullException instead. Found by the corpus round trip.
// Original fixtures; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DanglingTrailerReferenceTests
{
    static readonly string[] SimplePage = {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" };

    [Fact]
    public void A_missing_document_information_dictionary_is_treated_as_absent()
    {
        var bytes = TestPdf.Create(SimplePage, _ => "/Info 99 0 R");

        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
        {
            Assert.Equal(1, document.PageCount);
            document.Info.Title = "Set after opening";
            document.Save(saved);
        }
        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal("Set after opening", reopened.Info.Title);
    }

    [Theory]
    [InlineData(PdfDocumentOpenMode.Import)]
    [InlineData(PdfDocumentOpenMode.Modify)]
    public void A_missing_catalog_is_a_reader_error(PdfDocumentOpenMode mode)
    {
        var text = Encoding.ASCII.GetString(TestPdf.Create(SimplePage)).Replace("/Root 1 0 R", "/Root 99 0 R");

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(Encoding.ASCII.GetBytes(text)), mode));
    }

    [Fact]
    public void A_missing_encryption_dictionary_is_a_reader_error()
    {
        var bytes = TestPdf.Create(SimplePage, _ => "/Encrypt 99 0 R");

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import));
    }
}
