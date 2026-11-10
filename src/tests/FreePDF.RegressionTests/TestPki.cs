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

    /// <summary>HTTP status for OCSP requests; 200 answers them.</summary>
    public HttpStatusCode OcspStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>What the OCSP responder answers.</summary>
    public OcspAnswer Answer { get; set; } = OcspAnswer.Status;

    /// <summary>When set, the CRL addresses return these bytes instead of a CRL.</summary>
    public byte[]? CrlBody { get; set; }

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

    X509Certificate2 CreateEndEntity(string name, DateTimeOffset from, DateTimeOffset until, X509KeyUsageFlags usage, X509Extension? extra)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Intermediate, true, false));
        request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension([OcspUrl], null));
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

    byte[] Crl(X509Certificate2 issuer)
    {
        var builder = new CertificateRevocationListBuilder();
        foreach (var serial in Revoked)
            builder.AddEntry(serial, DateTimeOffset.UtcNow.AddMinutes(-10), X509RevocationReason.KeyCompromise);
        return builder.Build(issuer, BigInteger.One, DateTimeOffset.UtcNow.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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

        var data = new AsnWriter(AsnEncodingRules.DER);
        using (data.PushSequence())                                                          // ResponseData
        {
            using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)))        // responderID byName
                data.WriteEncodedValue(issuer.SubjectName.RawData);
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
                        data.WriteGeneralizedTime(now.AddMinutes(-10), omitFractionalSeconds: true);
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
        var signature = issuer.GetRSAPrivateKey()!.SignData(tbs, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var basic = new AsnWriter(AsnEncodingRules.DER);
        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(tbs);
            using (basic.PushSequence())
            {
                basic.WriteObjectIdentifier("1.2.840.113549.1.1.11");                         // sha256WithRSAEncryption
                basic.WriteNull();
            }
            basic.WriteBitString(signature);
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
