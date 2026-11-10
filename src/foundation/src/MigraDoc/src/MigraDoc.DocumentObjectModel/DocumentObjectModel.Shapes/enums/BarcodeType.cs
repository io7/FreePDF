// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

// ReSharper disable InconsistentNaming

namespace MigraDoc.DocumentObjectModel.Shapes
{
    /// <summary>
    /// Specifies the type of the barcode.
    /// </summary>
    public enum BarcodeType
    {
        /// <summary>
        /// Barcode "Interleaved 2 of 5"
        /// </summary>
        Barcode25i,

        /// <summary>
        /// Barcode "3 of 9"
        /// </summary>
        Barcode39,

        /// <summary>
        /// Barcode "Code 128"
        /// </summary>
        Barcode128,

        /// <summary>
        /// EAN-13 (GTIN-13): 12 digits, or 13 with the check digit. The shape's width includes the quiet zones.
        /// </summary>
        Ean13,

        /// <summary>
        /// EAN-8 (GTIN-8): 7 digits, or 8 with the check digit. The shape's width includes the quiet zones.
        /// </summary>
        Ean8,

        /// <summary>
        /// UPC-A (GTIN-12): 11 digits, or 12 with the check digit. The shape's width includes the quiet zones.
        /// </summary>
        UpcA,

        /// <summary>
        /// QR code with error correction level M. The shape's size includes the quiet zone of 4 modules.
        /// </summary>
        QRCode,
    }
}
