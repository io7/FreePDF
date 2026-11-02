// Streams whose data is in an external file (/F in the stream dictionary, ISO 32000-1 7.3.8.2). The reader threw
// NotImplementedException for the whole document (upstream issue 389; ten veraPDF corpus files). Such streams are now
// parsed by /Length like any other; FreePDF never loads the external file, so an untrusted PDF cannot make it read
// local files. Original fixtures; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class FileStreamTests
{
    static byte[] PageWithExternalStream(string externalFile, string embedded) => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Resources << /XObject << /X 5 0 R >> >> /Contents 4 0 R >>",
        "<< /Length 0 >>\nstream\n\nendstream",
        $"<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /F {externalFile} /Length {embedded.Length} >>\nstream\n{embedded}\nendstream" });

    [Theory]
    [InlineData("(missing-external-data.bin)")]
    [InlineData("<< /Type /Filespec /F (missing-external-data.bin) >>")]
    public void A_stream_with_external_file_data_opens_and_saves_without_reading_the_file(string externalFile)
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(PageWithExternalStream(externalFile, "q Q")), PdfDocumentOpenMode.Modify))
        {
            var form = document.Pages[0].Resources.Elements.GetDictionary("/XObject")!.Elements.GetDictionary("/X")!;
            Assert.True(form.Elements.ContainsKey("/F"));
            Assert.Equal("q Q", Encoding.ASCII.GetString(form.Stream!.Value));
            document.Save(saved);
        }

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        var kept = reopened.Pages[0].Resources.Elements.GetDictionary("/XObject")!.Elements.GetDictionary("/X")!;
        Assert.True(kept.Elements.ContainsKey("/F"), "The external file reference must survive saving.");
    }
}
