// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Drawing.BarCodes
{
    // TODO_OLD: Mere with PDFsharp strings table
    /// <summary>
    /// String resources for the empira barcode renderer.
    /// </summary>
    class BcgSR
    {
        internal static string Invalid2Of5Code(string code)
        {
            return $"'{code}' is not a valid code for an interleave 2 of 5 bar code. It can only represent an even number of digits.";
        }

        internal static string Invalid3Of9Code(string code)
        {
            return $"'{code}' is not a valid code for a 3 of 9 standard bar code.";
        }

        internal static string BarCodeNotSet => "A text must be set before rendering the bar code.";

        internal static string EmptyBarCodeSize => "A non-empty size must be set before rendering the bar code.";

        internal static string Invalid2of5Relation => "Value of relation between thick and thin lines on the interleaved 2 of 5 code must be between 2 and 3.";

        internal static string InvalidMarkName(string name)
        {
            return $"'{name}' is not a valid mark name for this OMR representation.";
        }

        internal static string OmrAlreadyInitialized => "Mark descriptions cannot be set when marks have already been set on OMR.";

        internal static string InvalidCode128(string code)
            => $"'{code}' cannot be encoded as Code 128: it must not be empty and may hold only ASCII characters and Code128.Fnc1.";

        internal static string InvalidEanUpcCode(string code, string symbology, int dataDigits)
            => $"'{code}' is not a valid {symbology} code: it must be {dataDigits} digits, or {dataDigits + 1} with the check digit.";

        internal static string WrongEanUpcCheckDigit(string code, string symbology, char expected)
            => $"The check digit of the {symbology} code '{code}' is wrong; it should be {expected}.";

        internal static string EanUpcTooLow => "The bar code is not high enough for its bars and the digits under them.";

        internal static string QRTextTooLong(int version, QRErrorCorrectionLevel level)
            => version == 0
                ? $"The text is too long for a QR code with error correction level {level}."
                : $"The text is too long for a version {version} QR code with error correction level {level}.";

        internal static string QREmptyText => "A QR code needs at least one character; readers do not report empty codes.";

        internal static string QRVersionOutOfRange => "The QR code version must be 0 (the smallest that fits) or 1 to 40.";

        internal static string QRQuietZoneNegative => "The quiet zone must not be negative.";

        internal static string DataMatrixTooBig =>"The given data and encoding combination is too big for the matrix size.";

        internal static string DataMatrixNotSupported => "Zero sizes, odd sizes and other than ecc200 coded DataMatrix is not supported.";

        internal static string DataMatrixNull => "No DataMatrix code is produced.";

        internal static string DataMatrixInvalid(int columns, int rows)
        {
            return $"'{rows}'x'{columns}' is an invalid ecc200 DataMatrix size.";
        }
    }
}
