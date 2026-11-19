# Links and bookmarks

## Links

<!-- snippet: links -->
```csharp
using var document = new PdfDocument();
var first = document.AddPage();
var second = document.AddPage();
var font = new XFont("Arial", 12);

using (var gfx = XGraphics.FromPdfPage(first))
{
    var web = new XRect(50, 50, 200, 16);
    gfx.DrawString("example.com", font, XBrushes.Blue, web, XStringFormats.CenterLeft);
    // Link rectangles use PDF page coordinates (origin bottom left); the transformer converts.
    first.AddWebLink(new PdfRectangle(gfx.Transformer.WorldToDefaultPage(web)), "https://example.com/");

    var next = new XRect(50, 80, 200, 16);
    gfx.DrawString("Go to page 2", font, XBrushes.Blue, next, XStringFormats.CenterLeft);
    first.AddDocumentLink(new PdfRectangle(gfx.Transformer.WorldToDefaultPage(next)), 2);   // pages count from 1
}
```

A link is an area of the page that the reader can click. It draws nothing: draw the text or a frame
yourself, as above.

- `AddWebLink` opens a web address. `AddDocumentLink` goes to a page of the same document; pages count
  from 1.
- The rectangle is in PDF page coordinates, with the origin at the bottom left. `XGraphics` coordinates
  start at the top left, so convert the rectangle you drew with `gfx.Transformer.WorldToDefaultPage`.

## Bookmarks

<!-- snippet: bookmarks -->
```csharp
var chapter = document.Outlines.Add("Chapter 1", first);
chapter.Outlines.Add("Section 1.1", second);
document.Save("links.pdf");
```

Bookmarks (outlines) appear in the reader's side panel and go to their page. Each bookmark has its own
`Outlines` for the next level.
