// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// An EAN-13 bar code (GTIN-13, ISBN) with its digits under the bars.
    /// </summary>
    /// <remarks>
    /// <para>The code is 12 digits, to which the check digit is added, or 13 digits with a check digit that must
    /// be right.</para>
    /// <para><see cref="CodeBase.Size"/> spans the bars and their quiet zones (11 modules left, 7 right), so the
    /// first digit, printed left of the bars, stays inside it. The width is 113 modules; a nominal EAN-13 is
    /// 37.29 mm wide and 25.93 mm high. Set <see cref="BarCode.TextLocation"/> to None to leave the digits out.</para>
    /// </remarks>
    public class Ean13 : BarCode
    {
        /// <summary>
        /// Initializes a new EAN-13 bar code. Set <see cref="CodeBase.Size"/> before drawing it.
        /// </summary>
        public Ean13(string code)
            : this(code, XSize.Empty)
        { }

        /// <summary>
        /// Initializes a new EAN-13 bar code with its size, quiet zones included.
        /// </summary>
        public Ean13(string code, XSize size)
            : base(code, size, CodeDirection.LeftToRight)
        {
            TextLocation = TextLocation.Below;
        }

        /// <summary>
        /// The 13 digits of the code, with the check digit.
        /// </summary>
        public string FullCode => EanUpcSymbology.Complete(Text, EanUpcSymbology.Kind.Ean13);

        /// <summary>
        /// The 95 modules from start guard to end guard, true for bar.
        /// </summary>
        public bool[] GetModules() => EanUpcSymbology.Modules(FullCode, EanUpcSymbology.Kind.Ean13);

        /// <summary>
        /// Renders the bar code.
        /// </summary>
        protected internal override void Render(XGraphics gfx, XBrush brush, XFont? font, XPoint position)
            => EanUpcSymbology.Render(this, EanUpcSymbology.Kind.Ean13, gfx, brush, font, position);

        /// <summary>
        /// Checks for 12 digits, or 13 with the right check digit.
        /// </summary>
        protected override void CheckCode(string text) => EanUpcSymbology.Check(text, EanUpcSymbology.Kind.Ean13);
    }
}
