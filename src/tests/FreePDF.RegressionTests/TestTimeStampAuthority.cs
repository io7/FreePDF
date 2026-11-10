// An offline RFC 3161 time-stamp authority for tests: it answers a TimeStampReq with a granted TimeStampResp whose
// token (a SignedData over a TSTInfo) is signed by a self-signed certificate with the timeStamping extended key usage.
// Built from RFC 3161 and RFC 5035 with .NET's own types. Original test code; MIT, see LICENSE.
#if NET8_0_OR_GREATER
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace FreePDF.RegressionTests;

sealed class TestTimeStampAuthority : IDisposable
{
    static readonly Oid TstInfoContentType = new("1.2.840.113549.1.9.16.1.4");
    static readonly Oid Policy = new("1.3.6.1.4.1.99999.1");
    long _serial = 1;

    public TestTimeStampAuthority(DateTimeOffset? time = null)
    {
        Time = time;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF test time-stamp authority", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.8") }, true));
        Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    public X509Certificate2 Certificate { get; }

    /// <summary>The time the authority puts in its tokens; now when null.</summary>
    public DateTimeOffset? Time { get; set; }

    public int Requests { get; private set; }

    public Task<byte[]> RespondAsync(byte[] requestDer)
    {
        Requests++;
        if (!Rfc3161TimestampRequest.TryDecode(requestDer, out var request, out _))
            throw new CryptographicException("Not a TimeStampReq.");
        // A small positive serial number in one byte: DER integers have no redundant leading bytes.
        var serial = new[] { (byte)(_serial++ % 127 + 1) };
        var info = new Rfc3161TimestampTokenInfo(Policy, request.HashAlgorithmId, request.GetMessageHash(), serial,
            Time ?? DateTimeOffset.UtcNow, nonce: request.GetNonce());

        var signed = new SignedCms(new ContentInfo(TstInfoContentType, info.Encode()));
        var signer = new CmsSigner(Certificate) { IncludeOption = X509IncludeOption.EndCertOnly, DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") };
        signer.SignedAttributes.Add(SigningCertificateV2(Certificate));
        signed.ComputeSignature(signer);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())           // TimeStampResp
        {
            using (writer.PushSequence())       // PKIStatusInfo
                writer.WriteInteger(0);         // granted
            writer.WriteEncodedValue(signed.Encode());
        }
        return Task.FromResult(writer.Encode());
    }

    // RFC 5035 signing-certificate-v2 with the SHA-256 hash of the certificate (the default algorithm).
    static AsnEncodedData SigningCertificateV2(X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
            writer.WriteOctetString(SHA256.HashData(certificate.RawData));
        return new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), writer.Encode());
    }

    public void Dispose() => Certificate.Dispose();
}
#endif
