// Original signing and tamper-detection tests. Self-signed test identity; MIT.
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class CryptographicSignatureTests
{
    [Theory]
    [InlineData(PdfMessageDigestType.SHA256, "2.16.840.1.101.3.4.2.1")]
    [InlineData(PdfMessageDigestType.SHA384, "2.16.840.1.101.3.4.2.2")]
    [InlineData(PdfMessageDigestType.SHA512, "2.16.840.1.101.3.4.2.3")]
    public async Task Detached_signature_verifies_and_detects_changed_content(PdfMessageDigestType digest, string oid)
    {
        using var rsa = RSA.Create(); rsa.KeySize = 2048;
        using var certificate = Certificate(rsa);
        var content = Encoding.UTF8.GetBytes("Original FreePDF release-check content.");
        var signer = new PdfSharpDefaultSigner(certificate, digest);
        var signature = await signer.GetSignatureAsync(new MemoryStream(content));
        var cms = new SignedCms(new ContentInfo(content), true);
        cms.Decode(signature);
        cms.CheckSignature(true);
        Assert.Equal(oid, Assert.Single(cms.SignerInfos.Cast<SignerInfo>()).DigestAlgorithm.Value);
        content[0] ^= 1;
        var tampered = new SignedCms(new ContentInfo(content), true);
        tampered.Decode(signature);
        Assert.Throws<CryptographicException>(() => tampered.CheckSignature(true));
    }

    [Fact]
    public async Task Signer_reads_streams_that_return_partial_buffers()
    {
        using var rsa = RSA.Create(); rsa.KeySize = 2048;
        using var certificate = Certificate(rsa);
        var content = Encoding.UTF8.GetBytes("A Stream.Read call is allowed to return fewer bytes than requested.");
        using var stream = new ShortReadStream(content);
        var signature = await new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256).GetSignatureAsync(stream);
        var cms = new SignedCms(new ContentInfo(content), true);
        cms.Decode(signature);
        cms.CheckSignature(true);
    }

    [Fact]
    public async Task Signer_rejects_unexpected_end_without_looping()
    {
        using var rsa = RSA.Create(); rsa.KeySize = 2048;
        using var certificate = Certificate(rsa);
        using var stream = new TruncatedStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256).GetSignatureAsync(stream));
    }

    static X509Certificate2 Certificate(RSA key)
    {
        var request = new CertificateRequest("CN=FreePDF synthetic signature fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    sealed class ShortReadStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 7));
    }

    sealed class TruncatedStream(byte[] data) : MemoryStream(data)
    {
        public override long Length => base.Length + 10;
    }
}
