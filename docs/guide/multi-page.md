# Documents with many pages

`XGraphics` draws on one page and does not know when the page is full. Your code decides when to start the
next page. This example writes a price list as a table and repeats the header on each page; `items` is a
list of name and price pairs:

<!-- snippet: table-pages -->
```csharp
using var document = new PdfDocument();
var font = new XFont("Arial", 10);
var bold = new XFont("Arial", 10, XFontStyleEx.Bold);
const double left = 50, right = 350, top = 60, bottom = 60, rowHeight = 16;

XGraphics StartPage()
{
    var page = document.AddPage();
    var g = XGraphics.FromPdfPage(page);
    g.DrawString("Item", bold, XBrushes.Black, left, top);
    g.DrawString("Price", bold, XBrushes.Black, right, top, XStringFormats.BaseLineRight);
    g.DrawLine(XPens.Black, left, top + 4, right, top + 4);
    return g;
}

var gfx = StartPage();
double y = top + rowHeight;
foreach (var (name, price) in items)
{
    if (y > gfx.PageSize.Height - bottom)   // no room for another row
    {
        gfx.Dispose();
        gfx = StartPage();
        y = top + rowHeight;
    }
    gfx.DrawString(name, font, XBrushes.Black, left, y);
    gfx.DrawString(price.ToString("N2", CultureInfo.InvariantCulture), font, XBrushes.Black,
        right, y, XStringFormats.BaseLineRight);
    y += rowHeight;
}
gfx.Dispose();
```

Add the page numbers last, when you know how many pages there are. `XGraphicsPdfPageOptions.Append` draws
on top of what the page already has:

<!-- snippet: page-numbers -->
```csharp
int count = document.PageCount;
for (int i = 0; i < count; i++)
{
    var page = document.Pages[i];
    using var footer = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
    var line = new XRect(0, page.Height.Point - 40, page.Width.Point, 20);
    footer.DrawString($"Page {i + 1} of {count}", font, XBrushes.Gray, line, XStringFormats.Center);
}
document.Save("price-list.pdf");
```

For running text with automatic page breaks, headers and footers, and tables that size themselves, use
MigraDoc (`io7.FreePDF.Layout`), which lays out the document for you.
