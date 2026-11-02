// Variant-specific check for the Windows packages: an image from the platform's own bitmap type reaches the PDF. MIT.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

static class WindowsVariant
{
    internal static void Check()
    {
#if FREEPDF_GDI || FREEPDF_WPF
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
        using (var image = CreatePlatformImage())
            graphics.DrawImage(image, 20, 20, 40, 40);
        using var pdf = new MemoryStream();
        document.Save(pdf);
        pdf.Position = 0;
        using var reopened = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        var resources = reopened.Pages[0].Elements.GetDictionary("/Resources");
        var images = resources?.Elements.GetDictionary("/XObject");
        if (images is null || images.Elements.Count != 1) throw new InvalidDataException("Platform bitmap was not embedded as one image.");
        Console.WriteLine("PASS: platform bitmap embedded through XImage on " + Variant);
#endif
    }

#if FREEPDF_GDI
    const string Variant = "GDI+";

    static XImage CreatePlatformImage()
    {
        var bitmap = new System.Drawing.Bitmap(8, 8);
        using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.Clear(System.Drawing.Color.SteelBlue);
        return XImage.FromGdiPlusImage(bitmap);
    }
#elif FREEPDF_WPF
    const string Variant = "WPF";

    static XImage CreatePlatformImage()
    {
        var pixels = Enumerable.Repeat((byte)0x80, 8 * 8 * 4).ToArray();
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(8, 8, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 8 * 4);
        return XImage.FromBitmapSource(bitmap);
    }
#endif
}
