// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// A UPC-A bar code (GTIN-12) with its digits under the bars.
    /// </summary>
    /// <remarks>
    /// <para>The code is 11 digits, to which the check digit is added, or 12 digits with a check digit that must
    /// be right.</para>
    /// <para><see cref="CodeBase.Size"/> spans the bars and their quiet zones (9 modules on each side), so the
    /// first and last digit, printed beside the bars, stay inside it: 113 modules in all. Set
    /// <see cref="BarCode.TextLocation"/> to None to leave the digits out.</para>
    /// </remarks>
    public class UpcA : BarCode
    {
        /// <summary>
        /// Initializes a new UPC-A bar code. Set <see cref="CodeBase.Size"/> before drawing it.
        /// </summary>
        public UpcA(string code)
            : this(code, XSize.Empty)
        { }

        /// <summary>
        /// Initializes a new UPC-A bar code with its size, quiet zones included.
        /// </summary>
        public UpcA(string code, XSize size)
            : base(code, size, CodeDirection.LeftToRight)
        {
            TextLocation = TextLocation.Below;
        }

        /// <summary>
        /// The 12 digits of the code, with the check digit.
        /// </summary>
        public string FullCode => EanUpcSymbology.Complete(Text, EanUpcSymbology.Kind.UpcA);

        /// <summary>
        /// The 95 modules from start guard to end guard, true for bar.
        /// </summary>
        public bool[] GetModules() => EanUpcSymbology.Modules(FullCode, EanUpcSymbology.Kind.UpcA);

        /// <summary>
        /// Renders the bar code.
        /// </summary>
        protected internal override void Render(XGraphics gfx, XBrush brush, XFont? font, XPoint position)
            => EanUpcSymbology.Render(this, EanUpcSymbology.Kind.UpcA, gfx, brush, font, position);

        /// <summary>
        /// Checks for 11 digits, or 12 with the right check digit.
        /// </summary>
        protected override void CheckCode(string text) => EanUpcSymbology.Check(text, EanUpcSymbology.Kind.UpcA);
    }
}
