// Examples for docs/guide/images.md. The test image comes from the original PNG generator in lab/Shared. MIT;
// see LICENSE.
using FreePDF.Lab.Fixtures;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.Samples;

public sealed class ImageSamples : SampleTest
{
    [Fact]
    public void FitImage()
    {
        File.WriteAllBytes("logo.png", PalettePng.Create(8, 40, 20, false));   // 40 × 20 pixels

        // begin-snippet: image-fit
        using var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        using (var image = XImage.FromFile("logo.png"))
        {
            // 150 points wide; the height keeps the image's proportions.
            double width = 150;
            double height = width * image.PixelHeight / image.PixelWidth;
            gfx.DrawImage(image, 50, 50, width, height);
        }

        document.Save("image.pdf");
        // end-snippet

        using var saved = PdfReader.Open("image.pdf", PdfDocumentOpenMode.Import);
        var xobjects = saved.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        var stored = xobjects.Elements.GetDictionary(xobjects.Elements.Keys.Single())!;
        Assert.Equal("/Image", stored.Elements.GetName("/Subtype"));
        Assert.Equal(40, stored.Elements.GetInteger("/Width"));
        Assert.Equal(20, stored.Elements.GetInteger("/Height"));
        // Drawn 150 × 75 points: the image matrix before "Do" scales by 150 and 75 (y may be mirrored).
        Assert.Matches(@"(?<![\d.-])150 0 0 -?75 -?[\d.]+ -?[\d.]+ cm\s*/\w+ Do", Content("image.pdf"));
    }
}
