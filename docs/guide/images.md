# Images

<!-- snippet: image-fit -->
```csharp
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
```

- In the portable package `io7.FreePDF`, `XImage.FromFile` and `XImage.FromStream` read JPEG, PNG and BMP
  files. Convert other formats, such as GIF, TIFF or WebP, to PNG first.
- `PixelWidth` and `PixelHeight` give the image's size in pixels. `DrawImage` scales the image to the
  rectangle you give it, so compute the height from the width as above to keep its proportions.
- Dispose the `XImage` when you no longer draw with it.
