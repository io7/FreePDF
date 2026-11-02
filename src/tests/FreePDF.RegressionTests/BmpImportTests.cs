// BMP header integrity check. Found by the GDI package consumer: GDI+ and WPF bitmaps reach the BMP importer
// through MemoryStream.GetBuffer(), which is longer than the BMP it holds. Original fixtures; MIT, see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class BmpImportTests
{
    // A 2 x 2 pixel, 32 bpp BI_RGB bitmap with a BITMAPINFOHEADER, the layout GDI+ writes for 32bpp ARGB.
    static byte[] Bmp32(int trailingBytes = 0, int declaredFileSizeAdjustment = 0)
    {
        const int pixelBytes = 2 * 2 * 4;
        const int fileSize = 14 + 40 + pixelBytes;
        var bytes = new byte[fileSize + trailingBytes];
        void Word(int at, int value) { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); }
        void DWord(int at, int value) { Word(at, value); Word(at + 2, value >> 16); }
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        DWord(2, fileSize + declaredFileSizeAdjustment);
        DWord(10, 14 + 40);
        DWord(14, 40);      // BITMAPINFOHEADER
        DWord(18, 2);       // width
        DWord(22, 2);       // height (bottom-up)
        Word(26, 1);        // planes
        Word(28, 32);       // bits per pixel
        DWord(30, 0);       // BI_RGB
        DWord(34, pixelBytes);
        for (var i = 0; i < 4; i++)
        {
            var at = 54 + i * 4;
            bytes[at] = 0x40; bytes[at + 1] = 0x80; bytes[at + 2] = 0xC0; bytes[at + 3] = 0xFF; // B, G, R, A
        }
        return bytes;
    }

    static int EmbeddedImageCount(XImage image)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
            graphics.DrawImage(image, 10, 10, 20, 20);
        using var pdf = new MemoryStream();
        document.Save(pdf);
        pdf.Position = 0;
        using var reopened = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return reopened.Pages[0].Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject")?.Elements.Count ?? 0;
    }

    [Fact]
    public void A_bitmap_followed_by_extra_bytes_is_imported()
    {
        using var image = XImage.FromStream(new MemoryStream(Bmp32(trailingBytes: 202)));

        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(1, EmbeddedImageCount(image));
    }

    [Fact]
    public void An_exact_bitmap_is_imported()
    {
        using var image = XImage.FromStream(new MemoryStream(Bmp32()));

        Assert.Equal(1, EmbeddedImageCount(image));
    }

    [Fact]
    public void A_bitmap_that_declares_more_bytes_than_it_has_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => XImage.FromStream(new MemoryStream(Bmp32(declaredFileSizeAdjustment: 64))));
    }
}
