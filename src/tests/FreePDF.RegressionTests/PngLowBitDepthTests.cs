// Upstream #66: the Core build refused 1-, 2- and 4-bit grayscale PNGs and 2-bit palette PNGs ("Unsupported bit
// depth"). QR code generators often write 1-bit grayscale. Original fixtures written with Python's zlib by a small
// generator; rows use the None, Sub and Up filters. Pillow decodes them to the values below. MIT, see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class PngLowBitDepthTests
{
    [Theory]
    // 10 x 2, 1 bit: a row crosses a byte boundary.
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAoAAAACAQAAAABJGnB9AAAADklEQVR4nGPY2MDo9wkABwwCc3+MB+UAAAAASUVORK5CYII=", 10,
        new byte[] { 255, 0, 255, 255, 0, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 255, 0, 0, 255 })]
    // 5 x 2, 2 bits: samples times 85.
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAUAAAACAgAAAAD/sVEgAAAADklEQVR4nGOUVmU66QAAAv8BTcvnxjUAAAAASUVORK5CYII=", 5,
        new byte[] { 0, 85, 170, 255, 85, 255, 170, 85, 0, 170 })]
    // 3 x 2, 4 bits: samples times 17.
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAMAAAACBAAAAAB979THAAAADklEQVR4nGNi/8DwQwAABfUCAgRc6NAAAAAASUVORK5CYII=", 3,
        new byte[] { 0, 119, 255, 255, 136, 17 })]
    public void A_low_bit_grayscale_png_is_embedded_with_8_bits(string png, int width, byte[] expected)
    {
        var image = EmbedAndReopen(png, width);

        Assert.Equal("/DeviceGray", image.Elements.GetName("/ColorSpace"));
        Assert.Equal(8, image.Elements.GetInteger("/BitsPerComponent"));
        Assert.Equal(expected, image.Stream!.UnfilteredValue.Take(expected.Length).ToArray());
    }

    // 5 x 1, 2-bit palette of four colours, indices 0 1 2 3 1: embedded as 4-bit indices.
    [Fact]
    public void A_2_bit_palette_png_is_embedded_with_its_palette()
    {
        var image = EmbedAndReopen(
            "iVBORw0KGgoAAAANSUhEUgAAAAUAAAABAgMAAABrkIxgAAAADFBMVEX/AAAAgAAAAP8KFB5X+Ko4AAAAC0lEQVR4nGOQdgAAAHkAXA90RxIAAAAASUVORK5CYII=", 5);

        var colorSpace = image.Elements.GetArray("/ColorSpace")!;
        Assert.Equal("/Indexed", colorSpace.Elements.GetName(0));
        Assert.Equal(3, colorSpace.Elements.GetInteger(2));
        Assert.Equal(4, image.Elements.GetInteger("/BitsPerComponent"));
        Assert.Equal(new byte[] { 0x01, 0x23, 0x10 }, image.Stream!.UnfilteredValue.Take(3).ToArray());
    }

    static PdfDictionary EmbedAndReopen(string png, int width)
    {
        using var saved = new MemoryStream();
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using (var graphics = XGraphics.FromPdfPage(page))
            using (var image = XImage.FromStream(new MemoryStream(Convert.FromBase64String(png))))
            {
                Assert.Equal(width, image.PixelWidth);
                graphics.DrawImage(image, 10, 10, 40, 20);
            }
            document.Save(saved);
        }

        var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        var xObjects = reopened.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        return xObjects.Elements.Values
            .Select(value => (value as PdfReference)?.Value as PdfDictionary)
            .Single(dictionary => dictionary?.Elements.GetName("/Subtype") == "/Image")!;
    }
}
