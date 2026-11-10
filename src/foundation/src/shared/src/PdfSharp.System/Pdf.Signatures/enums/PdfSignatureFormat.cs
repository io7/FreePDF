// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// The format of a signature's CMS (its /SubFilter, ISO 32000-2 12.8.3).
    /// </summary>
    public enum PdfSignatureFormat
    {
        /// <summary>
        /// adbe.pkcs7.detached: a detached CMS signature, as FreePDF wrote before 7.10.
        /// </summary>
        Pkcs7Detached = 0,

        /// <summary>
        /// ETSI.CAdES.detached: a PAdES baseline signature (ETSI EN 319 142-1). The CMS carries the
        /// signing-certificate-v2 attribute and no signing-time attribute; the claimed signing time is the signature
        /// dictionary's /M. With a signature time-stamp it is PAdES B-T, otherwise B-B.
        /// </summary>
        Pades = 1,

        /// <summary>
        /// ETSI.RFC3161: a document time-stamp (ISO 32000-2 12.8.5), an RFC 3161 time-stamp token over the document in an
        /// invisible signature field whose signature dictionary has /Type /DocTimeStamp. With the validation data of the
        /// document security store it makes the document's signatures PAdES B-LTA.
        /// </summary>
        DocumentTimeStamp = 2,
    }
}
