// Stream /Length entries that are not a non-negative integer. A missing /Length is already recovered by searching
// for 'endstream'; an indirect /Length pointing at a dictionary threw InvalidCastException instead. Found by mutation
// fuzzing. Original fixtures; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class StreamLengthTests
{
    [Theory]
    [InlineData("/Length 5 0 R")]
    [InlineData("/Length -5")]
    [InlineData("/Length (five)")]
    public void A_length_that_is_not_a_byte_count_is_recovered_from_endstream(string length)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>",
            $"<< {length} >>\nstream\n0 0 m 240 200 l S\nendstream",
            "<< /Not /AnInteger >>" });

        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
            document.Save(saved);

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Modify);
        var content = reopened.Pages[0].Elements.GetDictionary("/Contents")!.Stream!.Value;
        Assert.StartsWith("0 0 m 240 200 l S", Encoding.ASCII.GetString(content));
    }
}
