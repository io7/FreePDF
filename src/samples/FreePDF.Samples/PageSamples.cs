// Examples for docs/guide/pages.md. MIT; see LICENSE.
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.Samples;

public sealed class PageSamples : SampleTest
{
    [Fact]
    public void PageSizes()
    {
        // begin-snippet: page-sizes
        using var document = new PdfDocument();

        var a4 = document.AddPage();
        a4.Size = PageSize.A4;                        // portrait
        a4.Orientation = PageOrientation.Landscape;   // swaps width and height

        var letter = document.AddPage();
        letter.Size = PageSize.Letter;

        var label = document.AddPage();               // any size, here 100 × 50 mm
        label.Width = XUnit.FromMillimeter(100);
        label.Height = XUnit.FromMillimeter(50);

        document.Save("sizes.pdf");
        // end-snippet

        Assert.Equal((842.0, 595.0), MediaSize("sizes.pdf", 0));
        Assert.Equal((612.0, 792.0), MediaSize("sizes.pdf", 1));
        var labelSize = MediaSize("sizes.pdf", 2);
        AssertNear(283.46, labelSize.Width, 0.01);
        AssertNear(141.73, labelSize.Height, 0.01);
    }

    [Fact]
    public void Coordinates()
    {
        // begin-snippet: coordinates
        using var document = new PdfDocument();
        var page = document.AddPage();
        double margin = XUnit.FromCentimeter(2).Point;   // 56.69 points

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            // (0, 0) is the top left corner; x grows to the right, y grows downwards.
            // DrawString's point is the left end of the baseline, the line the letters sit on.
            gfx.DrawString("2 cm from the left, 2 cm from the top", font, XBrushes.Black, margin, margin);
            gfx.DrawRectangle(XPens.Gray, margin, margin, page.Width.Point - 2 * margin, page.Height.Point - 2 * margin);
        }

        document.Save("coordinates.pdf");
        // end-snippet

        var run = Assert.Single(Runs("coordinates.pdf"));
        AssertNear(56.69, run.X);
        // Text extraction reports PDF coordinates: y counts from the bottom of the page.
        AssertNear(MediaSize("coordinates.pdf").Height - 56.69, run.Y);
    }
}
