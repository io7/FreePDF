// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Security.Cryptography;
#if NET8_0_OR_GREATER
using System.Security.Cryptography.Pkcs;
#endif

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Adds a document time-stamp (ISO 32000-2 12.8.5, /SubFilter /ETSI.RFC3161): an RFC 3161 time-stamp token from a
    /// time-stamp authority over the bytes the signature covers. It shows that the document, with its signatures and
    /// its validation data, existed at that time; PAdES B-LTA adds one after the validation data (ETSI EN 319 142-1).
    /// Use it with <c>DigitalSignatureHandler.ForDocument</c> and no field name, certification or rectangle: a
    /// document time-stamp is an invisible new field. Needs .NET 8 or later.
    /// </summary>
    public class PdfTimeStampSigner : IDigitalSigner, IDigitalSignerFormat
    {
        /// <summary>
        /// Creates a signer that posts its requests to a time-stamp authority over HTTP.
        /// </summary>
        /// <param name="timeStampAuthorityUri">The RFC 3161 time-stamp authority.</param>
        /// <param name="digestType">The digest of the covered bytes that the authority time-stamps; SHA-256 or stronger.</param>
        public PdfTimeStampSigner(Uri timeStampAuthorityUri, PdfMessageDigestType digestType = PdfMessageDigestType.SHA256)
            : this(digestType)
        {
            TimeStampAuthorityUri = timeStampAuthorityUri ?? throw new ArgumentNullException(nameof(timeStampAuthorityUri));
        }

        /// <summary>
        /// Creates a signer that sends its requests through a function of your own, for example to add authentication or
        /// to use a test authority. The function gets a DER-encoded TimeStampReq and returns the DER-encoded
        /// TimeStampResp.
        /// </summary>
        /// <param name="timeStampClient">The function that sends the request and returns the reply.</param>
        /// <param name="digestType">The digest of the covered bytes that the authority time-stamps; SHA-256 or stronger.</param>
        public PdfTimeStampSigner(Func<byte[], Task<byte[]>> timeStampClient, PdfMessageDigestType digestType = PdfMessageDigestType.SHA256)
            : this(digestType)
        {
            TimeStampClient = timeStampClient ?? throw new ArgumentNullException(nameof(timeStampClient));
        }

        PdfTimeStampSigner(PdfMessageDigestType digestType)
        {
#if !NET8_0_OR_GREATER
            throw new NotSupportedException("Document time-stamps need .NET 8 or later.");
#else
            if (digestType is not (PdfMessageDigestType.SHA256 or PdfMessageDigestType.SHA384 or PdfMessageDigestType.SHA512))
                throw new ArgumentException("Document time-stamps need SHA-256 or a stronger digest.", nameof(digestType));
            DigestType = digestType;
#endif
        }

        /// <summary>
        /// Gets <see cref="PdfSignatureFormat.DocumentTimeStamp"/>.
        /// </summary>
        public PdfSignatureFormat Format => PdfSignatureFormat.DocumentTimeStamp;

        /// <summary>
        /// Gets a name for the time-stamp. A document time-stamp has no appearance, so it is not shown.
        /// </summary>
        public string CertificateName => "Document time-stamp";

        /// <summary>
        /// Gets the number of bytes reserved for the authority's token; set it when creating the signer. The default,
        /// 16384, holds a token with its authority's certificate chain. Saving fails if a token is larger.
        /// </summary>
        public int TimeStampReserve
        {
            get => _timeStampReserve;
            init => _timeStampReserve = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }
        readonly int _timeStampReserve = 16384;

        /// <summary>
        /// Gets the size reserved for the token.
        /// </summary>
        public Task<int> GetSignatureSizeAsync() => Task.FromResult(TimeStampReserve);

        /// <summary>
        /// Gets a time-stamp token over the bytes of the stream, the bytes the document time-stamp covers.
        /// </summary>
        public async Task<byte[]> GetSignatureAsync(Stream stream)
        {
#if !NET8_0_OR_GREATER
            await Task.CompletedTask.ConfigureAwait(false);
            throw new NotSupportedException("Document time-stamps need .NET 8 or later.");
#else
            stream.Position = 0;
            var (hash, algorithm) = DigestType switch
            {
                PdfMessageDigestType.SHA256 => (SHA256.HashData(stream), HashAlgorithmName.SHA256),
                PdfMessageDigestType.SHA384 => (SHA384.HashData(stream), HashAlgorithmName.SHA384),
                _ => (SHA512.HashData(stream), HashAlgorithmName.SHA512),
            };
            // A nonce pairs the request with the reply; ProcessResponse checks it and the message imprint.
            var nonce = RandomNumberGenerator.GetBytes(8);
            var request = Rfc3161TimestampRequest.CreateFromHash(hash, algorithm, nonce: nonce, requestSignerCertificates: true);

            var reply = TimeStampClient is not null
                ? await TimeStampClient(request.Encode()).ConfigureAwait(false)
                : await TimeStampAuthority.PostAsync(TimeStampAuthorityUri!, request.Encode()).ConfigureAwait(false);
            var token = request.ProcessResponse(reply, out _).AsSignedCms().Encode();
            if (token.Length > TimeStampReserve)
            {
                throw new InvalidOperationException($"The time-stamp authority's token needs {token.Length} bytes, more than the " +
                    $"{TimeStampReserve} reserved for it. Create the signer with a larger {nameof(TimeStampReserve)}.");
            }
            return token;
#endif
        }

        PdfMessageDigestType DigestType { get; }

        Uri? TimeStampAuthorityUri { get; }

        Func<byte[], Task<byte[]>>? TimeStampClient { get; }
    }
}
