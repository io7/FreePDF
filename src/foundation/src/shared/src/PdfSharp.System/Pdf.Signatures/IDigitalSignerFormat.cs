// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Implemented by an <see cref="IDigitalSigner"/> whose CMS is not adbe.pkcs7.detached, so that the signature
    /// dictionary gets the matching /SubFilter. A signer without it writes adbe.pkcs7.detached. A signer that wraps
    /// another one (for logging or a hardware key, for example) must implement it too and return the wrapped signer's
    /// format; otherwise a PAdES signature is labelled adbe.pkcs7.detached.
    /// </summary>
    public interface IDigitalSignerFormat
    {
        /// <summary>
        /// Gets the format of the signatures this signer creates.
        /// </summary>
        PdfSignatureFormat Format { get; }
    }
}
