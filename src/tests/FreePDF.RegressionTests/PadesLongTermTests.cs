// PAdES B-LT and B-LTA (7.10.0): validation data in the document security store (DSS) and document time-stamps
// (ETSI.RFC3161), added in Append mode after the signatures, also where a certification permits no changes (P=1).
// Original fixtures, self-signed test identities and an offline test time-stamp authority; MIT, see LICENSE.
#if NET8_0_OR_GREATER
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class PadesLongTermTests
{
    static readonly DateTimeOffset StampTime = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public PadesLongTermTests() => TestFonts.Initialize();

    [Fact]
    public void Validation_data_is_added_in_an_update_and_not_twice()
    {
        using var authority = new TestTimeStampAuthority(StampTime);
        var signed = SignBT(authority);

        var (withDss, added) = Update(signed, document => PdfDocumentSecurityStore.Add(document, [Certificate.RawData, authority.Certificate.RawData]));

        Assert.Equal(2, added);
        Assert.Equal(signed, withDss.Take(signed.Length).ToArray());
        using (var document = PdfReader.Open(new MemoryStream(withDss), PdfDocumentOpenMode.Import))
        {
            var certs = document.Catalog.Elements.GetDictionary("/DSS")!.Elements.GetArray("/Certs")!;
            var data = Enumerable.Range(0, certs.Elements.Count).Select(i => certs.Elements.GetDictionary(i)!.Stream!.UnfilteredValue).ToList();
            Assert.Equal(new[] { Certificate.RawData, authority.Certificate.RawData }, data);
        }

        var (_, again) = Update(withDss, document => PdfDocumentSecurityStore.Add(document, [Certificate.RawData]));
        Assert.Equal(0, again);
    }

    [Fact]
    public void Validation_data_must_be_der()
        => Assert.Throws<ArgumentException>(() => PdfDocumentSecurityStore.Add(new PdfDocument(), crls: [new byte[] { 1, 2, 3 }]));

    [Fact]
    public void A_document_time_stamp_covers_the_document()
    {
        using var authority = new TestTimeStampAuthority(StampTime);
        var signed = SignBT(authority);

        var (stamped, _) = Update(signed, document => AddDocumentTimeStamp(document, authority));

        Assert.Equal(signed, stamped.Take(signed.Length).ToArray());
        var text = Text(stamped);
        Assert.Matches(@"/Type\s*/DocTimeStamp", text);
        Assert.Matches(@"/SubFilter\s*/ETSI\.RFC3161", text);
        var (covered, der) = LastSignature(stamped);
        Assert.True(Rfc3161TimestampToken.TryDecode(der, out var token, out _));
        Assert.True(token!.VerifySignatureForData(covered, out var tsaCertificate));
        Assert.Equal(authority.Certificate.Thumbprint, tsaCertificate!.Thumbprint);
        Assert.Equal(StampTime, token.TokenInfo.Timestamp);
        Assert.Equal("2.16.840.1.101.3.4.2.1", token.TokenInfo.HashAlgorithmId.Value);   // SHA-256
    }

    // B-LTA: B-T signature, then the validation data, then a document time-stamp over both.
    [Fact]
    public void Validation_data_then_a_document_time_stamp_make_b_lta()
    {
        using var authority = new TestTimeStampAuthority(StampTime);
        var (withDss, _) = Update(SignBT(authority), document => PdfDocumentSecurityStore.Add(document, [Certificate.RawData, authority.Certificate.RawData]));

        var (lta, _) = Update(withDss, document => AddDocumentTimeStamp(document, authority));

        Assert.Equal(withDss, lta.Take(withDss.Length).ToArray());
        var (covered, der) = LastSignature(lta);
        Assert.True(Rfc3161TimestampToken.TryDecode(der, out var token, out _));
        Assert.True(token!.VerifySignatureForData(covered, out _));
        Assert.True(covered.Length > withDss.Length);

        // The PAdES signature still verifies over the bytes it covers.
        var (signedBytes, signature) = Signature(lta, 0);
        var cms = new SignedCms(new ContentInfo(signedBytes), detached: true);
        cms.Decode(signature);
        cms.CheckSignature(verifySignatureOnly: true);
        Assert.Equal(Certificate.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
    }

    // At P=1 (no changes) validation data and document time-stamps are still permitted; a signature is not.
    [Fact]
    public void A_document_certified_with_no_changes_takes_validation_data_and_a_document_time_stamp_only()
    {
        using var authority = new TestTimeStampAuthority(StampTime);
        var certified = SignBT(authority, PdfCertificationLevel.NoChanges);

        var (withDss, _) = Update(certified, document => PdfDocumentSecurityStore.Add(document, [Certificate.RawData]));
        Assert.Equal(certified, withDss.Take(certified.Length).ToArray());
        // The update leaves the document information as it is: no new /Info object in it.
        Assert.DoesNotContain("/ModDate", Text(withDss).Substring(certified.Length));

        var (stamped, _) = Update(withDss, document => AddDocumentTimeStamp(document, authority));
        Assert.Equal(withDss, stamped.Take(withDss.Length).ToArray());

        using var refused = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Append);
        DigitalSignatureHandler.ForDocument(refused, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades },
            new DigitalSignatureOptions { Reason = "Countersign" });
        var error = Assert.Throws<InvalidOperationException>(() => refused.Save(new MemoryStream()));
        Assert.Contains("P=1", error.Message);
    }

    [Fact]
    public void A_document_time_stamp_is_an_invisible_new_field()
    {
        using var authority = new TestTimeStampAuthority(StampTime);
        using var document = PdfReader.Open(new MemoryStream(SignBT(authority)), PdfDocumentOpenMode.Append);
        var signer = new PdfTimeStampSigner(authority.RespondAsync);

        Assert.ThrowsAny<ArgumentException>(() => DigitalSignatureHandler.ForDocument(document, signer,
            new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 100, 30) }));
        Assert.ThrowsAny<ArgumentException>(() => DigitalSignatureHandler.ForDocument(document, signer,
            new DigitalSignatureOptions { Certification = PdfCertificationLevel.FormFilling }));
    }

    [Fact]
    public void A_document_time_stamp_needs_sha256_or_stronger()
        => Assert.Throws<ArgumentException>(() => new PdfTimeStampSigner(_ => Task.FromResult(Array.Empty<byte>()), PdfMessageDigestType.SHA1));

    [Fact]
    public void A_token_larger_than_the_reserve_fails_the_document_time_stamp()
    {
        using var authority = new TestTimeStampAuthority(StampTime);
        using var document = PdfReader.Open(new MemoryStream(SignBT(authority)), PdfDocumentOpenMode.Append);
        DigitalSignatureHandler.ForDocument(document, new PdfTimeStampSigner(authority.RespondAsync) { TimeStampReserve = 64 }, new DigitalSignatureOptions());

        var refused = Assert.Throws<InvalidOperationException>(() => document.Save(new MemoryStream()));
        Assert.Contains("TimeStampReserve", refused.Message);
    }

    static void AddDocumentTimeStamp(PdfDocument document, TestTimeStampAuthority authority)
        => DigitalSignatureHandler.ForDocument(document, new PdfTimeStampSigner(authority.RespondAsync), new DigitalSignatureOptions());

    static byte[] SignBT(TestTimeStampAuthority authority, PdfCertificationLevel? certification = null)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("PAdES long-term test", new XFont("FreePDF Synthetic", 12), XBrushes.Black, 36, 100);
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = authority.RespondAsync,
        }, new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test", Certification = certification });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static (byte[] Pdf, T Result) Update<T>(byte[] original, Func<PdfDocument, T> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        var result = change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return (output.ToArray(), result);
    }

    static (byte[] Pdf, int Result) Update(byte[] original, Action<PdfDocument> change)
        => Update(original, document => { change(document); return 0; });

    static (byte[] Covered, byte[] Der) LastSignature(byte[] pdf) => Signature(pdf, -1);

    // The bytes a signature's /ByteRange covers, and its /Contents without the zero padding; index -1 is the last.
    static (byte[] Covered, byte[] Der) Signature(byte[] pdf, int index)
    {
        var text = Text(pdf);
        var ranges = Regex.Matches(text, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]").Cast<Match>().ToList();
        var range = ranges[index < 0 ? ranges.Count - 1 : index];
        var n = Enumerable.Range(1, 4).Select(i => int.Parse(range.Groups[i].Value)).ToArray();
        if (index < 0)
            Assert.Equal(pdf.Length, n[2] + n[3]);   // The last signature covers the whole file.
        var covered = pdf.Take(n[1]).Concat(pdf.Skip(n[2]).Take(n[3])).ToArray();
        var hex = text.Substring(n[1] + 1, n[2] - n[1] - 2);
        var der = Convert.FromHexString(hex);
        var length = new System.Formats.Asn1.AsnReader(der, System.Formats.Asn1.AsnEncodingRules.BER).ReadEncodedValue().Length;
        return (covered, der.AsSpan(0, length).ToArray());
    }

    static string Text(byte[] pdf) => new(pdf.Select(b => (char)b).ToArray());

    static readonly X509Certificate2 Certificate = CreateCertificate();

    static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF long-term test, O=FreePDF tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
#endif
