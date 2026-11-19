// Examples for docs/guide/shapes.md. MIT; see LICENSE.
using System.Globalization;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.Samples;

public sealed class ShapeSamples : SampleTest
{
    [Fact]
    public void Shapes()
    {
        // begin-snippet: shapes
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
        // end-snippet

        var top = MediaSize("shapes.pdf").Height;
        var segments = Lines("shapes.pdf");
        // The rectangle's top edge, 60 points below the top of the page, from x 50 to 250.
        Assert.Contains(segments, s => Near(top - 60, s.Y1) && Near(top - 60, s.Y2)
            && Near(50, Math.Min(s.X1, s.X2)) && Near(250, Math.Max(s.X1, s.X2)));
        // The dashed line.
        Assert.Contains(segments, s => Near(top - 40, s.Y1) && Near(top - 40, s.Y2) && Near(545, Math.Max(s.X1, s.X2)));
        // The triangle's base.
        Assert.Contains(segments, s => Near(top - 320, s.Y1) && Near(top - 320, s.Y2) && Near(100, Math.Min(s.X1, s.X2)));

        // FreePDF writes one operator per line. The styles must belong to their shapes, not just occur in the page.
        var content = Content("shapes.pdf");
        string y40 = Pt(top - 40), y150 = Pt(top - 150);
        // The horizontal line is stroked while a dash pattern ("[1.5 .5]0 d") is set: no other "d" operator, whose
        // array starts with "[", comes between the pattern and the line.
        Assert.Matches($@"\[[\d. ]+\]\s*[\d.]+ d\n[^\[]*\n50 {y40} m\n545 {y40} l\nS\n", content);
        // The ellipse (four curves from its right end) is filled red through a graphics state with 50% fill alpha.
        var ellipse = Regex.Match(content, $@"1 0 0 rg\n/(\w+) gs\n300 {y150} m\n(?:[-\d. ]+ c\n){{4}}h f\n");
        Assert.True(ellipse.Success, content);
        using var saved = PdfReader.Open("shapes.pdf", PdfDocumentOpenMode.Import);
        var states = saved.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/ExtGState")!;
        AssertNear(128 / 255.0, states.Elements.GetDictionary("/" + ellipse.Groups[1].Value)!.Elements.GetReal("/ca"), 0.01);
    }

    /// <summary>A coordinate as FreePDF writes it in content streams.</summary>
    static string Pt(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    [Fact]
    public void RotatedText()
    {
        // begin-snippet: rotate
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
        // end-snippet

        var runs = Runs("rotate.pdf");
        var rotated = runs.Single(r => r.Text == "Printed 2026-11-19");
        AssertNear(rotated.X, rotated.EndX);             // the run goes straight up the page
        Assert.True(rotated.EndY > rotated.Y + 50);
        var level = runs.Single(r => r.Text == "Not rotated");
        AssertNear(level.Y, level.EndY);
    }
}
