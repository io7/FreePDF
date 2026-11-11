// A small test PKI for revocation data: a root CA, an intermediate CA, a signer and a time-stamp authority, with an
// OCSP responder (RFC 6960) and CRLs (RFC 5280) answered in-process by an HttpMessageHandler, so nothing reaches the
// network. The signer and the TSA name the OCSP responder (AIA) and the intermediate's CRL; the intermediate names
// only the root's CRL. Built from RFC 5280 and RFC 6960 with .NET's own types. Original test code; MIT, see LICENSE.
#if NET8_0_OR_GREATER
using System.Formats.Asn1;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FreePDF.RegressionTests;

public sealed class TestPki : IDisposable
{
    public const string OcspUrl = "http://pki.test/ocsp";
    public const string RootCrlUrl = "http://pki.test/root.crl";
    public const string IntermediateCrlUrl = "http://pki.test/intermediate.crl";
    static readonly DateTimeOffset CaFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset CaUntil = new(2036, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <param name="signerValidity">How long the signer's certificate is valid from now.</param>
    public TestPki(TimeSpan? signerValidity = null)
    {
        Root = CreateCa("CN=FreePDF test root CA", null, null);
        Intermediate = CreateCa("CN=FreePDF test intermediate CA", Root, RootCrlUrl);
        var now = DateTimeOffset.UtcNow;
        Signer = CreateEndEntity("CN=FreePDF test signer, O=FreePDF tests", now.AddHours(-1), now + (signerValidity ?? TimeSpan.FromDays(30)),
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, null);
        TimeStampAuthority = CreateEndEntity("CN=FreePDF test time-stamp authority", CaFrom, CaUntil, X509KeyUsageFlags.DigitalSignature,
            new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.8") }, true));
    }

    public X509Certificate2 Root { get; }
    public X509Certificate2 Intermediate { get; }
    public X509Certificate2 Signer { get; }
    public X509Certificate2 TimeStampAuthority { get; }

    /// <summary>Serial numbers (big-endian, as in the certificate) that the OCSP responder and the CRLs list as revoked.</summary>
    public List<byte[]> Revoked { get; } = [];

    /// <summary>When the revoked certificates were revoked; ten minutes ago when null. OCSP gives no reason, CRLs keyCompromise.</summary>
    public DateTimeOffset? RevocationTime { get; set; }

    /// <summary>The reason OCSP and the CRLs give for revocations; OCSP none and CRLs keyCompromise when null.</summary>
    public X509RevocationReason? RevocationReason { get; set; }

    /// <summary>HTTP status for OCSP requests; 200 answers them.</summary>
    public HttpStatusCode OcspStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>What the OCSP responder answers.</summary>
    public OcspAnswer Answer { get; set; } = OcspAnswer.Status;

    /// <summary>When set, the CRL addresses return these bytes instead of a CRL.</summary>
    public byte[]? CrlBody { get; set; }

    /// <summary>Signs CRLs and OCSP responses with RSASSA-PSS (SHA-256), which FreePDF does not verify.</summary>
    public bool Pss { get; set; }

    public enum OcspAnswer
    {
        /// <summary>Good, or revoked when the serial number is in <see cref="Revoked"/>.</summary>
        Status,
        /// <summary>Unknown (RFC 6960 4.2.1).</summary>
        Unknown,
        /// <summary>An answer about another serial number only.</summary>
        OtherCertificate,
    }

    public int OcspRequests { get; private set; }
    public int CrlRequests { get; private set; }

    public HttpClient CreateClient() => new(new Handler(this));

    X509Certificate2 CreateCa(string name, X509Certificate2? issuer, string? crlUrl)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        if (issuer == null)
            return request.CreateSelfSigned(CaFrom, CaUntil);
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));
        request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([crlUrl!]));
        using var issued = request.Create(issuer, CaFrom, CaUntil, Serial());
        return issued.CopyWithPrivateKey(key);
    }

    /// <summary>
    /// Another signer the intermediate CA issues, with the given CRL distribution points extension in place of the usual
    /// one, or none.
    /// </summary>
    public X509Certificate2 CreateSigner(X509Extension? distributionPoints)
    {
        var now = DateTimeOffset.UtcNow;
        return CreateEndEntity("CN=FreePDF test signer 2, O=FreePDF tests", now.AddHours(-1), now.AddDays(30),
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, distributionPoints, usualDistributionPoint: false);
    }

    X509Certificate2 CreateEndEntity(string name, DateTimeOffset from, DateTimeOffset until, X509KeyUsageFlags usage, X509Extension? extra,
        bool usualDistributionPoint = true)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Intermediate, true, false));
        request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension([OcspUrl], null));
        if (usualDistributionPoint)
            request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([IntermediateCrlUrl]));
        if (extra != null)
            request.CertificateExtensions.Add(extra);
        using var issued = request.Create(Intermediate, from, until, Serial());
        return issued.CopyWithPrivateKey(key);
    }

    // A positive 8-byte serial number.
    static byte[] Serial()
    {
        var serial = RandomNumberGenerator.GetBytes(8);
        serial[0] = (byte)(serial[0] & 0x7F | 0x01);
        return serial;
    }

    // id-RSASSA-PSS with SHA-256, MGF1 with SHA-256 and a 32-byte salt (RFC 4055 3.1), inside an AlgorithmIdentifier.
    static void WritePssParameters(AsnWriter writer)
    {
        writer.WriteObjectIdentifier("1.2.840.113549.1.1.10");
        using (writer.PushSequence())
        {
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                writer.WriteNull();
            }
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)))
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.113549.1.1.8");
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                    writer.WriteNull();
                }
            }
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2, true)))
                writer.WriteInteger(32);
        }
    }

    /// <summary>
    /// A CRL of the issuer for one partition: its issuing distribution point extension (RFC 5280 5.2.5, critical) names
    /// the URI as the distribution point's full name. It lists the serial numbers in <see cref="Revoked"/>.
    /// </summary>
    public byte[] PartitionCrl(X509Certificate2 issuer, string uri)
    {
        var now = DateTimeOffset.UtcNow;
        var tbs = new AsnWriter(AsnEncodingRules.DER);
        using (tbs.PushSequence())
        {
            tbs.WriteInteger(1);                                                              // v2
            using (tbs.PushSequence())
            {
                tbs.WriteObjectIdentifier("1.2.840.113549.1.1.11");
                tbs.WriteNull();
            }
            tbs.WriteEncodedValue(issuer.SubjectName.RawData);
            tbs.WriteUtcTime(now.AddMinutes(-1));
            tbs.WriteUtcTime(now.AddDays(7));
            if (Revoked.Count > 0)
            {
                using (tbs.PushSequence())
                {
                    foreach (var serial in Revoked)
                    {
                        using (tbs.PushSequence())
                        {
                            tbs.WriteIntegerUnsigned(serial);
                            tbs.WriteUtcTime(RevocationTime ?? now.AddMinutes(-10));
                        }
                    }
                }
            }
            using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))         // crlExtensions
            using (tbs.PushSequence())
            using (tbs.PushSequence())
            {
                tbs.WriteObjectIdentifier("2.5.29.28");                                        // issuingDistributionPoint
                tbs.WriteBoolean(true);
                var point = new AsnWriter(AsnEncodingRules.DER);
                using (point.PushSequence())
                using (point.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))    // distributionPoint
                using (point.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))    // fullName
                    point.WriteCharacterString(UniversalTagNumber.IA5String, uri, new Asn1Tag(TagClass.ContextSpecific, 6));
                tbs.WriteOctetString(point.Encode());
            }
        }
        var tbsDer = tbs.Encode();
        var signature = issuer.GetRSAPrivateKey()!.SignData(tbsDer, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var crl = new AsnWriter(AsnEncodingRules.DER);
        using (crl.PushSequence())
        {
            crl.WriteEncodedValue(tbsDer);
            using (crl.PushSequence())
            {
                crl.WriteObjectIdentifier("1.2.840.113549.1.1.11");
                crl.WriteNull();
            }
            crl.WriteBitString(signature);
        }
        return crl.Encode();
    }

    public byte[] Crl(X509Certificate2 issuer)
    {
        var builder = new CertificateRevocationListBuilder();
        foreach (var serial in Revoked)
            builder.AddEntry(serial, RevocationTime ?? DateTimeOffset.UtcNow.AddMinutes(-10), RevocationReason ?? X509RevocationReason.KeyCompromise);
        return builder.Build(issuer, BigInteger.One, DateTimeOffset.UtcNow.AddDays(7), HashAlgorithmName.SHA256,
            Pss ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
    }

    /// <summary>The responder's answer about the certificate, without HTTP: an OCSPRequest with one SHA-1 CertID.</summary>
    public byte[] OcspResponseFor(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        var request = new AsnWriter(AsnEncodingRules.DER);
        using (request.PushSequence())                       // OCSPRequest
        using (request.PushSequence())                       // TBSRequest
        using (request.PushSequence())                       // requestList
        using (request.PushSequence())                       // Request
        using (request.PushSequence())                       // CertID
        {
            using (request.PushSequence())
            {
                request.WriteObjectIdentifier("1.3.14.3.2.26");
                request.WriteNull();
            }
            request.WriteOctetString(SHA1.HashData(issuer.SubjectName.RawData));
            request.WriteOctetString(SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData));
            request.WriteIntegerUnsigned(certificate.SerialNumberBytes.Span);
        }
        return OcspResponse(request.Encode());
    }

    // An OCSPResponse for the request's single certificate, signed by the CA that issued it (RFC 6960 4.2.2.2).
    byte[] OcspResponse(byte[] requestDer)
    {
        var request = new AsnReader(requestDer, AsnEncodingRules.DER).ReadSequence().ReadSequence();   // OCSPRequest, TBSRequest
        var single = request.ReadSequence().ReadSequence();                                            // requestList, Request
        var certId = single.ReadSequence();
        var hashAlgorithm = certId.ReadSequence().ReadObjectIdentifier();
        var nameHash = certId.ReadOctetString();
        var keyHash = certId.ReadOctetString();
        var serial = certId.ReadIntegerBytes().ToArray();
        if (hashAlgorithm != "1.3.14.3.2.26")
            throw new InvalidDataException("The test responder answers SHA-1 CertIDs.");
        // RFC 6960 4.1.1: the issuer's name hash and the hash of its public key's BIT STRING value.
        var issuer = new[] { Root, Intermediate }.SingleOrDefault(ca => SHA1.HashData(ca.SubjectName.RawData).AsSpan().SequenceEqual(nameHash)
            && SHA1.HashData(ca.PublicKey.EncodedKeyValue.RawData).AsSpan().SequenceEqual(keyHash))
            ?? throw new InvalidDataException("No CA of the test PKI has this name and key hash.");
        if (Answer == OcspAnswer.OtherCertificate)
            serial = [0x01, 0x02, 0x03];
        // The CertID is written again, with the AlgorithmIdentifier's parameters left out as some responders do: the
        // client must match it by its fields.
        var answeredId = new AsnWriter(AsnEncodingRules.DER);
        using (answeredId.PushSequence())
        {
            using (answeredId.PushSequence())
                answeredId.WriteObjectIdentifier(hashAlgorithm);
            answeredId.WriteOctetString(nameHash);
            answeredId.WriteOctetString(keyHash);
            answeredId.WriteIntegerUnsigned(serial);
        }
        var certIdDer = answeredId.Encode();
        var now = DateTimeOffset.UtcNow;
        // The issuing CA signs, or a responder certificate (RFC 6960 4.2.2.2): one the CA issued, or a forgery that
        // only carries the CA's name.
        var responder = Responder == OcspResponder.Issuer
            ? issuer
            : ResponderFor(issuer, Responder == OcspResponder.ForgedDelegated, Responder == OcspResponder.DelegatedPss,
                noCheck: Responder != OcspResponder.DelegatedWithoutNoCheck);

        var data = new AsnWriter(AsnEncodingRules.DER);
        using (data.PushSequence())                                                          // ResponseData
        {
            using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)))        // responderID byName
                data.WriteEncodedValue(responder.SubjectName.RawData);
            data.WriteGeneralizedTime(now, omitFractionalSeconds: true);                     // producedAt
            using (data.PushSequence())                                                      // responses
            using (data.PushSequence())                                                      // SingleResponse
            {
                data.WriteEncodedValue(certIdDer);
                if (Answer == OcspAnswer.Unknown)
                {
                    data.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 2));                 // unknown
                }
                else if (Revoked.Any(r => r.AsSpan().SequenceEqual(serial)))
                {
                    using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)))  // revoked
                    {
                        data.WriteGeneralizedTime(RevocationTime ?? now.AddMinutes(-10), omitFractionalSeconds: true);
                        if (RevocationReason is { } reason)
                        {
                            using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))  // revocationReason
                                data.WriteEnumeratedValue(reason);
                        }
                    }
                }
                else
                {
                    data.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));                 // good
                }
                data.WriteGeneralizedTime(now, omitFractionalSeconds: true);                 // thisUpdate
                using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))    // nextUpdate
                    data.WriteGeneralizedTime(now.AddDays(7), omitFractionalSeconds: true);
            }
        }
        var tbs = data.Encode();
        var signature = responder.GetRSAPrivateKey()!.SignData(tbs, HashAlgorithmName.SHA256, Pss ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);

        var basic = new AsnWriter(AsnEncodingRules.DER);
        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(tbs);
            using (basic.PushSequence())
            {
                if (Pss)
                    WritePssParameters(basic);
                else
                {
                    basic.WriteObjectIdentifier("1.2.840.113549.1.1.11");                     // sha256WithRSAEncryption
                    basic.WriteNull();
                }
            }
            basic.WriteBitString(signature);
            if (!ReferenceEquals(responder, issuer))
            {
                using (basic.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))   // certs
                using (basic.PushSequence())
                    basic.WriteEncodedValue(responder.RawData);
            }
        }

        var response = new AsnWriter(AsnEncodingRules.DER);
        using (response.PushSequence())
        {
            response.WriteEnumeratedValue(OcspResponseStatus.Successful);
            using (response.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
            using (response.PushSequence())
            {
                response.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1.1");                      // id-pkix-ocsp-basic
                response.WriteOctetString(basic.Encode());
            }
        }
        return response.Encode();
    }

    enum OcspResponseStatus { Successful = 0 }

    readonly Dictionary<(string, bool), X509Certificate2> _responders = new();

    // A responder certificate with id-kp-OCSPSigning and id-pkix-ocsp-nocheck. The forgery is signed by a key of its
    // own under a CA certificate that only copies the real CA's name.
    X509Certificate2 ResponderFor(X509Certificate2 issuer, bool forged, bool pss = false, bool noCheck = true)
    {
        var cacheKey = issuer.Thumbprint + (pss ? "/pss" : "") + (noCheck ? "" : "/check");
        if (_responders.TryGetValue((cacheKey, forged), out var cached))
            return cached;
        var signingCa = issuer;
        if (forged)
        {
            using var fakeKey = RSA.Create(2048);
            var fakeRequest = new CertificateRequest(issuer.SubjectName, fakeKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            fakeRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            signingCa = fakeRequest.CreateSelfSigned(CaFrom, CaUntil);
        }
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF test OCSP responder", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.9") }, true));
        if (noCheck)
            request.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.48.1.5", [0x05, 0x00], false));   // id-pkix-ocsp-nocheck
        using var issued = request.Create(signingCa.SubjectName,
            X509SignatureGenerator.CreateForRSA(signingCa.GetRSAPrivateKey()!, pss ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1),
            CaFrom, CaUntil, Serial());
        var responder = issued.CopyWithPrivateKey(key);
        _responders[(cacheKey, forged)] = responder;
        return responder;
    }

    /// <summary>Who signs the OCSP responses.</summary>
    public OcspResponder Responder { get; set; } = OcspResponder.Issuer;

    public enum OcspResponder
    {
        /// <summary>The CA that issued the certificate.</summary>
        Issuer,
        /// <summary>A responder certificate that the CA issued.</summary>
        Delegated,
        /// <summary>A responder certificate that the CA issued, signed with RSASSA-PSS.</summary>
        DelegatedPss,
        /// <summary>A responder certificate that the CA issued, without id-pkix-ocsp-nocheck.</summary>
        DelegatedWithoutNoCheck,
        /// <summary>A responder certificate with the CA's name as issuer, signed by another key.</summary>
        ForgedDelegated,
    }

    sealed class Handler(TestPki pki) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url == OcspUrl && request.Method == HttpMethod.Post)
            {
                pki.OcspRequests++;
                if (pki.OcspStatus != HttpStatusCode.OK)
                    return new HttpResponseMessage(pki.OcspStatus);
                var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return Reply(pki.OcspResponse(body), "application/ocsp-response");
            }
            if (url == RootCrlUrl || url == IntermediateCrlUrl)
            {
                pki.CrlRequests++;
                return Reply(pki.CrlBody ?? pki.Crl(url == RootCrlUrl ? pki.Root : pki.Intermediate), "application/pkix-crl");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        static HttpResponseMessage Reply(byte[] body, string type)
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    public void Dispose()
    {
        Root.Dispose();
        Intermediate.Dispose();
        Signer.Dispose();
        TimeStampAuthority.Dispose();
    }
}
#endif
