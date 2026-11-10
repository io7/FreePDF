// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Implemented by an <see cref="IDigitalSigner"/> whose CMS is not adbe.pkcs7.detached, so that the signature
    /// dictionary gets the matching /SubFilter. A signer without it writes adbe.pkcs7.detached.
    /// </summary>
    public interface IDigitalSignerFormat
    {
        /// <summary>
        /// Gets the format of the signatures this signer creates.
        /// </summary>
        PdfSignatureFormat Format { get; }
    }
}
