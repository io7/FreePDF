// Examples for docs/guide/text.md. MIT; see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.Samples;

public sealed class TextSamples : SampleTest
{
    [Fact]
    public void Alignment()
    {
        // begin-snippet: text-alignment
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
        // end-snippet

        var width = MediaSize("alignment.pdf").Width;
        var runs = Runs("alignment.pdf");
        AssertNear(50, runs.Single(r => r.Text == "Left").X);
        AssertNear(width - 50, runs.Single(r => r.Text == "Right").EndX);
        var centre = runs.Single(r => r.Text == "Centre");
        AssertNear(width / 2, (centre.X + centre.EndX) / 2);
    }

    [Fact]
    public void Measure()
    {
        // begin-snippet: measure-text
        using var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 16);
            const string title = "Quarterly report";
            XSize size = gfx.MeasureString(title, font);   // width, and the line height of the font, in points

            gfx.DrawString(title, font, XBrushes.Black, 50, 80);
            gfx.DrawLine(new XPen(XColors.SteelBlue, 1.5), 50, 84, 50 + size.Width, 84);   // as wide as the text
        }

        document.Save("measure.pdf");
        // end-snippet

        var run = Assert.Single(Runs("measure.pdf"));
        var underline = Assert.Single(Lines("measure.pdf"));
        AssertNear(run.EndX - run.X, Math.Abs(underline.X2 - underline.X1));
    }

    [Fact]
    public void Paragraph()
    {
        // begin-snippet: text-formatter
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
        // end-snippet

        var runs = Runs("paragraph.pdf");
        Assert.True(runs.Select(r => Math.Round(r.Y)).Distinct().Count() >= 4);   // wrapped onto several lines
        Assert.All(runs, r => Assert.InRange(r.EndX, 50, 250.5));
        Assert.Equal(Words(text), Words(Text("paragraph.pdf")));
    }

    [Fact]
    public void ParagraphTallerThanItsBox()
    {
        // Checks the guide's statement that text below the rectangle is not drawn.
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var text = string.Join(" ", Enumerable.Repeat("word", 300));
                new XTextFormatter(gfx).DrawString(text, new XFont("Arial", 11), XBrushes.Black,
                    new XRect(50, 50, 200, 30), XStringFormats.TopLeft);
            }
            document.Save("overflow.pdf");
        }

        var lines = Runs("overflow.pdf").Select(r => Math.Round(r.Y)).Distinct().Count();
        Assert.InRange(lines, 1, 2);   // 30 points hold two lines of 11-point text
    }
}
