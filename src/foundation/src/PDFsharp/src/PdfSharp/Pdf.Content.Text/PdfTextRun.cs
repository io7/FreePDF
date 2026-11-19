// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// The text of one show-text operator (Tj, TJ, ' or ") and where it is drawn. Coordinates are in the page's
    /// default user space: points, origin at the bottom left of the page, before the page's /Rotate.
    /// </summary>
    public sealed class PdfTextRun
    {
        internal PdfTextRun(string text, double x, double y, double endX, double endY, double fontSize, string fontName)
        {
            Text = text;
            X = x;
            Y = y;
            EndX = endX;
            EndY = endY;
            FontSize = fontSize;
            FontName = fontName;
        }

        /// <summary>The Unicode text. Characters without a known Unicode value are U+FFFD.</summary>
        public string Text { get; }

        /// <summary>The horizontal position of the baseline where the run starts.</summary>
        public double X { get; }

        /// <summary>The vertical position of the baseline where the run starts.</summary>
        public double Y { get; }

        /// <summary>The horizontal position of the baseline after the run's last glyph.</summary>
        public double EndX { get; }

        /// <summary>The vertical position of the baseline after the run's last glyph.</summary>
        public double EndY { get; }

        /// <summary>The font size as drawn on the page, in points (the text matrix and transformations applied).</summary>
        public double FontSize { get; }

        /// <summary>The PostScript name of the font (/BaseFont without a subset prefix), or "" if it has none.</summary>
        public string FontName { get; }

        /// <inheritdoc/>
        public override string ToString() => $"\"{Text}\" at ({X:0.##}, {Y:0.##}), {FontSize:0.##} pt {FontName}";
    }
}
