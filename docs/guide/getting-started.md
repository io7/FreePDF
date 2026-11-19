# Getting started

## Install

```
dotnet add package io7.FreePDF
```

`io7.FreePDF` runs on Windows, Linux and macOS, on .NET 8, 9 and 10, and on .NET Framework 4.6.2 or later
through its .NET Standard 2.0 build. On Windows, `io7.FreePDF.Gdi` and `io7.FreePDF.Wpf` draw through GDI+
or WPF instead. The code in this guide is the same for all three packages, except where [Fonts](fonts.md)
says otherwise.

The namespaces are the ones PDFsharp uses. The examples need these:

```csharp
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
```

## A first PDF

<!-- snippet: hello-world -->
```csharp
using var document = new PdfDocument();
document.Info.Title = "Hello";
var page = document.AddPage();

using (var gfx = XGraphics.FromPdfPage(page))
{
    var font = new XFont("Arial", 20);
    var wholePage = new XRect(0, 0, page.Width.Point, page.Height.Point);
    gfx.DrawString("Hello, FreePDF!", font, XBrushes.Black, wholePage, XStringFormats.Center);
}

document.Save("hello.pdf");
```

A `PdfDocument` holds the pages. `XGraphics.FromPdfPage` gives you a surface to draw on one page.
Dispose the `XGraphics` (end its `using` block) before you save the document. [Pages and
coordinates](pages.md) explains the numbers, and [Text](text.md) the alignment.

## Save to a stream

A web application usually sends the PDF instead of writing a file:

<!-- snippet: save-to-stream -->
```csharp
using var document = new PdfDocument();
var page = document.AddPage();
using (var gfx = XGraphics.FromPdfPage(page))
    gfx.DrawString("Invoice 2026-118", new XFont("Arial", 14), XBrushes.Black, 50, 50);

using var stream = new MemoryStream();
document.Save(stream);          // the stream stays open
byte[] pdf = stream.ToArray();  // in ASP.NET Core: return File(pdf, "application/pdf", "invoice.pdf");
```

## Next

[Pages and coordinates](pages.md)
