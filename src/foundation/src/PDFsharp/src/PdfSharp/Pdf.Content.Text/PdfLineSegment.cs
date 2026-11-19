// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// A straight segment of a painted path, in the page's default user space (points, origin bottom left).
    /// </summary>
    public sealed class PdfLineSegment
    {
        internal PdfLineSegment(double x1, double y1, double x2, double y2, bool stroked)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Stroked = stroked;
        }

        /// <summary>The start point's horizontal position.</summary>
        public double X1 { get; }

        /// <summary>The start point's vertical position.</summary>
        public double Y1 { get; }

        /// <summary>The end point's horizontal position.</summary>
        public double X2 { get; }

        /// <summary>The end point's vertical position.</summary>
        public double Y2 { get; }

        /// <summary>True if the path was stroked (S, s, B, B*, b, b*); false if it was only filled.</summary>
        public bool Stroked { get; }
    }
}
