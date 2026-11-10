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
    }

    [Fact]
    public void The_default_format_is_unchanged()
    {
        var pdf = SignNew(new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256));

        var (cms, subFilter) = ReadSignature(pdf);

        Assert.Equal("adbe.pkcs7.detached", subFilter);
        var signer = cms.SignerInfos[0];
        Assert.Contains(SigningTime, signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value));
        Assert.DoesNotContain(SigningCertificateV2, signer.SignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value));
    }

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
        // One request sizes the placeholder, one stamps the real signature.
        Assert.Equal(2, authority.Requests);
    }

    [Fact]
    public void A_refused_time_stamp_fails_the_signature()
    {
        var pdfDocument = NewDocument();
        DigitalSignatureHandler.ForDocument(pdfDocument, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = _ => Task.FromResult(new byte[] { 0x30, 0x03, 0x02, 0x01, 0x02 }),   // status 2: rejection
        }, new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test" });

        Assert.ThrowsAny<CryptographicException>(() => pdfDocument.Save(new MemoryStream()));
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
