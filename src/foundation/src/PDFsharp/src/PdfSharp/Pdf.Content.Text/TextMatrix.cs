// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// A PDF transformation matrix [a b c d e f] (ISO 32000-1 8.3.4). Points are row vectors: [x y 1] × M.
    /// </summary>
    readonly struct TextMatrix
    {
        internal readonly double A, B, C, D, E, F;

        internal TextMatrix(double a, double b, double c, double d, double e, double f)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }

        internal static readonly TextMatrix Identity = new(1, 0, 0, 1, 0, 0);

        internal static TextMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

        /// <summary>This matrix followed by <paramref name="next"/>: this × next.</summary>
        internal TextMatrix Then(TextMatrix next) => new(
            A * next.A + B * next.C, A * next.B + B * next.D,
            C * next.A + D * next.C, C * next.B + D * next.D,
            E * next.A + F * next.C + next.E, E * next.B + F * next.D + next.F);

        internal double X(double x, double y) => x * A + y * C + E;

        internal double Y(double x, double y) => x * B + y * D + F;
    }
}
