// Object syntax that cannot be parsed: a stream keyword inside an array or dictionary, a number with two decimal
// points. The reader threw NotImplementedException or ContentReaderException instead of PdfReaderException.
// Found by mutation fuzzing. Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class MalformedObjectTests
{
    [Theory]
    [InlineData("/Junk [1 stream 2]")]
    [InlineData("/Junk << /Inner stream >>")]
    [InlineData("/Junk 1.2.3")]
    public void Unparsable_object_syntax_is_a_reader_error(string entry)
    {
        var bytes = TestPdf.Create(new[] {
            $"<< /Type /Catalog /Pages 2 0 R {entry} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" });

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify));
    }
}
