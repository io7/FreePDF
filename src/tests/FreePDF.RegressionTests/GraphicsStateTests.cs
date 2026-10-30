// FreePDF regression tests. Licensed under MIT; see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class GraphicsStateTests
{
    [Theory]
    [InlineData(false, PdfColorMode.Rgb, false)]
    [InlineData(false, PdfColorMode.Cmyk, false)]
    [InlineData(true, PdfColorMode.Rgb, false)]
    [InlineData(true, PdfColorMode.Cmyk, false)]
    [InlineData(false, PdfColorMode.Rgb, true)]
    [InlineData(false, PdfColorMode.Cmyk, true)]
    [InlineData(true, PdfColorMode.Rgb, true)]
    [InlineData(true, PdfColorMode.Cmyk, true)]
    public void First_transparent_paint_is_invisible(bool stroke, PdfColorMode mode, bool emptyColor)
    {
        var color = emptyColor ? XColor.Empty : XColor.FromArgb(0, 0, 0, 0);
        using var document = Render(gfx => Draw(gfx, color, stroke), mode);
        Assert.Equal(new[] { 0d }, PaintedAlphas(document.Pages[0], stroke));
        Assert.NotNull(document.Pages[0].Elements.GetDictionary("/Group"));
    }

    [Theory]
    [InlineData(false, PdfColorMode.Rgb)]
    [InlineData(false, PdfColorMode.Cmyk)]
    [InlineData(true, PdfColorMode.Rgb)]
    [InlineData(true, PdfColorMode.Cmyk)]
    public void Opacity_transitions_are_applied_to_each_paint(bool stroke, PdfColorMode mode)
    {
        using var document = Render(gfx =>
        {
            Draw(gfx, XColor.FromArgb(0, 0, 0, 0), stroke);
            Draw(gfx, XColors.Black, stroke);
            Draw(gfx, XColor.FromArgb(0, 0, 0, 0), stroke);
        }, mode);
        Assert.Equal(new[] { 0d, 1d, 0d }, PaintedAlphas(document.Pages[0], stroke));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_restore_preserves_opacity(bool stroke)
    {
        using var document = Render(gfx =>
        {
            Draw(gfx, XColors.Black, stroke);
            var state = gfx.Save();
            Draw(gfx, XColor.FromArgb(0, 0, 0, 0), stroke);
            gfx.Restore(state);
            Draw(gfx, XColors.Black, stroke);
        });
        Assert.Equal(new[] { 1d, 0d, 1d }, PaintedAlphas(document.Pages[0], stroke));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_opaque_paint_reuses_the_realized_state(bool stroke)
    {
        using var document = Render(gfx =>
        {
            Draw(gfx, XColors.Black, stroke);
            Draw(gfx, XColors.Black, stroke);
        });
        Assert.Equal(new[] { 1d, 1d }, PaintedAlphas(document.Pages[0], stroke));
        Assert.Single(Operators(document.Pages[0]).Where(op => op.Name == "gs"));
    }

    [Theory]
    [InlineData(PdfColorMode.Rgb, "rg")]
    [InlineData(PdfColorMode.Cmyk, "k")]
    public void Solid_black_after_gradient_restores_the_solid_color_space(PdfColorMode mode, string colorOperator)
    {
        using var document = Render(gfx =>
        {
            var gradient = new XLinearGradientBrush(new XPoint(10, 10), new XPoint(50, 50), XColors.Red, XColors.Blue);
            gfx.DrawRectangle(gradient, 10, 10, 40, 40);
            Draw(gfx, XColors.Black, false);
        }, mode);
        var afterPattern = Operators(document.Pages[0]).SkipWhile(op => op.Name != "scn").Skip(1).ToArray();
        Assert.Contains(afterPattern, op => op.Name == colorOperator);
        Assert.Equal(new[] { 1d, 1d }, PaintedAlphas(document.Pages[0], false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pdf_13_keeps_its_existing_opaque_behavior(bool stroke)
    {
        using var document = Render(gfx => Draw(gfx, XColor.FromArgb(0, 0, 0, 0), stroke), version: 13);
        Assert.Equal(new[] { 1d }, PaintedAlphas(document.Pages[0], stroke));
        Assert.Empty(Operators(document.Pages[0]).Where(op => op.Name == "gs"));
    }

    static void Draw(XGraphics gfx, XColor color, bool stroke)
    {
        if (stroke)
            gfx.DrawLine(new XPen(color, 2), 10, 10, 50, 50);
        else
            gfx.DrawRectangle(new XSolidBrush(color), 10, 10, 40, 40);
    }

    static PdfDocument Render(Action<XGraphics> draw, PdfColorMode mode = PdfColorMode.Rgb, int version = 17)
    {
        using var document = new PdfDocument();
        document.Version = version;
        document.Options.ColorMode = mode;
        document.Options.CompressContentStreams = false;
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            draw(gfx);
        using var stream = new MemoryStream();
        document.Save(stream);
        stream.Position = 0;
        return PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
    }

    static COperator[] Operators(PdfPage page) => ContentReader.ReadContent(page).OfType<COperator>().ToArray();

    // Interpret the relevant PDF graphics-state operators rather than inspecting private caches.
    static double[] PaintedAlphas(PdfPage page, bool stroke)
    {
        var alpha = 1d; // PDF's default opacity.
        var stack = new Stack<double>();
        var painted = new List<double>();
        foreach (var op in Operators(page))
        {
            switch (op.Name)
            {
                case "q": stack.Push(alpha); break;
                case "Q": alpha = stack.Pop(); break;
                case "gs":
                    var name = Assert.IsType<CName>(Assert.Single(op.Operands)).Name;
                    var states = page.Resources.Elements.GetDictionary("/ExtGState");
                    Assert.NotNull(states);
                    var state = states!.Elements.GetDictionary(name);
                    Assert.NotNull(state);
                    var key = stroke ? "/CA" : "/ca";
                    if (state!.Elements.ContainsKey(key))
                        alpha = state.Elements.GetReal(key);
                    break;
                case "S":
                    if (stroke) painted.Add(alpha);
                    break;
                case "f":
                case "f*":
                    if (!stroke) painted.Add(alpha);
                    break;
            }
        }
        return painted.ToArray();
    }
}
