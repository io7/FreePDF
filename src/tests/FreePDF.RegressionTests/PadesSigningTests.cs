// PAdES baseline signatures (7.10.0): PdfSharpDefaultSigner.Format = Pades writes ETSI.CAdES.detached with the
// signing-certificate-v2 attribute and no signing-time attribute (ETSI EN 319 142-1 5.2); a time-stamp authority makes
// it B-T. Original fixtures, a self-signed test identity and an offline test time-stamp authority; MIT, see LICENSE.
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class PadesSigningTests
{
    const string ContentType = "1.2.840.113549.1.9.3";
    const string MessageDigest = "1.2.840.113549.1.9.4";
    const string SigningTime = "1.2.840.113549.1.9.5";
    const string SigningCertificateV2 = "1.2.840.113549.1.9.16.2.47";
    const string SignatureTimeStamp = "1.2.840.113549.1.9.16.2.14";

    public PadesSigningTests() => TestFonts.Initialize();

    [Fact]
    public void A_pades_signature_has_the_baseline_attributes()
    {
        var pdf = SignNew(new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });

        var (cms, subFilter) = ReadSignature(pdf);

        Assert.Equal("ETSI.CAdES.detached", subFilter);
        var signer = cms.SignerInfos[0];
        var signedOids = signer.SignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value).ToList();
        Assert.Contains(ContentType, signedOids);
        Assert.Contains(MessageDigest, signedOids);
        Assert.Contains(SigningCertificateV2, signedOids);
        Assert.DoesNotContain(SigningTime, signedOids);
        Assert.DoesNotContain(SigningTime, signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value));
        Assert.Equal(Certificate.Thumbprint, signer.Certificate!.Thumbprint);

        // ESSCertIDv2: SHA-256 (default, not written) of the certificate, and the issuer and serial number.
        var value = signer.SignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid.Value == SigningCertificateV2).Values[0].RawData;
        var certId = new AsnReader(value, AsnEncodingRules.DER).ReadSequence().ReadSequence().ReadSequence();
        Assert.Equal(SHA256Hash(Certificate.RawData), certId.ReadOctetString());
        var issuerSerial = certId.ReadSequence();
        var generalNames = issuerSerial.ReadSequence();
        var directoryName = generalNames.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
        Assert.Equal(Certificate.IssuerName.RawData, directoryName.ReadEncodedValue().ToArray());
        var serial = Certificate.GetSerialNumber();
        Array.Reverse(serial);
        Assert.Equal(serial, issuerSerial.ReadIntegerBytes().ToArray());
        Assert.False(certId.HasData);

        // EN 319 142-1 6.3 c: the content-type attribute is id-data.
        var contentType = signer.SignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid.Value == ContentType).Values[0].RawData;
        Assert.Equal("1.2.840.113549.1.7.1", new AsnReader(contentType, AsnEncodingRules.DER).ReadObjectIdentifier());
        // EN 319 142-1 5.6: the ESIC extension is declared below PDF 2.0.
        Assert.Matches(@"/ESIC\s*<<\s*/BaseVersion\s*/1\.7\s*/ExtensionLevel\s*2\s*>>", Text(pdf));
    }

    [Fact]
    public void Pades_needs_sha256_or_stronger()
        => Assert.Throws<ArgumentException>(() => new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA1) { Format = PdfSignatureFormat.Pades });

    // A signer of its own reports its format through IDigitalSignerFormat.
    [Fact]
    public void A_custom_signer_reports_its_format()
    {
        var pdf = SignNew(new WrappingSigner(new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades }));

        Assert.Equal("ETSI.CAdES.detached", ReadSignature(pdf).SubFilter);
    }

    // A certificate whose serial number has a redundant leading byte (non-conforming, but loaded by .NET and Windows).
    [Fact]
    public void A_serial_number_with_a_redundant_leading_byte_is_written_as_its_value()
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=FreePDF test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF odd serial", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var issued = request.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), new byte[] { 0x00, 0x01 });
        using var certificate = issued.CopyWithPrivateKey(key);

        var pdf = SignNew(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });

        var signer = ReadSignature(pdf).Cms.SignerInfos[0];
        var value = signer.SignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid.Value == SigningCertificateV2).Values[0].RawData;
        var certId = new AsnReader(value, AsnEncodingRules.DER).ReadSequence().ReadSequence().ReadSequence();
        certId.ReadOctetString();
        var issuerSerial = certId.ReadSequence();
        issuerSerial.ReadSequence();
        Assert.Equal(System.Numerics.BigInteger.One, issuerSerial.ReadInteger());
    }

    [Fact]
    public void The_default_format_is_unchanged()
    {
        var pdf = SignNew(new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256));

        var (cms, subFilter) = ReadSignature(pdf);

        Assert.Equal("adbe.pkcs7.detached", subFilter);
        var signer = cms.SignerInfos[0];
        Assert.Contains(SigningTime, signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value));
        Assert.Equal(0, signer.SignedAttributes.Count);
        Assert.DoesNotContain("/ESIC", Text(pdf));
    }

#if !NET8_0_OR_GREATER
    // The RFC 3161 types are missing here, so a time-stamp client is refused when the signer is made.
    [Fact]
    public void A_time_stamp_client_needs_net8()
        => Assert.Throws<NotSupportedException>(() => new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256)
        {
            TimeStampClient = request => Task.FromResult(request),
        });
#endif

#if NET8_0_OR_GREATER
    // B-T: a signature time-stamp over the signature value, from the time-stamp client instead of HTTP.
    [Theory]
    [InlineData(PdfSignatureFormat.Pades, "ETSI.CAdES.detached")]
    [InlineData(PdfSignatureFormat.Pkcs7Detached, "adbe.pkcs7.detached")]
    public void A_time_stamp_client_adds_a_signature_time_stamp(PdfSignatureFormat format, string expectedSubFilter)
    {
        using var authority = new TestTimeStampAuthority(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var pdf = SignNew(new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256)
        {
            Format = format,
            TimeStampClient = authority.RespondAsync,
        });

        var (cms, subFilter) = ReadSignature(pdf);

        Assert.Equal(expectedSubFilter, subFilter);
        var signer = cms.SignerInfos[0];
        var attribute = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid.Value == SignatureTimeStamp);
        Assert.True(Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _));
        Assert.True(token!.VerifySignatureForSignerInfo(signer, out var tsaCertificate));
        Assert.Equal(authority.Certificate.Thumbprint, tsaCertificate!.Thumbprint);
        Assert.Equal(authority.Time, token.TokenInfo.Timestamp);
        // Only the real signature is time-stamped; the placeholder reserves room for the token.
        Assert.Equal(1, authority.Requests);
    }

    // A TimeStampResp with PKIStatusInfo status 2 (rejection) fails the save, and an existing file is left as it was.
    [Fact]
    public void A_rejected_time_stamp_fails_the_save_and_keeps_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"freepdf-rejected-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        try
        {
            var document = NewDocument();
            DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256)
            {
                Format = PdfSignatureFormat.Pades,
                TimeStampClient = _ => Task.FromResult(new byte[] { 0x30, 0x05, 0x30, 0x03, 0x02, 0x01, 0x02 }),
            }, new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test" });

            var refused = Assert.ThrowsAny<CryptographicException>(() => document.Save(path));
            Assert.Contains("Rejection", refused.Message);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A token larger than the reserve fails the save instead of being cut.
    [Fact]
    public void A_token_larger_than_the_reserve_fails_the_save()
    {
        using var authority = new TestTimeStampAuthority(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var document = NewDocument();
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = authority.RespondAsync,
            TimeStampReserve = 64,
        }, new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test" });

        Assert.ThrowsAny<Exception>(() => document.Save(new MemoryStream()));
    }

    // The built-in HTTP client posts application/timestamp-query and reads application/timestamp-reply.
    [Fact]
    public async Task The_time_stamp_authority_uri_is_posted_to()
    {
        using var authority = new TestTimeStampAuthority(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        using var server = new TestHttpTimeStampServer(authority);
        var serving = server.ServeOnceAsync();

        var pdf = SignNew(new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256, server.Uri) { Format = PdfSignatureFormat.Pades });
        await serving;

        Assert.Equal("application/timestamp-query", server.ContentType);
        var signer = ReadSignature(pdf).Cms.SignerInfos[0];
        Assert.Contains(SignatureTimeStamp, signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value));
    }
#endif

    // A field's required /SubFilter seed value is checked against the signer's format.
    [Theory]
    [InlineData("/ETSI.CAdES.detached", PdfSignatureFormat.Pades, true)]
    [InlineData("/ETSI.CAdES.detached", PdfSignatureFormat.Pkcs7Detached, false)]
    [InlineData("/adbe.pkcs7.detached", PdfSignatureFormat.Pades, false)]
    [InlineData("/adbe.pkcs7.detached", PdfSignatureFormat.Pkcs7Detached, true)]
    public void A_required_sub_filter_must_match_the_signers_format(string required, PdfSignatureFormat format, bool allowed)
    {
        var document = NewDocument();
        var field = document.Catalog.GetOrCreateAcroForm().AddSignatureField("approval", document.Pages[0], new XRect(36, 36, 160, 40));
        var seed = new PdfDictionary(document);
        seed.Elements.SetName("/Type", "/SV");
        seed.Elements["/SubFilter"] = new PdfArray(document, new PdfName(required));
        seed.Elements.SetInteger("/Ff", 2);
        field.Elements["/SV"] = seed;
        var signer = new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256) { Format = format };

        void Sign() => DigitalSignatureHandler.ForDocument(document, signer, new DigitalSignatureOptions { FieldName = "approval" });

        if (allowed)
        {
            Sign();
            using var output = new MemoryStream();
            document.Save(output);
            Assert.Equal(required.Substring(1), ReadSignature(output.ToArray()).SubFilter);
        }
        else
            Assert.Throws<NotSupportedException>(Sign);
    }

    static PdfDocument NewDocument()
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("PAdES test", new XFont("FreePDF Synthetic", 12), XBrushes.Black, 36, 100);
        return document;
    }

    static byte[] SignNew(IDigitalSigner signer)
    {
        using var document = NewDocument();
        DigitalSignatureHandler.ForDocument(document, signer, new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test" });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    /// <summary>
    /// The last signature's CMS, checked against the bytes its /ByteRange covers, and its /SubFilter.
    /// </summary>
    static (SignedCms Cms, string SubFilter) ReadSignature(byte[] pdf)
    {
        var text = new string(pdf.Select(b => (char)b).ToArray());
        var range = Regex.Matches(text, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]").Cast<Match>().Last();
        var numbers = Enumerable.Range(1, 4).Select(i => int.Parse(range.Groups[i].Value)).ToArray();
        var covered = pdf.Take(numbers[1]).Concat(pdf.Skip(numbers[2]).Take(numbers[3])).ToArray();
        Assert.Equal(pdf.Length, numbers[2] + numbers[3]);
        var hex = text.Substring(numbers[1] + 1, numbers[2] - numbers[1] - 2);
        var der = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(2 * i, 2), 16)).ToArray();
        var length = new AsnReader(der, AsnEncodingRules.BER).ReadEncodedValue().Length;

        var cms = new SignedCms(new ContentInfo(covered), detached: true);
        cms.Decode(der.AsSpan(0, length).ToArray());
        cms.CheckSignature(verifySignatureOnly: true);
        var subFilter = Regex.Matches(text, @"/SubFilter\s*/([A-Za-z0-9.]+)").Cast<Match>().Last().Groups[1].Value;
        return (cms, subFilter);
    }

    static string Text(byte[] pdf) => new(pdf.Select(b => (char)b).ToArray());

    sealed class WrappingSigner(PdfSharpDefaultSigner inner) : IDigitalSigner, IDigitalSignerFormat
    {
        public string CertificateName => inner.CertificateName;
        public Task<int> GetSignatureSizeAsync() => inner.GetSignatureSizeAsync();
        public Task<byte[]> GetSignatureAsync(Stream stream) => inner.GetSignatureAsync(stream);
        public PdfSignatureFormat Format => inner.Format;
    }

    static byte[] SHA256Hash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }

    static readonly X509Certificate2 Certificate = CreateCertificate();

    static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF PAdES test, O=FreePDF tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
