// Examples for docs/guide/multi-page.md. MIT; see LICENSE.
using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.Samples;

public sealed class MultiPageSamples : SampleTest
{
    [Fact]
    public void PriceList()
    {
        var items = Enumerable.Range(1, 120).Select(i => (Name: $"Item {i}", Price: i * 9.95m)).ToList();

        // begin-snippet: table-pages
        using var document = new PdfDocument();
        var font = new XFont("Arial", 10);
        var bold = new XFont("Arial", 10, XFontStyleEx.Bold);
        const double left = 50, right = 350, top = 60, bottom = 60, rowHeight = 16;

        XGraphics StartPage()
        {
            var page = document.AddPage();
            var g = XGraphics.FromPdfPage(page);
            g.DrawString("Item", bold, XBrushes.Black, left, top);
            g.DrawString("Price", bold, XBrushes.Black, right, top, XStringFormats.BaseLineRight);
            g.DrawLine(XPens.Black, left, top + 4, right, top + 4);
            return g;
        }

        var gfx = StartPage();
        double y = top + rowHeight;
        foreach (var (name, price) in items)
        {
            if (y > gfx.PageSize.Height - bottom)   // no room for another row
            {
                gfx.Dispose();
                gfx = StartPage();
                y = top + rowHeight;
            }
            gfx.DrawString(name, font, XBrushes.Black, left, y);
            gfx.DrawString(price.ToString("N2", CultureInfo.InvariantCulture), font, XBrushes.Black,
                right, y, XStringFormats.BaseLineRight);
            y += rowHeight;
        }
        gfx.Dispose();
        // end-snippet

        // begin-snippet: page-numbers
        int count = document.PageCount;
        for (int i = 0; i < count; i++)
        {
            var page = document.Pages[i];
            using var footer = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var line = new XRect(0, page.Height.Point - 40, page.Width.Point, 20);
            footer.DrawString($"Page {i + 1} of {count}", font, XBrushes.Gray, line, XStringFormats.Center);
        }
        document.Save("price-list.pdf");
        // end-snippet

        using var saved = PdfReader.Open("price-list.pdf", PdfDocumentOpenMode.Import);
        Assert.InRange(saved.PageCount, 3, 4);
        for (int i = 0; i < saved.PageCount; i++)
        {
            var text = PdfTextExtractor.GetText(saved.Pages[i]);
            Assert.StartsWith("Item Price", text);
            Assert.Contains($"Page {i + 1} of {saved.PageCount}", text);
        }
        Assert.Contains("Item 120 1,194.00", PdfTextExtractor.GetText(saved.Pages[saved.PageCount - 1]));
    }
}
