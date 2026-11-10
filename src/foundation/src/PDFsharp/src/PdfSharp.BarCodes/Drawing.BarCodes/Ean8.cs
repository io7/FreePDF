// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// An EAN-8 bar code (GTIN-8) with its digits under the bars.
    /// </summary>
    /// <remarks>
    /// <para>The code is 7 digits, to which the check digit is added, or 8 digits with a check digit that must
    /// be right.</para>
    /// <para><see cref="CodeBase.Size"/> spans the bars and their quiet zones (7 modules on each side): 81 modules
    /// in all. Set <see cref="BarCode.TextLocation"/> to None to leave the digits out.</para>
    /// </remarks>
    public class Ean8 : BarCode
    {
        /// <summary>
        /// Initializes a new EAN-8 bar code. Set <see cref="CodeBase.Size"/> before drawing it.
        /// </summary>
        public Ean8(string code)
            : this(code, XSize.Empty)
        { }

        /// <summary>
        /// Initializes a new EAN-8 bar code with its size, quiet zones included.
        /// </summary>
        public Ean8(string code, XSize size)
            : base(code, size, CodeDirection.LeftToRight)
        {
            TextLocation = TextLocation.Below;
        }

        /// <summary>
        /// The 8 digits of the code, with the check digit.
        /// </summary>
        public string FullCode => EanUpcSymbology.Complete(Text, EanUpcSymbology.Kind.Ean8);

        /// <summary>
        /// The 67 modules from start guard to end guard, true for bar.
        /// </summary>
        public bool[] GetModules() => EanUpcSymbology.Modules(FullCode, EanUpcSymbology.Kind.Ean8);

        /// <summary>
        /// Renders the bar code.
        /// </summary>
        protected internal override void Render(XGraphics gfx, XBrush brush, XFont? font, XPoint position)
            => EanUpcSymbology.Render(this, EanUpcSymbology.Kind.Ean8, gfx, brush, font, position);

        /// <summary>
        /// Checks for 7 digits, or 8 with the right check digit.
        /// </summary>
        protected override void CheckCode(string text) => EanUpcSymbology.Check(text, EanUpcSymbology.Kind.Ean8);
    }
}
