// Original synthetic PDF fixtures. MIT; see LICENSE.
using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FreePDF.RegressionTests;

static class TestPdf
{
    // ISO-8859-1 maps every byte to one char and back; Encoding.Latin1 does not exist on .NET Framework.
    public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    public static PdfDocument OpenCatalog(string entries)
    {
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R " + entries + " >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" };
        return PdfReader.Open(new MemoryStream(Create(objects)), PdfDocumentOpenMode.Modify);
    }

    public static byte[] Create(string[] objects, Func<int, string>? trailerEntries = null)
        => FreePDF.Lab.Fixtures.PdfFixtureData.Create(objects, trailerEntries);
}
