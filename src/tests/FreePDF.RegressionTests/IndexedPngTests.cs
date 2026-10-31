// Authored palette PNGs; no upstream images or test code. MIT.
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;
using FreePDF.Lab.Fixtures;

namespace FreePDF.RegressionTests;

public class IndexedPngTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 7)]
    [InlineData(1, 8)]
    [InlineData(1, 9)]
    [InlineData(4, 1)]
    [InlineData(4, 5)]
    [InlineData(4, 6)]
    [InlineData(8, 5)]
    public void Palette_alpha_keeps_every_pixel_and_ignores_row_padding(int depth, int width)
    {
        var png = PalettePng.Create(depth, width, 2, true);
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var image = XImage.FromStream(new MemoryStream(png));
        using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(image, 0, 0);
        var images = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        var embedded = Assert.Single(images.Elements.Values.OfType<PdfSharp.Pdf.Advanced.PdfReference>()).Value as PdfDictionary;
        var mask = embedded!.Elements.GetDictionary("/SMask");
        Assert.NotNull(mask);
        Assert.Equal(width, mask!.Elements.GetInteger("/Width"));
        Assert.Equal(2, mask.Elements.GetInteger("/Height"));
        var expected = Enumerable.Range(0, width * 2).Select(i => PalettePng.Alpha(depth, i)).ToArray();
        Assert.Equal(expected, mask.Stream!.UnfilteredValue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Opaque_palette_does_not_create_a_soft_mask(int depth)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var image = XImage.FromStream(new MemoryStream(PalettePng.Create(depth, 5, 2, false)));
        using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(image, 0, 0);
        var objects = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        var embedded = (PdfDictionary)Assert.Single(objects.Elements.Values.OfType<PdfSharp.Pdf.Advanced.PdfReference>()).Value;
        Assert.False(embedded.Elements.ContainsKey("/SMask"));
    }
}

