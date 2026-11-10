// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Formats.Asn1;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Pdf.Advanced;

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Options for <see cref="PdfDocumentSecurityStore.AddValidationDataAsync"/>.
    /// </summary>
    public sealed class PdfValidationDataOptions
    {
        /// <summary>
        /// Gets the client for OCSP requests and CRL downloads; set it to add a proxy or authentication, or to answer
        /// the requests yourself. The default is a shared client with a 30-second timeout.
        /// </summary>
        public HttpClient? HttpClient { get; init; }

        /// <summary>
        /// Gets certificates the signatures do not carry, such as intermediate and root certificates, to complete the
        /// chains with.
        /// </summary>
        public IEnumerable<X509Certificate2>? ExtraCertificates { get; init; }
    }

    /// <summary>
    /// What <see cref="PdfDocumentSecurityStore.AddValidationDataAsync"/> found and added.
    /// </summary>
    public sealed class PdfValidationDataReport
    {
        internal PdfValidationDataReport(int added, IReadOnlyList<X509Certificate2> certificates, int ocspResponses, int crls,
            IReadOnlyList<string> problems)
        {
            Added = added;
            Certificates = certificates;
            OcspResponses = ocspResponses;
            Crls = crls;
            Problems = problems;
        }

        /// <summary>
        /// Gets the number of items added to the DSS; items it already held are not counted.
        /// </summary>
        public int Added { get; }

        /// <summary>
        /// Gets the certificates of the signatures' and time-stamps' chains.
        /// </summary>
        public IReadOnlyList<X509Certificate2> Certificates { get; }

        /// <summary>
        /// Gets the number of OCSP responses obtained.
        /// </summary>
        public int OcspResponses { get; }

        /// <summary>
        /// Gets the number of CRLs obtained.
        /// </summary>
        public int Crls { get; }

        /// <summary>
        /// Gets what could not be done: a chain that ends below a root, a certificate without revocation data, a
        /// failed request, or a certificate that OCSP reports as revoked. Empty when every certificate below a root has
        /// revocation data.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }
    }

    static partial class PdfDocumentSecurityStore
    {
        /// <summary>
        /// Collects validation data for the document's signatures and document time-stamps and adds it to the DSS
        /// (PAdES B-LT, ETSI EN 319 142-1): the certificates of their chains, and for each certificate below a root an
        /// OCSP response (RFC 6960) from the responder its certificate names, or else the CRLs (RFC 5280) of its
        /// distribution points. The certificates come from the signatures, their signature time-stamps and
        /// <see cref="PdfValidationDataOptions.ExtraCertificates"/>. Open the document in Append mode and save it after.
        /// The responses are not checked: their signatures and the certificates' status are for the validator to judge.
        /// </summary>
        public static async Task<PdfValidationDataReport> AddValidationDataAsync(PdfDocument document, PdfValidationDataOptions? options = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            options ??= new PdfValidationDataOptions();
            var http = options.HttpClient ?? SharedHttp;
            var problems = new List<string>();

            // The certificates the CMS structures carry, and the ones that signed them.
            var available = new List<X509Certificate2>();
            var signers = new List<X509Certificate2>();
            foreach (var contents in SignatureContents(document))
                ReadCms(contents, available, signers);
            if (options.ExtraCertificates != null)
                available.AddRange(options.ExtraCertificates);

            // Each signer's chain, and the certificate that issued each certificate in it.
            var chainCertificates = new List<X509Certificate2>();
            var issuers = new Dictionary<string, X509Certificate2>(StringComparer.Ordinal);
            foreach (var signer in signers)
            {
                var chain = BuildChain(signer, available);
                for (int index = 0; index < chain.Count; index++)
                {
                    if (!chainCertificates.Any(c => c.RawData.AsSpan().SequenceEqual(chain[index].RawData)))
                        chainCertificates.Add(chain[index]);
                    if (index + 1 < chain.Count)
                        issuers[chain[index].Thumbprint] = chain[index + 1];
                }
                var last = chain[chain.Count - 1];
                if (!IsSelfIssued(last))
                    problems.Add($"The chain of '{signer.Subject}' ends at '{last.Subject}', which is not a root; add its issuer to ExtraCertificates.");
            }

            var ocspResponses = new List<byte[]>();
            var crls = new List<byte[]>();
            var extraCertificates = new List<X509Certificate2>();
            foreach (var certificate in chainCertificates)
            {
                if (IsSelfIssued(certificate) || !issuers.TryGetValue(certificate.Thumbprint, out var issuer))
                    continue;
                var ocsp = await FetchOcspAsync(http, certificate, issuer, problems).ConfigureAwait(false);
                if (ocsp != null)
                {
                    ocspResponses.Add(ocsp.Value.Response);
                    extraCertificates.AddRange(ocsp.Value.ResponderCertificates);
                    continue;
                }
                var fetched = await FetchCrlsAsync(http, certificate, problems).ConfigureAwait(false);
                if (fetched.Count == 0)
                {
                    problems.Add($"No revocation data for '{certificate.Subject}'.");
                    continue;
                }
                foreach (var crl in fetched)
                {
                    if (!crls.Any(c => c.AsSpan().SequenceEqual(crl)))
                        crls.Add(crl);
                }
            }

            var allCertificates = chainCertificates.Concat(extraCertificates).Select(c => c.RawData);
            int added = Add(document, allCertificates, ocspResponses, crls);
            return new PdfValidationDataReport(added, chainCertificates, ocspResponses.Count, crls.Count, problems);
        }

        // One client for all requests: no new connection per request, and a bounded wait.
        static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

        const string SignatureTimeStampOid = "1.2.840.113549.1.9.16.2.14";
        const string AuthorityInfoAccessOid = "1.3.6.1.5.5.7.1.1";
        const string OcspAccessMethodOid = "1.3.6.1.5.5.7.48.1";
        const string CrlDistributionPointsOid = "2.5.29.31";
        const string OcspBasicResponseOid = "1.3.6.1.5.5.7.48.1.1";
        const string Sha1Oid = "1.3.14.3.2.26";

        // The /Contents of every signature and document time-stamp in the form: field values with /ByteRange.
        static List<byte[]> SignatureContents(PdfDocument document)
        {
            var result = new List<byte[]>();
            var visited = new HashSet<PdfDictionary>();
            var pending = new Stack<PdfItem>();
            if (document.Catalog.Elements.GetDictionary("/AcroForm")?.Elements.GetArray("/Fields") is { } fields)
            {
                foreach (var field in fields.Elements)
                    pending.Push(field);
            }
            while (pending.Count > 0)
            {
                if (Resolve(pending.Pop()) is not PdfDictionary field || !visited.Add(field))
                    continue;
                if (Resolve(field.Elements["/V"]) is PdfDictionary value && value.Elements.ContainsKey("/ByteRange")
                    && Resolve(value.Elements["/Contents"]) is PdfString contents)
                {
                    result.Add(contents.GetRawBytes());
                }
                if (field.Elements.GetArray("/Kids") is { } kids)
                {
                    foreach (var kid in kids.Elements)
                        pending.Push(kid);
                }
            }
            return result;
        }

        static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

        // The certificates of a signature's CMS and of its signature time-stamps; the signers of both. A document
        // time-stamp's CMS is the token itself.
        static void ReadCms(byte[] contents, List<X509Certificate2> available, List<X509Certificate2> signers)
        {
            SignedCms cms;
            try
            {
                // /Contents is padded with zeros after the DER value.
                var length = new AsnReader(contents, AsnEncodingRules.BER).ReadEncodedValue().Length;
                cms = new SignedCms();
                cms.Decode(contents.AsSpan(0, length).ToArray());
            }
            catch (Exception ex) when (ex is CryptographicException or AsnContentException)
            {
                return;   // Not a CMS signature FreePDF can read; the validator will say so.
            }
            available.AddRange(cms.Certificates.Cast<X509Certificate2>());
            foreach (var signerInfo in cms.SignerInfos)
            {
                if (signerInfo.Certificate is { } certificate)
                    signers.Add(certificate);
                foreach (var attribute in signerInfo.UnsignedAttributes)
                {
                    if (attribute.Oid.Value != SignatureTimeStampOid)
                        continue;
                    foreach (var value in attribute.Values)
                        ReadCms(value.RawData, available, signers);
                }
            }
        }

        // The certificate, its issuer, and so on up to a self-issued certificate or as far as the certificates go.
        static List<X509Certificate2> BuildChain(X509Certificate2 leaf, List<X509Certificate2> available)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
#if NET5_0_OR_GREATER
            chain.ChainPolicy.DisableCertificateDownloads = true;
#endif
            foreach (var certificate in available)
                chain.ChainPolicy.ExtraStore.Add(certificate);
            chain.Build(leaf);
            var result = chain.ChainElements.Cast<X509ChainElement>().Select(element => LoadCertificate(element.Certificate.RawData)).ToList();
            return result.Count > 0 ? result : [leaf];
        }

        static X509Certificate2 LoadCertificate(byte[] der)
#if NET9_0_OR_GREATER
            => X509CertificateLoader.LoadCertificate(der);
#else
            => new(der);
#endif

        static bool IsSelfIssued(X509Certificate2 certificate)
            => certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

        static async Task<(byte[] Response, List<X509Certificate2> ResponderCertificates)?> FetchOcspAsync(HttpClient http,
            X509Certificate2 certificate, X509Certificate2 issuer, List<string> problems)
        {
            var urls = AccessLocations(certificate, OcspAccessMethodOid);
            if (urls.Count == 0)
                return null;
            var request = OcspRequest(certificate, issuer, out var certId);
            foreach (var url in urls)
            {
                try
                {
                    using var content = new ByteArrayContent(request);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/ocsp-request");
                    using var response = await http.PostAsync(url, content).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        problems.Add($"The OCSP responder {url} answered {(int)response.StatusCode} for '{certificate.Subject}'.");
                        continue;
                    }
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var status = ReadOcspResponse(bytes, certId, out var responderCertificates, out var error);
                    if (status == null)
                    {
                        problems.Add($"The OCSP responder {url} gave no usable answer for '{certificate.Subject}': {error}.");
                        continue;
                    }
                    if (status == OcspCertStatus.Revoked)
                        problems.Add($"The OCSP responder {url} reports '{certificate.Subject}' as revoked.");
                    if (status == OcspCertStatus.Unknown)
                    {
                        problems.Add($"The OCSP responder {url} does not know '{certificate.Subject}'.");
                        continue;
                    }
                    return (bytes, responderCertificates);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    problems.Add($"The OCSP request to {url} for '{certificate.Subject}' failed: {ex.Message}");
                }
            }
            return null;
        }

        static async Task<List<byte[]>> FetchCrlsAsync(HttpClient http, X509Certificate2 certificate, List<string> problems)
        {
            var result = new List<byte[]>();
            foreach (var url in CrlLocations(certificate))
            {
                try
                {
                    var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                    // A CRL is a DER SEQUENCE (RFC 5280 5.1); a PEM or an HTML error page is not.
                    if (bytes.Length > 1 && bytes[0] == 0x30)
                    {
                        result.Add(bytes);
                        break;
                    }
                    problems.Add($"The CRL at {url} for '{certificate.Subject}' is not DER-encoded.");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    problems.Add($"The CRL download from {url} for '{certificate.Subject}' failed: {ex.Message}");
                }
            }
            return result;
        }

        // The HTTP locations of an access method in the authority information access extension (RFC 5280 4.2.2.1).
        static List<Uri> AccessLocations(X509Certificate2 certificate, string accessMethod)
        {
            var result = new List<Uri>();
            if (certificate.Extensions[AuthorityInfoAccessOid] is not { } extension)
                return result;
            try
            {
                var descriptions = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
                while (descriptions.HasData)
                {
                    var description = descriptions.ReadSequence();
                    var method = description.ReadObjectIdentifier();
                    var location = description.ReadEncodedValue();
                    if (method == accessMethod && HttpUri(location.Span) is { } uri)
                        result.Add(uri);
                }
            }
            catch (AsnContentException)
            {
            }
            return result;
        }

        // The HTTP locations of the full names in the CRL distribution points extension (RFC 5280 4.2.1.13).
        static List<Uri> CrlLocations(X509Certificate2 certificate)
        {
            var result = new List<Uri>();
            if (certificate.Extensions[CrlDistributionPointsOid] is not { } extension)
                return result;
            try
            {
                var points = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
                while (points.HasData)
                {
                    var point = points.ReadSequence();
                    // distributionPoint [0] DistributionPointName; fullName [0] GeneralNames.
                    if (!point.HasData || !point.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                        continue;
                    var name = point.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                    if (!name.HasData || !name.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                        continue;
                    var generalNames = name.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                    while (generalNames.HasData)
                    {
                        if (HttpUri(generalNames.ReadEncodedValue().Span) is { } uri)
                            result.Add(uri);
                    }
                }
            }
            catch (AsnContentException)
            {
            }
            return result;
        }

        // A GeneralName that is an http or https uniformResourceIdentifier ([6] IA5String).
        static Uri? HttpUri(ReadOnlySpan<byte> generalName)
        {
            var reader = new AsnReader(generalName.ToArray(), AsnEncodingRules.DER);
            if (!reader.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 6)))
                return null;
            var text = reader.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 6));
            return System.Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") ? uri : null;
        }

        // An OCSPRequest (RFC 6960 4.1.1) for one certificate, without a nonce or a signature, as RFC 5019 profiles it.
        // The CertID uses SHA-1, which every responder accepts.
        static byte[] OcspRequest(X509Certificate2 certificate, X509Certificate2 issuer, out byte[] certId)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())                   // CertID
            {
                using (writer.PushSequence())               // hashAlgorithm
                {
                    writer.WriteObjectIdentifier(Sha1Oid);
                    writer.WriteNull();
                }
                using (var sha1 = SHA1.Create())
                {
                    writer.WriteOctetString(sha1.ComputeHash(issuer.SubjectName.RawData));
                    // The hash of the issuer's subjectPublicKey BIT STRING value.
                    writer.WriteOctetString(sha1.ComputeHash(issuer.PublicKey.EncodedKeyValue.RawData));
                }
                // GetSerialNumber is little-endian two's complement.
                writer.WriteInteger(new BigInteger(certificate.GetSerialNumber()));
            }
            certId = writer.Encode();

            var request = new AsnWriter(AsnEncodingRules.DER);
            using (request.PushSequence())                  // OCSPRequest
            using (request.PushSequence())                  // tbsRequest
            using (request.PushSequence())                  // requestList
            using (request.PushSequence())                  // Request
                request.WriteEncodedValue(certId);
            return request.Encode();
        }

        enum OcspCertStatus { Good, Revoked, Unknown }

        // The status of the requested certificate in an OCSPResponse (RFC 6960 4.2.1), and the certificates the basic
        // response carries; null if the response is not successful, not a basic response, or has no answer for it.
        static OcspCertStatus? ReadOcspResponse(byte[] der, byte[] certId, out List<X509Certificate2> certificates, out string error)
        {
            certificates = [];
            error = "";
            try
            {
                var response = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
                var status = response.ReadEnumeratedBytes().ToArray();
                if (status.Length != 1 || status[0] != 0)
                {
                    error = $"response status {(status.Length == 1 ? status[0] : -1)}";
                    return null;
                }
                var responseBytes = response.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
                if (responseBytes.ReadObjectIdentifier() != OcspBasicResponseOid)
                {
                    error = "not a basic OCSP response";
                    return null;
                }
                var basic = new AsnReader(responseBytes.ReadOctetString(), AsnEncodingRules.DER).ReadSequence();
                var data = basic.ReadSequence();
                basic.ReadSequence();          // signatureAlgorithm
                basic.ReadBitString(out _);    // signature
                if (basic.HasData && basic.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    var certs = basic.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
                    while (certs.HasData)
                        certificates.Add(LoadCertificate(certs.ReadEncodedValue().ToArray()));
                }

                if (data.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                    data.ReadEncodedValue();   // version
                data.ReadEncodedValue();       // responderID
                data.ReadGeneralizedTime();    // producedAt
                var responses = data.ReadSequence();
                while (responses.HasData)
                {
                    var single = responses.ReadSequence();
                    var id = single.ReadEncodedValue();
                    if (!SameCertId(id.Span, certId))
                        continue;
                    var tag = single.PeekTag();
                    if (tag.TagClass != TagClass.ContextSpecific)
                        break;
                    return tag.TagValue switch
                    {
                        0 => OcspCertStatus.Good,
                        1 => OcspCertStatus.Revoked,
                        _ => OcspCertStatus.Unknown,
                    };
                }
                error = "no answer for the certificate";
                return null;
            }
            catch (Exception ex) when (ex is AsnContentException or CryptographicException)
            {
                error = "not a DER-encoded OCSP response";
                return null;
            }
        }

        // The responder may encode the CertID's AlgorithmIdentifier without NULL parameters; the hashes and the serial
        // number decide.
        static bool SameCertId(ReadOnlySpan<byte> answered, byte[] requested)
        {
            if (answered.SequenceEqual(requested))
                return true;
            var a = new AsnReader(answered.ToArray(), AsnEncodingRules.DER).ReadSequence();
            var b = new AsnReader(requested, AsnEncodingRules.DER).ReadSequence();
            if (a.ReadSequence().ReadObjectIdentifier() != b.ReadSequence().ReadObjectIdentifier())
                return false;
            return a.ReadOctetString().AsSpan().SequenceEqual(b.ReadOctetString())
                && a.ReadOctetString().AsSpan().SequenceEqual(b.ReadOctetString())
                && a.ReadInteger() == b.ReadInteger();
        }
    }
}
