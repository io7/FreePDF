// File header versions. ISO 19005-4 (PDF/A-4) allows any "%PDF-2.n" header; FreePDF rejected everything above
// 2.0. Found by the corpus round trip. Original fixtures; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class FileHeaderTests
{
    // The fixture builder writes "%PDF-1.7"; replace those eight bytes to test other headers.
    static byte[] WithHeader(string header)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" });
        Assert.Equal("%PDF-1.7", Encoding.ASCII.GetString(bytes, 0, 8));
        Encoding.ASCII.GetBytes(header).CopyTo(bytes, 0);
        return bytes;
    }

    [Theory]
    [InlineData("%PDF-2.0", 20)]
    [InlineData("%PDF-2.1", 20)]
    [InlineData("%PDF-2.9", 20)]
    [InlineData("%PDF-1.9", 19)]
    public void Any_minor_version_of_PDF_1_and_2_opens(string header, int version)
    {
        using var document = PdfReader.Open(new MemoryStream(WithHeader(header)), PdfDocumentOpenMode.Modify);

        Assert.Equal(1, document.PageCount);
        Assert.Equal(version, document.Version);
    }

    [Fact]
    public void A_future_PDF_2_minor_version_is_saved_as_PDF_2_0()
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(WithHeader("%PDF-2.9")), PdfDocumentOpenMode.Modify))
            document.Save(saved);

        Assert.Equal("%PDF-2.0", Encoding.ASCII.GetString(saved.ToArray(), 0, 8));
    }

    [Theory]
    [InlineData("%PDF-a.4")]
    [InlineData("%PDF-3.0")]
    public void An_unreadable_header_is_reported_as_a_reader_error(string header)
    {
        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(WithHeader(header)), PdfDocumentOpenMode.Import));
    }
}
