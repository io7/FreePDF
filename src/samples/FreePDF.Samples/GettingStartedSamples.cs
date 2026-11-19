// Examples for docs/guide/getting-started.md. MIT; see LICENSE.
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.Samples;

public sealed class GettingStartedSamples : SampleTest
{
    [Fact]
    public void HelloWorld()
    {
        // begin-snippet: hello-world
        using var document = new PdfDocument();
        document.Info.Title = "Hello";
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 20);
            var wholePage = new XRect(0, 0, page.Width.Point, page.Height.Point);
            gfx.DrawString("Hello, FreePDF!", font, XBrushes.Black, wholePage, XStringFormats.Center);
        }

        document.Save("hello.pdf");
        // end-snippet

        Assert.Equal("Hello, FreePDF!", Text("hello.pdf").Trim());
    }

    [Fact]
    public void SaveToStream()
    {
        // begin-snippet: save-to-stream
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("Invoice 2026-118", new XFont("Arial", 14), XBrushes.Black, 50, 50);

        using var stream = new MemoryStream();
        document.Save(stream);          // the stream stays open
        byte[] pdf = stream.ToArray();  // in ASP.NET Core: return File(pdf, "application/pdf", "invoice.pdf");
        // end-snippet

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        using var reopened = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal("Invoice 2026-118", PdfTextExtractor.GetText(reopened.Pages[0]).Trim());
    }
}
