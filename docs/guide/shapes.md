# Shapes and colours

<!-- snippet: shapes -->
```csharp
using var document = new PdfDocument();
var page = document.AddPage();

using (var gfx = XGraphics.FromPdfPage(page))
{
    var outline = new XPen(XColors.Black, 1);
    var dashed = new XPen(XColors.Gray, 0.5) { DashStyle = XDashStyle.Dash };
    var lightBlue = new XSolidBrush(XColor.FromArgb(255, 230, 240, 255));
    var halfRed = new XSolidBrush(XColor.FromArgb(128, 255, 0, 0));   // alpha 128: half transparent

    gfx.DrawLine(dashed, 50, 40, 545, 40);
    gfx.DrawRectangle(outline, lightBlue, 50, 60, 200, 100);   // filled, then outlined
    gfx.DrawEllipse(halfRed, 200, 100, 100, 100);              // filled only
    gfx.DrawPolygon(outline, XBrushes.Gold,
        new[] { new XPoint(150, 220), new XPoint(200, 320), new XPoint(100, 320) }, XFillMode.Winding);
}

document.Save("shapes.pdf");
```

- A pen (`XPen`) draws outlines: a colour, a width in points and, if you like, a `DashStyle`.
- A brush (`XBrush`, usually `XSolidBrush`) fills. Methods that take a pen and a brush fill first and draw
  the outline on top. Give only a pen or only a brush to do one of the two.
- `XPens` and `XBrushes` have a ready-made pen (1 point wide) and brush for each named colour in `XColors`.
  You cannot change those pens; make a new `XPen` instead.
- `XColor.FromArgb(alpha, red, green, blue)` makes any colour. An alpha below 255 makes it transparent.

`XGraphics` also has `DrawBezier`, `DrawArc`, `DrawPie`, `DrawRoundedRectangle`, and `DrawPath` with an
`XGraphicsPath` for shapes made of several lines and curves.

## Rotating, moving and scaling

<!-- snippet: rotate -->
```csharp
using var document = new PdfDocument();
var page = document.AddPage();

using (var gfx = XGraphics.FromPdfPage(page))
{
    var font = new XFont("Arial", 10);
    var state = gfx.Save();
    gfx.RotateAtTransform(-90, new XPoint(30, 400));   // degrees; positive turns clockwise
    gfx.DrawString("Printed 2026-11-19", font, XBrushes.Gray, 30, 400);
    gfx.Restore(state);                                // what follows is not rotated
    gfx.DrawString("Not rotated", font, XBrushes.Black, 50, 50);
}

document.Save("rotate.pdf");
```

`RotateAtTransform`, `TranslateTransform` and `ScaleTransform` change the coordinates of everything you
draw after them. `Save` keeps the current state and `Restore` returns to it.
