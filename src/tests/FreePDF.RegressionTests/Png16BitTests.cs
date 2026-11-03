// PNG images with 16 bits per sample. The importer refused them ("Unsupported bit depth 16"), although its decoder
// reads 16-bit samples; they are now embedded with 8 bits per component, which every PDF/A part allows.
// Original fixtures (2 x 2 pixels, ImageMagick); MIT, see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class Png16BitTests
{
    [Theory]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACEAAAAAAHTY67AAAAEklEQVQI12Osr2dgYGJgYGAAAAiNAQKDKi+zAAAAAElFTkSuQmCC", new byte[] { 127 }, false)] // sample 32639
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACEAIAAACtREYwAAAAGUlEQVQI12N0c2tq2rKFgYGBgYGBiQEJAABBYgL8MNt+FwAAAABJRU5ErkJggg==", new byte[] { 70, 130, 180 }, false)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACEAQAAACILxnsAAAAFElEQVQI12NsYGhgYGBgYGBigAIAEDYBBKF0TEIAAAAASUVORK5CYII=", new byte[] { 128 }, true)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACEAYAAAAiJtFnAAAAFklEQVQI12N0c2tq2rKlgQEGmBjQAABmugN82DxPmAAAAABJRU5ErkJggg==", new byte[] { 70, 130, 180 }, true)]
    public void A_16_bit_png_is_embedded_with_8_bits_per_component(string png, byte[] firstPixel, bool alpha)
    {
        using var saved = new MemoryStream();
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using (var graphics = XGraphics.FromPdfPage(page))
            using (var image = XImage.FromStream(new MemoryStream(Convert.FromBase64String(png))))
            {
                Assert.Equal(2, image.PixelWidth);
                graphics.DrawImage(image, 10, 10, 20, 20);
            }
            document.Save(saved);
        }

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        var xObjects = reopened.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        var embedded = xObjects.Elements.Values
            .Select(v => (v as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfDictionary)
            .Single(d => d?.Elements.GetName("/Subtype") == "/Image")!;
        Assert.Equal(8, embedded.Elements.GetInteger("/BitsPerComponent"));
        Assert.Equal(firstPixel, embedded.Stream!.UnfilteredValue.Take(firstPixel.Length).ToArray());
        Assert.Equal(alpha, embedded.Elements.ContainsKey("/SMask"));
    }
}
