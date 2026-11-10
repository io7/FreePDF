// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// Draws the bars of module-based linear codes (Code 128, EAN, UPC) as one filled path.
    /// </summary>
    static class LinearCodeRenderer
    {
        /// <summary>
        /// Rotates the graphics for the code direction around the position.
        /// </summary>
        public static void ApplyDirection(XGraphics gfx, CodeDirection direction, XPoint position)
        {
            switch (direction)
            {
                case CodeDirection.RightToLeft:
                    gfx.RotateAtTransform(180, position);
                    break;

                case CodeDirection.TopToBottom:
                    gfx.RotateAtTransform(90, position);
                    break;

                case CodeDirection.BottomToTop:
                    gfx.RotateAtTransform(-90, position);
                    break;
            }
        }

        /// <summary>
        /// Draws each run of dark modules as one bar. The height of a bar is the height of its first module.
        /// </summary>
        public static void DrawBars(XGraphics gfx, XBrush brush, bool[] modules, double left, double top,
            double moduleWidth, Func<int, double> heightAt)
        {
            var path = new XGraphicsPath { FillMode = XFillMode.Winding };
            int i = 0;
            while (i < modules.Length)
            {
                if (!modules[i])
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < modules.Length && modules[i])
                    i++;
                path.AddRectangle(left + start * moduleWidth, top, (i - start) * moduleWidth, heightAt(start));
            }
            gfx.DrawPath(brush, path);
        }

        /// <summary>
        /// The font for human-readable text: the one passed to DrawBarCode, or Courier New at the given size.
        /// </summary>
        public static XFont TextFont(XFont? font, double emSize) => font ?? new XFont("Courier New", emSize);
    }
}
