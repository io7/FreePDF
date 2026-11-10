// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Formats.Asn1;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Signer implementation that uses .NET classes only.
    /// FreePDF: <see cref="Format"/> selects a PAdES baseline signature, and <see cref="TimeStampClient"/> replaces the
    /// HTTP call to the time-stamp authority. Time-stamps need .NET 8 or later.
    /// </summary>
    public class PdfSharpDefaultSigner : IDigitalSigner, IDigitalSignerFormat
    {
        static readonly Oid SignatureTimeStampOid = new("1.2.840.113549.1.9.16.2.14");
        // id-aa-signingCertificateV2 (RFC 5035).
        static readonly Oid SigningCertificateV2Oid = new("1.2.840.113549.1.9.16.2.47");
        const string TimestampQueryContentType = "application/timestamp-query";
        const string TimestampReplyContentType = "application/timestamp-reply";

        /// <summary>
        /// Creates a new instance of the PdfSharpDefaultSigner class.
        /// </summary>
        /// <param name="certificate">The signer's certificate, with its private key.</param>
        /// <param name="digestType">The message digest algorithm.</param>
        /// <param name="timeStampAuthorityUri">The RFC 3161 time-stamp authority to post requests to, or null.</param>
        public PdfSharpDefaultSigner(X509Certificate2 certificate, PdfMessageDigestType digestType, Uri? timeStampAuthorityUri = null)
        {
            Certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
            DigestType = digestType;
#if !NET8_0_OR_GREATER
            // The RFC 3161 types of System.Security.Cryptography.Pkcs exist only for .NET 8 and later here.
            if (timeStampAuthorityUri != null)
                throw new ArgumentException(nameof(timeStampAuthorityUri) + " must be null when using .NET Framework or .NET Standard.");
#endif
            TimeStampAuthorityUri = timeStampAuthorityUri;
        }

        /// <summary>
        /// Gets the name of the certificate.
        /// </summary>
        public string CertificateName => Certificate.GetNameInfo(X509NameType.SimpleName, false) ?? "(undefined)";

        /// <summary>
        /// Gets or sets the format of the signatures. The default, <see cref="PdfSignatureFormat.Pkcs7Detached"/>, writes
        /// the CMS as before 7.10. <see cref="PdfSignatureFormat.Pades"/> writes a PAdES baseline signature
        /// (ETSI.CAdES.detached): B-B, or B-T with a time-stamp authority.
        /// </summary>
        public PdfSignatureFormat Format { get; init; } = PdfSignatureFormat.Pkcs7Detached;

        /// <summary>
        /// Gets or sets the function that sends an RFC 3161 time-stamp request (a DER-encoded TimeStampReq) to a
        /// time-stamp authority and returns its reply (a DER-encoded TimeStampResp). When it is set, the signature gets
        /// a signature time-stamp from it instead of an HTTP POST to the URI given to the constructor, for example to
        /// add authentication or to use a test authority. Time-stamps need .NET 8 or later.
        /// </summary>
        public Func<byte[], Task<byte[]>>? TimeStampClient { get; init; }

        /// <summary>
        /// Determines the size of the contents to be reserved in the PDF file for the signature.
        /// </summary>
        public async Task<int> GetSignatureSizeAsync()
        {
            if (_signatureSize == 0)
            {
                // The signature size varies depending on the length of the digest
                // and the size of the certificate.
                // We simply calculate it once by signing an empty stream.
                _signatureSize = (await GetSignatureAsync(new MemoryStream([0])).ConfigureAwait(false)).Length;
                if (MustAddTimeStamp)
                {
                    // Add arbitrary padding because TSA timestamp response’s length seems to vary from one call to another by 1 byte.
                    _signatureSize += 10; // 2 was found to be too small. Make it 10 to allow for some DSA variation.
                }
                else
                {
                    // With DSA, signature size varies without timestamp. A variation of 2 bytes was observed. Make it 4 to allow more variation.
                    _signatureSize += 4;
                }
            }
            return _signatureSize;
        }
        int _signatureSize;

        /// <summary>
        /// Creates the signature for a stream containing the PDF file.
        /// </summary>
        /// <param name="stream"></param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<byte[]> GetSignatureAsync(Stream stream)
        {
            var content = new byte[stream.Length];
            stream.Position = 0;
            var read = 0;
            while (read < content.Length)
            {
                // A stream may return a partial buffer before reaching EOF.
                // ReSharper disable once MethodHasAsyncOverload
                var count = stream.Read(content, read, content.Length - read);
                if (count == 0)
                    throw new InvalidOperationException($"Tried to read {content.Length} bytes but got {read} bytes.");
                read += count;
            }

            // Sign the byte range.
            var contentInfo = new ContentInfo(content);
            var signedCms = new SignedCms(contentInfo, true);
            var signer = new CmsSigner(Certificate) { DigestAlgorithm = DigestOid() };
            if (Format == PdfSignatureFormat.Pades)
            {
                // PAdES baseline (ETSI EN 319 142-1 5.2): signing-certificate-v2 binds the signer's certificate; there is
                // no signing-time attribute, the claimed signing time is the signature dictionary's /M. SignedCms adds
                // content-type and message-digest because there are signed attributes.
                signer.SignedAttributes.Add(SigningCertificateV2(Certificate));
            }
            else
            {
                signer.UnsignedAttributes.Add(new Pkcs9SigningTime());
            }

            signedCms.ComputeSignature(signer, true);

            if (MustAddTimeStamp)
                await AddTimeStampAsync(signedCms).ConfigureAwait(false);
            return signedCms.Encode();
        }

        X509Certificate2 Certificate { get; }

        PdfMessageDigestType DigestType { get; }

        Uri? TimeStampAuthorityUri { get; }

        bool MustAddTimeStamp => TimeStampAuthorityUri is not null || TimeStampClient is not null;

        Oid DigestOid() => DigestType switch
        {
            PdfMessageDigestType.SHA1 => Oid.FromFriendlyName("sha1", OidGroup.HashAlgorithm),
            PdfMessageDigestType.SHA256 => Oid.FromFriendlyName("sha256", OidGroup.HashAlgorithm),
            PdfMessageDigestType.SHA384 => Oid.FromFriendlyName("sha384", OidGroup.HashAlgorithm),
            PdfMessageDigestType.SHA512 => Oid.FromFriendlyName("sha512", OidGroup.HashAlgorithm),
            _ => throw new NotSupportedException($"Digest type {DigestType} not supported by this signer.")
        };

        HashAlgorithmName HashAlgorithm() => DigestType switch
        {
            PdfMessageDigestType.SHA1 => HashAlgorithmName.SHA1,
            PdfMessageDigestType.SHA256 => HashAlgorithmName.SHA256,
            PdfMessageDigestType.SHA384 => HashAlgorithmName.SHA384,
            PdfMessageDigestType.SHA512 => HashAlgorithmName.SHA512,
            _ => throw new NotSupportedException($"Digest type {DigestType} not supported by this signer.")
        };

        /// <summary>
        /// The signing-certificate-v2 attribute (RFC 5035 5.4): one ESSCertIDv2 with the SHA-256 hash of the certificate
        /// (the default algorithm, so it is not written) and the issuer and serial number.
        /// </summary>
        static AsnEncodedData SigningCertificateV2(X509Certificate2 certificate)
        {
            byte[] hash;
            using (var sha256 = SHA256.Create())
                hash = sha256.ComputeHash(certificate.RawData);
            var serial = certificate.GetSerialNumber();
            Array.Reverse(serial);

            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())           // SigningCertificateV2
            using (writer.PushSequence())           // certs: SEQUENCE OF ESSCertIDv2
            using (writer.PushSequence())           // ESSCertIDv2
            {
                writer.WriteOctetString(hash);
                using (writer.PushSequence())       // issuerSerial: IssuerSerial
                {
                    using (writer.PushSequence())   // issuer: GeneralNames
                    {
                        // directoryName [4] Name; Name is a CHOICE, so the tag is explicit.
                        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true)))
                            writer.WriteEncodedValue(certificate.IssuerName.RawData);
                    }
                    writer.WriteInteger(serial);
                }
            }
            return new AsnEncodedData(SigningCertificateV2Oid, writer.Encode());
        }

        /// <summary>
        /// Adds a signature time-stamp (RFC 3161, the signature-time-stamp unsigned attribute of RFC 5126 / EN 319 122-1)
        /// over the signature value.
        /// </summary>
        async Task AddTimeStampAsync(SignedCms signedCms)
        {
#if !NET8_0_OR_GREATER
            await Task.CompletedTask.ConfigureAwait(false);
            throw new NotSupportedException("Signature time-stamps need .NET 8 or later.");
#else
            // A nonce pairs the request with the reply.
            var nonce = new byte[8];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(nonce);

            var signerInfo = signedCms.SignerInfos[0];
            var request = Rfc3161TimestampRequest.CreateFromSignerInfo(
                signerInfo,
                HashAlgorithm(),
                requestSignerCertificates: true, // Ask the TSA to embed its signing certificate in the token.
                nonce: nonce);

            byte[] reply;
            if (TimeStampClient is not null)
                reply = await TimeStampClient(request.Encode()).ConfigureAwait(false);
            else
                reply = await PostToAuthorityAsync(request.Encode()).ConfigureAwait(false);
            var token = request.ProcessResponse(reply, out _);

            // The time-stamp token goes into the signer's unsigned attributes.
            signerInfo.AddUnsignedAttribute(new AsnEncodedData(SignatureTimeStampOid, token.AsSignedCms().Encode()));
#endif
        }

        async Task<byte[]> PostToAuthorityAsync(byte[] request)
        {
            using var client = new HttpClient();
            using var content = new ByteArrayContent(request);
            content.Headers.ContentType = new MediaTypeHeaderValue(TimestampQueryContentType);
            using var response = await client.PostAsync(TimeStampAuthorityUri, content).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new CryptographicException(
                    $"There was an error from the timestamp authority. It responded with {response.StatusCode} {(int)response.StatusCode}.");
            }
            if (response.Content.Headers.ContentType?.MediaType != TimestampReplyContentType)
                throw new CryptographicException("The reply from the time stamp server was in an invalid format.");

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
    }
}
