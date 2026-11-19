# Text

## Alignment

`DrawString` with a rectangle and an `XStringFormat` aligns the text in the rectangle:

<!-- snippet: text-alignment -->
```csharp
using var document = new PdfDocument();
var page = document.AddPage();

using (var gfx = XGraphics.FromPdfPage(page))
{
    var font = new XFont("Arial", 12);
    var bold = new XFont("Arial", 12, XFontStyleEx.Bold);
    var line = new XRect(50, 50, page.Width.Point - 100, 20);

    gfx.DrawString("Left", font, XBrushes.Black, line, XStringFormats.CenterLeft);
    gfx.DrawString("Centre", bold, XBrushes.DarkBlue, line, XStringFormats.Center);
    gfx.DrawString("Right", font, XBrushes.Black, line, XStringFormats.CenterRight);
}

document.Save("alignment.pdf");
```

`XStringFormats` has the nine combinations of `Top`, `Center` and `Bottom` with `Left`, `Center` and
`Right` (`TopLeft`, `CenterRight`, `BottomCenter`, ...), and `BaseLineLeft`, `BaseLineCenter` and
`BaseLineRight` for a point on the baseline. `DrawString` draws one line: it does not wrap.

## Measuring text

`MeasureString` returns the width of the text and its height from the font's ascent to its descent. The
distance from one baseline to the next is `font.GetHeight()`, which can be larger.

<!-- snippet: measure-text -->
```csharp
using var document = new PdfDocument();
var page = document.AddPage();

using (var gfx = XGraphics.FromPdfPage(page))
{
    var font = new XFont("Arial", 16);
    const string title = "Quarterly report";
    XSize size = gfx.MeasureString(title, font);   // width, and height from ascent to descent, in points

    gfx.DrawString(title, font, XBrushes.Black, 50, 80);
    gfx.DrawLine(new XPen(XColors.SteelBlue, 1.5), 50, 84, 50 + size.Width, 84);   // as wide as the text
}

document.Save("measure.pdf");
```

## Paragraphs

`XTextFormatter`, in the namespace `PdfSharp.Drawing.Layout`, breaks text into lines that fit a rectangle:

<!-- snippet: text-formatter -->
```csharp
const string text =
    "XGraphics.DrawString writes one line. XTextFormatter breaks a longer text into lines that fit " +
    "a rectangle, and starts a new line at each line break in the text. Justified lines end at the " +
    "right edge of the rectangle, except the last one.";

using var document = new PdfDocument();
var page = document.AddPage();

using (var gfx = XGraphics.FromPdfPage(page))
{
    var box = new XRect(50, 50, 200, 300);
    var formatter = new XTextFormatter(gfx) { Alignment = XParagraphAlignment.Justify };
    formatter.DrawString(text, new XFont("Arial", 11), XBrushes.Black, box, XStringFormats.TopLeft);
}

document.Save("paragraph.pdf");
```

- It breaks lines at spaces and at the line breaks in the text.
- `Alignment` is `Left` (the default), `Center`, `Right` or `Justify`.
- The format must be `XStringFormats.TopLeft`; other formats throw `ArgumentException`.
- Lines below the rectangle's height are not drawn. The first line always is, even in a rectangle lower
  than one line. Measure first, or split the text over pages yourself; see
  [Documents with many pages](multi-page.md).
- A word wider than the rectangle is not broken: it runs past the right edge.
- One call uses one font. For paragraphs with mixed fonts, lists and tables, MigraDoc
  (`io7.FreePDF.Layout`) lays out the text for you.
