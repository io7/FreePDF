# Pages and coordinates

## Page size and orientation

A new page is A4 (595 × 842 points) when the machine's region uses metric units, and US Letter
(612 × 792 points) otherwise. So the same program can make different pages on two servers. Set the size
when it matters (`PageSize` and `PageOrientation` are in the namespace `PdfSharp`):

<!-- snippet: page-sizes -->
```csharp
using var document = new PdfDocument();

var a4 = document.AddPage();
a4.Size = PageSize.A4;                        // portrait
a4.Orientation = PageOrientation.Landscape;   // swaps width and height

var letter = document.AddPage();
letter.Size = PageSize.Letter;

var label = document.AddPage();               // any size, here 100 × 50 mm
label.Width = XUnit.FromMillimeter(100);
label.Height = XUnit.FromMillimeter(50);

document.Save("sizes.pdf");
```

`Size` sets the size as listed, so set `Orientation` after it. Ledger (1224 × 792 points) and Crown are
landscape; all other sizes are portrait. `PageSize` has the ISO A and B sizes, Letter, Legal, Ledger,
Tabloid and others.

## Units

FreePDF measures in points: 72 points make one inch, so one centimetre is 28.35 points.
`XUnit.FromMillimeter`, `XUnit.FromCentimeter` and `XUnit.FromInch` convert, and `.Point` gives the number
in points.

## Where (0, 0) is

<!-- snippet: coordinates -->
```csharp
using var document = new PdfDocument();
var page = document.AddPage();
double margin = XUnit.FromCentimeter(2).Point;   // 56.69 points

using (var gfx = XGraphics.FromPdfPage(page))
{
    var font = new XFont("Arial", 12);
    // (0, 0) is the top left corner; x grows to the right, y grows downwards.
    // DrawString's point is the left end of the baseline, the line the letters sit on.
    gfx.DrawString("2 cm from the left, 2 cm from the top", font, XBrushes.Black, margin, margin);
    gfx.DrawRectangle(XPens.Gray, margin, margin, page.Width.Point - 2 * margin, page.Height.Point - 2 * margin);
}

document.Save("coordinates.pdf");
```

`XGraphics` puts (0, 0) at the top left corner of the page. x grows to the right and y grows downwards.
The point you give `DrawString` is the left end of the baseline, so text drawn at y = 0 is above the page.
To place text by its top edge, give a rectangle and `XStringFormats.TopLeft`; see [Text](text.md).

PDF itself counts from the bottom left corner, with y growing upwards. You meet that only where FreePDF
passes PDF coordinates through: link rectangles ([Links and bookmarks](links-and-bookmarks.md)) and the
positions that [text extraction](../text-extraction.md) reports.
