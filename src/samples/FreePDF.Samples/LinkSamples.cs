// Examples for docs/guide/links-and-bookmarks.md. MIT; see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.Samples;

public sealed class LinkSamples : SampleTest
{
    [Fact]
    public void LinksAndBookmarks()
    {
        // begin-snippet: links
        using var document = new PdfDocument();
        var first = document.AddPage();
        var second = document.AddPage();
        var font = new XFont("Arial", 12);

        using (var gfx = XGraphics.FromPdfPage(first))
        {
            var web = new XRect(50, 50, 200, 16);
            gfx.DrawString("example.com", font, XBrushes.Blue, web, XStringFormats.CenterLeft);
            // Link rectangles use PDF page coordinates (origin bottom left); the transformer converts.
            first.AddWebLink(new PdfRectangle(gfx.Transformer.WorldToDefaultPage(web)), "https://example.com/");

            var next = new XRect(50, 80, 200, 16);
            gfx.DrawString("Go to page 2", font, XBrushes.Blue, next, XStringFormats.CenterLeft);
            first.AddDocumentLink(new PdfRectangle(gfx.Transformer.WorldToDefaultPage(next)), 2);   // pages count from 1
        }
        // end-snippet

        // begin-snippet: bookmarks
        var chapter = document.Outlines.Add("Chapter 1", first);
        chapter.Outlines.Add("Section 1.1", second);
        document.Save("links.pdf");
        // end-snippet

        using var saved = PdfReader.Open("links.pdf", PdfDocumentOpenMode.Import);
        var annots = saved.Pages[0].Elements.GetArray("/Annots")!;
        var links = Enumerable.Range(0, annots.Elements.Count).Select(i => annots.Elements.GetDictionary(i)!).ToList();
        Assert.Equal(2, links.Count);
        var webLink = links.Single(link => link.Elements.GetDictionary("/A")?.Elements.GetName("/S") == "/URI");
        Assert.Equal("https://example.com/", webLink.Elements.GetDictionary("/A")!.Elements.GetString("/URI"));
        AssertNear(MediaSize("links.pdf").Height - 50, webLink.Elements.GetRectangle("/Rect")!.Y2);
        Assert.Equal(1, saved.Outlines.Count);
        Assert.Equal("Chapter 1", saved.Outlines[0].Title);
        Assert.Equal("Section 1.1", saved.Outlines[0].Outlines[0].Title);
    }
}
