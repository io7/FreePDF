// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// The error correction level of a QR code: how much of the symbol can be damaged or covered
    /// and still be read. A higher level makes the symbol larger for the same text.
    /// </summary>
    public enum QRErrorCorrectionLevel
    {
        /// <summary>
        /// About 7 % of the codewords can be restored.
        /// </summary>
        L,

        /// <summary>
        /// About 15 % of the codewords can be restored. The usual choice for print.
        /// </summary>
        M,

        /// <summary>
        /// About 25 % of the codewords can be restored.
        /// </summary>
        Q,

        /// <summary>
        /// About 30 % of the codewords can be restored, for example when a logo covers part of the symbol.
        /// </summary>
        H,
    }
}
