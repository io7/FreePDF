// References to missing objects inside direct arrays and dictionaries, such as a page's /Annots array. ISO 32000-1
// 7.3.10 makes them references to null. The reader resolved them only one level deep, so a nested one stayed a
// reference without a value and reading it threw InvalidOperationException. Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class NestedDanglingReferenceTests
{
    [Theory]
    [InlineData("/Annots [99 0 R]")]
    [InlineData("/Resources << /XObject << /X1 99 0 R >> >>")]
    [InlineData("/Group << /CS [/ICCBased 99 0 R] /S /Transparency >>")]
    public void A_nested_reference_to_a_missing_object_reads_as_null(string pageEntry)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] {pageEntry} >>" });

        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
        {
            Assert.Empty(document.Pages[0].Annotations);
            document.Save(saved);
        }

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.PageCount);
    }
}
