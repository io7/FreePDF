// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Formats.Asn1;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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
        /// Gets the client for OCSP requests and CRL downloads; set it to add a proxy or authentication, to restrict
        /// where requests may go, or to answer them yourself. Its handler then decides which addresses it reaches.
        /// The default is a shared client with a 30-second timeout that only reaches public addresses, unless
        /// <see cref="AllowPrivateNetworks"/> is set.
        /// </summary>
        public HttpClient? HttpClient { get; init; }

        /// <summary>
        /// Gets certificates the signatures do not carry, such as intermediate and root certificates, to complete the
        /// chains with.
        /// </summary>
        public IEnumerable<X509Certificate2>? ExtraCertificates { get; init; }

        /// <summary>
        /// Gets a value indicating whether the default client may reach loopback, link-local and private network
        /// addresses, for example an internal CA's responder. The addresses come from the certificates in the
        /// document, which anyone who signed it chose; leave it false for documents from others.
        /// </summary>
        public bool AllowPrivateNetworks { get; init; }

        /// <summary>
        /// Gets the largest OCSP response or CRL accepted, in bytes. The default is 32 MiB.
        /// </summary>
        public int MaxResponseSize
        {
            get => _maxResponseSize;
            init => _maxResponseSize = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }
        readonly int _maxResponseSize = 32 * 1024 * 1024;
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
        /// failed or refused request, or a certificate that OCSP reports as revoked. Empty when every certificate below
        /// a root has revocation data.
        /// </summary>
        public IReadOnlyList<string> Problems { get; }
    }

    static partial class PdfDocumentSecurityStore
    {
        /// <summary>
        /// Collects validation data for the document's signatures and document time-stamps and adds it to the DSS
        /// (PAdES B-LT, ETSI EN 319 142-1): the certificates of their chains, and for each certificate below a root an
        /// OCSP response (RFC 6960) from the responder its certificate names, or else a CRL (RFC 5280) from each of its
        /// distribution points. The certificates come from the signatures, their signature time-stamps and
        /// <see cref="PdfValidationDataOptions.ExtraCertificates"/>. Open the document in Append mode and save it after.
        /// The responses are not verified: their signatures and the certificates' status are for the validator to
        /// judge. The addresses asked come from the certificates; see <see cref="PdfValidationDataOptions"/>.
        /// </summary>
        public static async Task<PdfValidationDataReport> AddValidationDataAsync(PdfDocument document, PdfValidationDataOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            options ??= new PdfValidationDataOptions();
            var fetcher = new Fetcher(options, cancellationToken);

            // The certificates the CMS structures carry, and the ones that signed them.
            var available = new List<X509Certificate2>();
            var signers = new List<X509Certificate2>();
            foreach (var contents in SignatureContents(document))
                ReadCms(contents, available, signers, 0);
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
                    fetcher.Problems.Add($"The chain of '{signer.Subject}' ends at '{last.Subject}', which is not a root; add its issuer to ExtraCertificates.");
            }

            var ocspResponses = new List<byte[]>();
            var crls = new List<byte[]>();
            var responderCertificates = new List<X509Certificate2>();
            foreach (var certificate in chainCertificates)
            {
                if (IsSelfIssued(certificate) || !issuers.TryGetValue(certificate.Thumbprint, out var issuer))
                    continue;
                var ocsp = await fetcher.OcspAsync(certificate, issuer).ConfigureAwait(false);
                if (ocsp != null)
                {
                    ocspResponses.Add(ocsp.Value.Response);
                    responderCertificates.AddRange(ocsp.Value.ResponderCertificates);
                    continue;
                }
                var fetched = await fetcher.CrlsAsync(certificate).ConfigureAwait(false);
                if (fetched.Count == 0)
                {
                    fetcher.Problems.Add($"No revocation data for '{certificate.Subject}'.");
                    continue;
                }
                foreach (var crl in fetched)
                {
                    if (!crls.Any(c => c.AsSpan().SequenceEqual(crl)))
                        crls.Add(crl);
                }
            }

            var allCertificates = chainCertificates.Concat(responderCertificates).Select(c => c.RawData);
            int added = Add(document, allCertificates, ocspResponses, crls);
            return new PdfValidationDataReport(added, chainCertificates, ocspResponses.Count, crls.Count, fetcher.Problems);
        }

        const string SignatureTimeStampOid = "1.2.840.113549.1.9.16.2.14";
        const string AuthorityInfoAccessOid = "1.3.6.1.5.5.7.1.1";
        const string OcspAccessMethodOid = "1.3.6.1.5.5.7.48.1";
        const string CrlDistributionPointsOid = "2.5.29.31";
        const string OcspBasicResponseOid = "1.3.6.1.5.5.7.48.1.1";
        const string Sha1Oid = "1.3.14.3.2.26";

        // At most this many addresses of one kind are asked per certificate; a certificate may list any number.
        const int MaxUrlsPerCertificate = 4;

        // A signature's time-stamp is a CMS inside the CMS; deeper nesting is not followed.
        const int MaxCmsDepth = 3;

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
        static void ReadCms(byte[] contents, List<X509Certificate2> available, List<X509Certificate2> signers, int depth)
        {
            if (depth > MaxCmsDepth)
                return;
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
                        ReadCms(value.RawData, available, signers, depth + 1);
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

        internal static X509Certificate2 LoadCertificate(byte[] der)
#if NET9_0_OR_GREATER
            => X509CertificateLoader.LoadCertificate(der);
#else
            => new(der);
#endif

        static bool IsSelfIssued(X509Certificate2 certificate)
            => certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

        // The HTTP requests of one call: the client, the limits, the problems found.
        internal sealed class Fetcher(PdfValidationDataOptions options, CancellationToken cancellationToken)
        {
            internal List<string> Problems { get; } = [];

            readonly HttpClient _http = options.HttpClient ?? (options.AllowPrivateNetworks ? AnyAddressClient.Value : PublicAddressClient.Value);
            // A client of the caller's decides where it goes; the default one only goes to public addresses unless allowed.
            readonly bool _checkAddresses = options.HttpClient == null && !options.AllowPrivateNetworks;
            readonly Dictionary<string, byte[]> _crls = new(StringComparer.Ordinal);

            internal async Task<(byte[] Response, List<X509Certificate2> ResponderCertificates)?> OcspAsync(X509Certificate2 certificate,
                X509Certificate2 issuer)
            {
                var urls = AccessLocations(certificate, OcspAccessMethodOid);
                if (urls.Count == 0)
                    return null;
                var request = OcspRequest(certificate, issuer, out var certId);
                foreach (var url in urls)
                {
                    var bytes = await SendAsync(url, request, certificate, "OCSP request").ConfigureAwait(false);
                    if (bytes == null)
                        continue;
                    var status = ReadOcspResponse(bytes, certId, out var responderCertificates, out var error);
                    if (status == null)
                    {
                        Problems.Add($"The OCSP responder {url} gave no usable answer for '{certificate.Subject}': {error}.");
                        continue;
                    }
                    if (status == OcspCertStatus.Unknown)
                    {
                        Problems.Add($"The OCSP responder {url} does not know '{certificate.Subject}'.");
                        continue;
                    }
                    if (status == OcspCertStatus.Revoked)
                        Problems.Add($"The OCSP responder {url} reports '{certificate.Subject}' as revoked.");
                    return (bytes, responderCertificates);
                }
                return null;
            }

            // A CRL from each distribution point; the addresses of one point are alternatives.
            internal async Task<List<byte[]>> CrlsAsync(X509Certificate2 certificate)
            {
                var result = new List<byte[]>();
                foreach (var point in CrlDistributionPoints(certificate))
                {
                    foreach (var url in point.Urls)
                    {
                        // Certificates of one issuer usually share their CRL: it is fetched once per call.
                        if (!_crls.TryGetValue(url.AbsoluteUri, out var bytes))
                        {
                            bytes = await SendAsync(url, null, certificate, "CRL download").ConfigureAwait(false);
                            if (bytes != null)
                                _crls[url.AbsoluteUri] = bytes;
                        }
                        if (bytes == null)
                            continue;
                        if (!IsCrl(bytes, point.IndirectIssuer ? null : certificate.IssuerName.RawData))
                        {
                            Problems.Add($"The CRL at {url} for '{certificate.Subject}' is not a CRL of its issuer in DER.");
                            continue;
                        }
                        result.Add(bytes);
                        break;
                    }
                }
                return result;
            }

            // A POST of an OCSP request, or a GET; the body, or null after noting the problem. The whole exchange, body
            // included, has 30 seconds. A GET follows up to three redirects, each address checked like the first; a POST
            // follows none.
            async Task<byte[]?> SendAsync(Uri url, byte[]? ocspRequest, X509Certificate2 certificate, string what)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var target = url;
                try
                {
                    for (int hop = 0; ; hop++)
                    {
                        var connectTo = target;
                        if (_checkAddresses)
                        {
                            var address = await PublicAddressAsync(target).ConfigureAwait(false);
                            if (address == null)
                            {
                                Problems.Add($"The {what} to {target} for '{certificate.Subject}' was not made: it is not a public address " +
                                    "(set AllowPrivateNetworks, or pass an HttpClient, to reach it).");
                                return null;
                            }
#if !NET5_0_OR_GREATER
                            // Without a connection callback, the request goes to the address just checked, so that the host
                            // name cannot resolve to another one when the connection is made. HTTPS cannot be pinned so.
                            if (target.Scheme != "http")
                            {
                                Problems.Add($"The {what} to {target} for '{certificate.Subject}' was not made: on .NET Framework and .NET " +
                                    "Standard the default client checks addresses for HTTP only (set AllowPrivateNetworks, or pass an HttpClient).");
                                return null;
                            }
                            connectTo = new UriBuilder(target) { Host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString() }.Uri;
#endif
                        }
                        using var message = new HttpRequestMessage(ocspRequest == null ? HttpMethod.Get : HttpMethod.Post, connectTo);
                        if (!ReferenceEquals(connectTo, target))
                            message.Headers.Host = target.IsDefaultPort ? target.IdnHost : $"{target.IdnHost}:{target.Port}";
                        if (ocspRequest != null)
                        {
                            message.Content = new ByteArrayContent(ocspRequest);
                            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ocsp-request");
                        }
                        using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                        int status = (int)response.StatusCode;
                        if (status is 301 or 302 or 303 or 307 or 308 && ocspRequest == null && hop < 3
                            && response.Headers.Location is { } location
                            && Uri.TryCreate(target, location, out var next) && next.Scheme is "http" or "https")
                        {
                            target = next;
                            continue;
                        }
                        if (!response.IsSuccessStatusCode)
                        {
                            Problems.Add($"The {what} to {target} for '{certificate.Subject}' was answered with {status}.");
                            return null;
                        }
                        var body = await ReadLimitedAsync(response.Content, options.MaxResponseSize, timeout.Token).ConfigureAwait(false);
                        if (body == null)
                            Problems.Add($"The {what} to {target} for '{certificate.Subject}' returned more than {options.MaxResponseSize} bytes.");
                        return body;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException
                                               or IOException or SocketException)
                {
                    Problems.Add($"The {what} to {target} for '{certificate.Subject}' failed: " +
                        (ex is OperationCanceledException ? "no complete answer within 30 seconds." : ex.Message));
                    return null;
                }
            }

            static async Task<byte[]?> ReadLimitedAsync(HttpContent content, int limit, CancellationToken token)
            {
                if (content.Headers.ContentLength > limit)
                    return null;
                using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > limit)
                        return null;
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }

            // Every address the host has must be public: not loopback, link-local, private, shared, multicast or
            // reserved. The first one, or null.
            static async Task<IPAddress?> PublicAddressAsync(Uri url)
            {
                var addresses = IPAddress.TryParse(url.DnsSafeHost, out var literal)
                    ? [literal]
                    : await Dns.GetHostAddressesAsync(url.IdnHost).ConfigureAwait(false);
                return addresses.Length > 0 && addresses.All(IsPublicAddress) ? addresses[0] : null;
            }
        }

        static bool IsPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address))
                return false;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = address.GetAddressBytes();
                return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                         || (b[0] == 100 && (b[1] & 0xC0) == 64)      // 100.64.0.0/10, shared address space
                         || (b[0] == 169 && b[1] == 254)             // link-local
                         || (b[0] == 172 && (b[1] & 0xF0) == 16)     // 172.16.0.0/12
                         || (b[0] == 192 && b[1] == 168));           // 192.168.0.0/16
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = address.GetAddressBytes();
                return !(address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
                         || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);   // fc00::/7, unique local
            }
            return false;
        }

        // Shared clients that follow no redirects by themselves (SendAsync checks each hop). The public one also checks the
        // address it connects to (.NET 5 and later), so that a host name cannot resolve to a public address for the check
        // and a private one for the connection; it does not use a proxy, through which that cannot be seen.
        static readonly Lazy<HttpClient> PublicAddressClient = new(() => CreateClient(publicOnly: true));
        static readonly Lazy<HttpClient> AnyAddressClient = new(() => CreateClient(publicOnly: false));

        static HttpClient CreateClient(bool publicOnly)
        {
#if NET5_0_OR_GREATER
            var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) };
            if (publicOnly)
            {
                handler.UseProxy = false;
                handler.ConnectCallback = async (context, token) =>
                {
                    var addresses = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false))
                        .Where(IsPublicAddress).ToList();
                    if (addresses.Count == 0)
                        throw new HttpRequestException($"{context.DnsEndPoint.Host} has no public address.");
                    // Each public address in turn, as a normal connection would.
                    Exception? last = null;
                    foreach (var address in addresses)
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        try
                        {
                            await socket.ConnectAsync(address, context.DnsEndPoint.Port, token).ConfigureAwait(false);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (SocketException ex)
                        {
                            socket.Dispose();
                            last = ex;
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }
                    throw new HttpRequestException($"No public address of {context.DnsEndPoint.Host} could be reached.", last);
                };
            }
#else
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !publicOnly };
#endif
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
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
                while (descriptions.HasData && result.Count < MaxUrlsPerCertificate)
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

        // The distribution points of the CRL distribution points extension (RFC 5280 4.2.1.13) with HTTP full names; a
        // point that names a cRLIssuer is an indirect CRL, issued by another than the certificate's issuer.
        static List<(List<Uri> Urls, bool IndirectIssuer)> CrlDistributionPoints(X509Certificate2 certificate)
        {
            var result = new List<(List<Uri>, bool)>();
            if (certificate.Extensions[CrlDistributionPointsOid] is not { } extension)
                return result;
            try
            {
                var points = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
                while (points.HasData && result.Count < MaxUrlsPerCertificate)
                {
                    var point = points.ReadSequence();
                    var urls = new List<Uri>();
                    bool indirect = false;
                    while (point.HasData)
                    {
                        var tag = point.PeekTag();
                        // distributionPoint [0] DistributionPointName, with fullName [0] GeneralNames.
                        if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                        {
                            var name = point.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                            if (!name.HasData || !name.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                                continue;
                            var generalNames = name.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                            while (generalNames.HasData && urls.Count < MaxUrlsPerCertificate)
                            {
                                if (HttpUri(generalNames.ReadEncodedValue().Span) is { } uri)
                                    urls.Add(uri);
                            }
                        }
                        else
                        {
                            // reasons [1], cRLIssuer [2].
                            indirect |= tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2));
                            point.ReadEncodedValue();
                        }
                    }
                    if (urls.Count > 0)
                        result.Add((urls, indirect));
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
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") ? uri : null;
        }

        // A DER CertificateList (RFC 5280 5.1): tbsCertList, signatureAlgorithm and signatureValue, nothing after it;
        // its issuer, when given, must be the expected one.
        static bool IsCrl(byte[] der, byte[]? expectedIssuer)
        {
            try
            {
                var outer = new AsnReader(der, AsnEncodingRules.DER);
                var list = outer.ReadSequence();
                if (outer.HasData)
                    return false;
                var tbs = list.ReadSequence();
                list.ReadSequence();
                list.ReadBitString(out _);
                if (list.HasData)
                    return false;
                if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
                    tbs.ReadInteger();                  // version
                tbs.ReadSequence();                     // signature
                var issuer = tbs.ReadEncodedValue();    // issuer
                return expectedIssuer == null || issuer.Span.SequenceEqual(expectedIssuer);
            }
            catch (AsnContentException)
            {
                return false;
            }
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
                // GetSerialNumber is the INTEGER's bytes, little-endian, with its sign byte.
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
