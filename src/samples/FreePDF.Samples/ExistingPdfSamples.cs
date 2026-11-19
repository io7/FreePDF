// Examples for docs/guide/existing-pdfs.md. MIT; see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.Samples;

public sealed class ExistingPdfSamples : SampleTest
{
    [Fact]
    public void StampPages()
    {
        CreateReport("report.pdf", 2);

        // begin-snippet: stamp-pages
        using (var document = PdfReader.Open("report.pdf", PdfDocumentOpenMode.Modify))
        {
            var font = new XFont("Arial", 60, XFontStyleEx.Bold);
            var translucentRed = new XSolidBrush(XColor.FromArgb(60, 255, 0, 0));

            foreach (var page in document.Pages)
            {
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var centre = new XPoint(page.Width.Point / 2, page.Height.Point / 2);
                gfx.RotateAtTransform(-45, centre);
                gfx.DrawString("COPY", font, translucentRed, centre, XStringFormats.Center);
            }

            document.Save("report-copy.pdf");
        }
        // end-snippet

        for (int i = 0; i < 2; i++)
        {
            var text = Text("report-copy.pdf", i);
            Assert.Contains($"Report page {i + 1}", text);
            Assert.Contains("COPY", text);
        }
    }

    [Fact]
    public void CombineFiles()
    {
        CreateReport("a.pdf", 2, bookmarkOnLastPage: "Summary of a");
        CreateReport("b.pdf", 3);

        // begin-snippet: import-pages
        using var combined = new PdfDocument();
        foreach (var path in new[] { "a.pdf", "b.pdf" })
        {
            using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            combined.AppendPages(source);   // all pages, with their form fields, links and bookmarks
        }

        using (var source = PdfReader.Open("b.pdf", PdfDocumentOpenMode.Import))
            combined.AddPage(source.Pages[0]);   // one page

        combined.Save("combined.pdf");
        // end-snippet

        using var saved = PdfReader.Open("combined.pdf", PdfDocumentOpenMode.Import);
        Assert.Equal(6, saved.PageCount);
        Assert.Contains("Report page 2", Text("combined.pdf", 1));
        Assert.Contains("Report page 3", Text("combined.pdf", 4));
        Assert.Contains("Report page 1", Text("combined.pdf", 5));
        // AppendPages kept a.pdf's bookmark, and it still leads to a.pdf's second page (AddPage alone drops it).
        var bookmark = Assert.Single(Enumerable.Range(0, saved.Outlines.Count).Select(i => saved.Outlines[i]));
        Assert.Equal("Summary of a", bookmark.Title);
        Assert.Equal(1, DestinationPageIndex(saved, bookmark));
    }

    static void CreateReport(string path, int pages, string? bookmarkOnLastPage = null)
    {
        using var document = new PdfDocument();
        for (int i = 1; i <= pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Report page {i}", new XFont("Arial", 14), XBrushes.Black, 50, 50);
        }
        if (bookmarkOnLastPage != null)
            document.Outlines.Add(bookmarkOnLastPage, document.Pages[pages - 1]);
        document.Save(path);
    }
}
