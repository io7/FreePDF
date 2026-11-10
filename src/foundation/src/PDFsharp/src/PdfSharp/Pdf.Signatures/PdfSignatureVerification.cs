// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// The overall result of checking one signature.
    /// </summary>
    public enum PdfSignatureStatus
    {
        /// <summary>
        /// Intact, made with a certificate that chains to a trusted root, not revoked as far as checked, and no later
        /// change that its permissions forbid.
        /// </summary>
        Valid,

        /// <summary>
        /// The signed bytes or the signature do not match, the certificate was revoked before the signature was made,
        /// or a later change breaks it.
        /// </summary>
        Invalid,

        /// <summary>
        /// Intact, but trust could not be established: an untrusted or incomplete chain, missing revocation data, an
        /// expired certificate without proof that the signature existed before, or a check this platform cannot make.
        /// </summary>
        Indeterminate,
    }

    /// <summary>
    /// What a signature dictionary is.
    /// </summary>
    public enum PdfSignatureKind
    {
        /// <summary>An ordinary (approval) signature.</summary>
        Approval,

        /// <summary>A certification signature (DocMDP), which limits later changes.</summary>
        Certification,

        /// <summary>A document time-stamp (/Type /DocTimeStamp, ETSI.RFC3161).</summary>
        DocumentTimeStamp,
    }

    /// <summary>
    /// The PAdES baseline level a signature reaches (ETSI EN 319 142-1), as far as FreePDF checks it.
    /// </summary>
    public enum PdfPadesLevel
    {
        /// <summary>Not a PAdES signature (for example adbe.pkcs7.detached), or not even B-B.</summary>
        None,

        /// <summary>ETSI.CAdES.detached with a signing-certificate attribute that matches the signer.</summary>
        BaselineB,

        /// <summary>B-B with a valid signature time-stamp.</summary>
        BaselineT,

        /// <summary>B-T with validation data in the document for the whole chain.</summary>
        BaselineLT,

        /// <summary>B-LT with a valid document time-stamp over the validation data.</summary>
        BaselineLTA,
    }

    /// <summary>
    /// Where revocation data comes from when a signature is checked.
    /// </summary>
    public enum PdfRevocationCheck
    {
        /// <summary>Revocation is not checked.</summary>
        None,

        /// <summary>OCSP responses and CRLs in the document's DSS only.</summary>
        Document,

        /// <summary>The DSS first; for certificates it does not cover, the responders and CRLs the certificates name.</summary>
        Online,
    }

    /// <summary>
    /// The revocation status found for a signer's chain.
    /// </summary>
    public enum PdfRevocationStatus
    {
        /// <summary>Not checked.</summary>
        NotChecked,

        /// <summary>Every certificate below the root has a verified OCSP response or CRL that does not list it.</summary>
        Good,

        /// <summary>A certificate of the chain is revoked.</summary>
        Revoked,

        /// <summary>Some certificate has no usable revocation data.</summary>
        Unknown,
    }

    /// <summary>
    /// Options for <see cref="PdfSignatureVerifier"/>.
    /// </summary>
    public sealed class PdfSignatureVerificationOptions
    {
        /// <summary>
        /// Gets the root certificates to trust. When null, the system's trusted roots are used.
        /// </summary>
        public IEnumerable<X509Certificate2>? TrustedRoots { get; init; }

        /// <summary>
        /// Gets where revocation data comes from. The default is <see cref="PdfRevocationCheck.Document"/>.
        /// </summary>
        public PdfRevocationCheck Revocation { get; init; } = PdfRevocationCheck.Document;

        /// <summary>
        /// Gets the time to validate at. The default is now.
        /// </summary>
        public DateTimeOffset? ValidationTime { get; init; }

        /// <summary>
        /// Gets the options for requests when <see cref="Revocation"/> is <see cref="PdfRevocationCheck.Online"/>: the
        /// HttpClient, private networks, the size limit. Extra certificates given there also complete the chains.
        /// </summary>
        public PdfValidationDataOptions? Online { get; init; }

        /// <summary>
        /// Gets the password of an encrypted document.
        /// </summary>
        public string? Password { get; init; }
    }

    /// <summary>
    /// The facts FreePDF established about one signature, and a status. It is a technical check, not a statement about
    /// the legal effect of the signature.
    /// </summary>
    public sealed class PdfSignatureVerification
    {
        /// <summary>Gets the fully qualified name of the signature field.</summary>
        public string FieldName { get; internal set; } = "";

        /// <summary>Gets what the signature is.</summary>
        public PdfSignatureKind Kind { get; internal set; }

        /// <summary>Gets the DocMDP permission (1, 2 or 3) of a certification signature.</summary>
        public int? CertificationLevel { get; internal set; }

        /// <summary>Gets the /SubFilter, without the slash.</summary>
        public string SubFilter { get; internal set; } = "";

        /// <summary>Gets the PAdES baseline level reached.</summary>
        public PdfPadesLevel PadesLevel { get; internal set; }

        /// <summary>Gets the 1-based number of the revision the signature covers, counted in the file's updates.</summary>
        public int Revision { get; internal set; }

        /// <summary>Gets the number of revisions in the file.</summary>
        public int Revisions { get; internal set; }

        /// <summary>Gets a value indicating whether the signature covers the whole file, with no later update.</summary>
        public bool CoversWholeFile { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the signed bytes are intact: a well-formed /ByteRange, the digest matches,
        /// and the signature verifies with the signer's key (or, for a document time-stamp, the token's imprint and
        /// signature).
        /// </summary>
        public bool Intact { get; internal set; }

        /// <summary>Gets the signer's certificate, or the time-stamp authority's for a document time-stamp.</summary>
        public X509Certificate2? Signer { get; internal set; }

        /// <summary>Gets the signer's chain, leaf first, as far as it was built.</summary>
        public IReadOnlyList<X509Certificate2> Chain { get; internal set; } = [];

        /// <summary>Gets a value indicating whether the chain ends at a trusted root.</summary>
        public bool Trusted { get; internal set; }

        /// <summary>Gets the signing time the dictionary claims (/M); not proven.</summary>
        public DateTimeOffset? ClaimedSigningTime { get; internal set; }

        /// <summary>Gets the time of a verified signature time-stamp, or of the document time-stamp.</summary>
        public DateTimeOffset? TimeStampTime { get; internal set; }

        /// <summary>Gets the certificate of the time-stamp authority.</summary>
        public X509Certificate2? TimeStampAuthority { get; internal set; }

        /// <summary>Gets the revocation status found for the chain.</summary>
        public PdfRevocationStatus Revocation { get; internal set; }

        /// <summary>Gets the first later change the signature's permissions or locks forbid, or null.</summary>
        public string? ForbiddenChange { get; internal set; }

        /// <summary>Gets the overall status.</summary>
        public PdfSignatureStatus Status { get; internal set; }

        /// <summary>Gets why the status is what it is, and other findings.</summary>
        public IReadOnlyList<string> Reasons => _reasons;

        internal readonly List<string> _reasons = [];
    }
}
