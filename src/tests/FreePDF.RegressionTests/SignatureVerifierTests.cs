// PdfSignatureVerifier (7.10.0): intact or not, signer, time-stamps, trust, revocation from the DSS, later changes
// against DocMDP, PAdES level. Original fixtures, self-signed identities, TestPki and the offline TestTimeStampAuthority;
// MIT, see LICENSE.
#if NET8_0_OR_GREATER
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class SignatureVerifierTests
{
    public SignatureVerifierTests() => TestFonts.Initialize();

    [Fact]
    public async Task A_pades_signature_with_a_trusted_certificate_is_valid()
    {
        using var certificate = SelfSigned();
        var pdf = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Valid, result.Status);
        Assert.True(result.Intact);
        Assert.True(result.Trusted);
        Assert.True(result.CoversWholeFile);
        Assert.Equal(PdfSignatureKind.Approval, result.Kind);
        Assert.Equal("ETSI.CAdES.detached", result.SubFilter);
        Assert.Equal(PdfPadesLevel.BaselineB, result.PadesLevel);
        Assert.Equal(PdfRevocationStatus.Good, result.Revocation);   // a self-signed certificate is its own root
        Assert.Equal(certificate.Thumbprint, result.Signer!.Thumbprint);
        Assert.NotNull(result.ClaimedSigningTime);
        Assert.Equal((1, 1), (result.Revision, result.Revisions));
    }

    [Fact]
    public async Task An_untrusted_certificate_is_indeterminate()
    {
        using var certificate = SelfSigned();
        using var other = SelfSigned();
        var pdf = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256));

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [other] }));

        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.True(result.Intact);
        Assert.False(result.Trusted);
        Assert.Equal(PdfPadesLevel.None, result.PadesLevel);   // adbe.pkcs7.detached
    }

    // One changed byte in the signed range breaks the signature.
    [Fact]
    public async Task A_changed_byte_makes_the_signature_invalid()
    {
        using var certificate = SelfSigned();
        var pdf = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        int index = Latin1(pdf).IndexOf("PAdES verifier test", StringComparison.Ordinal);
        if (index < 0)
            index = 20;   // The text may be in a compressed stream; a byte of the header is signed too.
        pdf[index] ^= 0x01;

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Invalid, result.Status);
        Assert.False(result.Intact);
    }

    [Fact]
    public async Task A_signature_time_stamp_is_verified()
    {
        using var certificate = SelfSigned();
        using var authority = new TestTimeStampAuthority(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var pdf = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = authority.RespondAsync,
        });

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf,
            new PdfSignatureVerificationOptions { TrustedRoots = [certificate, authority.Certificate] }));

        Assert.Equal(PdfSignatureStatus.Valid, result.Status);
        Assert.Equal(authority.Time, result.TimeStampTime);
        Assert.Equal(authority.Certificate.Thumbprint, result.TimeStampAuthority!.Thumbprint);
        Assert.Equal(PdfPadesLevel.BaselineT, result.PadesLevel);
    }

    // B-T, then the DSS from AddValidationDataAsync, then a document time-stamp: B-LTA, checked against the PKI's root
    // with revocation data from the document only.
    [Fact]
    public async Task A_b_lta_signature_in_a_pki_is_valid_with_revocation_from_the_dss()
    {
        using var pki = new TestPki();
        var lta = await SignLta(pki);

        var results = await PdfSignatureVerifier.VerifyAsync(lta, new PdfSignatureVerificationOptions { TrustedRoots = [pki.Root] });

        Assert.Equal(2, results.Count);
        var signature = results[0];
        Assert.Equal(PdfSignatureStatus.Valid, signature.Status);
        Assert.Equal(PdfRevocationStatus.Good, signature.Revocation);
        Assert.Equal(PdfPadesLevel.BaselineLTA, signature.PadesLevel);
        Assert.Equal(3, signature.Chain.Count);
        Assert.False(signature.CoversWholeFile);
        Assert.Null(signature.ForbiddenChange);
        var stamp = results[1];
        Assert.Equal(PdfSignatureKind.DocumentTimeStamp, stamp.Kind);
        Assert.Equal(PdfSignatureStatus.Valid, stamp.Status);
        Assert.True(stamp.CoversWholeFile);
        Assert.Equal((3, 3), (stamp.Revision, stamp.Revisions));
    }

    // Sixty days on, the signer's 30-day certificate has expired; its time-stamp shows it was valid when signing.
    [Fact]
    public async Task An_expired_signer_is_valid_at_its_time_stamp()
    {
        using var pki = new TestPki();
        var lta = await SignLta(pki);

        var signature = (await PdfSignatureVerifier.VerifyAsync(lta, new PdfSignatureVerificationOptions
        {
            TrustedRoots = [pki.Root],
            ValidationTime = DateTimeOffset.UtcNow.AddDays(60),
        }))[0];

        Assert.Equal(PdfSignatureStatus.Valid, signature.Status);
        Assert.Contains(signature.Reasons, reason => reason.Contains("at the time-stamp"));
    }

    [Fact]
    public async Task A_revoked_signer_is_invalid()
    {
        using var pki = new TestPki();
        pki.Revoked.Add(pki.Signer.SerialNumberBytes.ToArray());
        var lta = await SignLta(pki);

        var signature = (await PdfSignatureVerifier.VerifyAsync(lta, new PdfSignatureVerificationOptions { TrustedRoots = [pki.Root] }))[0];

        Assert.Equal(PdfSignatureStatus.Invalid, signature.Status);
        Assert.Equal(PdfRevocationStatus.Revoked, signature.Revocation);
    }

    // B-T in a PKI without a DSS: no revocation data in the document.
    [Theory]
    [InlineData(PdfRevocationCheck.Document, PdfSignatureStatus.Indeterminate, PdfRevocationStatus.Unknown)]
    [InlineData(PdfRevocationCheck.None, PdfSignatureStatus.Valid, PdfRevocationStatus.NotChecked)]
    public async Task Without_revocation_data_the_status_depends_on_the_check(PdfRevocationCheck check, PdfSignatureStatus status,
        PdfRevocationStatus revocation)
    {
        using var pki = new TestPki();
        var bt = SignBT(pki);

        var signature = Assert.Single(await PdfSignatureVerifier.VerifyAsync(bt, new PdfSignatureVerificationOptions
        {
            TrustedRoots = [pki.Root],
            Revocation = check,
            Online = new PdfValidationDataOptions { ExtraCertificates = [pki.Intermediate] },
        }));

        Assert.Equal(status, signature.Status);
        Assert.Equal(revocation, signature.Revocation);
    }

    // Online: the verifier asks the PKI's responder for what the DSS does not hold.
    [Fact]
    public async Task Online_revocation_asks_the_responders()
    {
        using var pki = new TestPki();
        using var client = pki.CreateClient();
        var bt = SignBT(pki);

        var signature = Assert.Single(await PdfSignatureVerifier.VerifyAsync(bt, new PdfSignatureVerificationOptions
        {
            TrustedRoots = [pki.Root],
            Revocation = PdfRevocationCheck.Online,
            Online = new PdfValidationDataOptions { HttpClient = client, ExtraCertificates = [pki.Intermediate] },
        }));

        Assert.Equal(PdfSignatureStatus.Valid, signature.Status);
        Assert.Equal(PdfRevocationStatus.Good, signature.Revocation);
        Assert.True(pki.OcspRequests + pki.CrlRequests >= 2);
    }

    [Fact]
    public async Task A_countersigned_document_has_two_valid_signatures()
    {
        using var certificate = SelfSigned();
        var first = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        byte[] second;
        using (var document = PdfReader.Open(new MemoryStream(first), PdfDocumentOpenMode.Append))
        {
            DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades },
                new DigitalSignatureOptions { Rectangle = new XRect(36, 120, 160, 40), Reason = "Countersign" });
            using var output = new MemoryStream();
            document.Save(output);
            second = output.ToArray();
        }

        var results = await PdfSignatureVerifier.VerifyAsync(second, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(PdfSignatureStatus.Valid, r.Status));
        Assert.False(results[0].CoversWholeFile);
        Assert.True(results[1].CoversWholeFile);
    }

    // A later update that adds document information: P=2 permits it, P=1 does not.
    [Theory]
    [InlineData(PdfCertificationLevel.FormFilling, PdfSignatureStatus.Valid)]
    [InlineData(PdfCertificationLevel.NoChanges, PdfSignatureStatus.Invalid)]
    public async Task A_later_update_is_checked_against_the_certification(PdfCertificationLevel level, PdfSignatureStatus status)
    {
        using var certificate = SelfSigned();
        var certified = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades }, level);
        var updated = AppendInformationUpdate(certified, "Changed later");

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureKind.Certification, result.Kind);
        Assert.Equal((int)level, result.CertificationLevel);
        Assert.True(result.Intact);
        Assert.Equal(status, result.Status);
        if (status == PdfSignatureStatus.Invalid)
            Assert.Contains("document information", result.ForbiddenChange);
        else
            Assert.Null(result.ForbiddenChange);
    }

    // ===== Helpers =====

    static async Task<byte[]> SignLta(TestPki pki)
    {
        var bt = SignBT(pki);
        using var client = pki.CreateClient();
        byte[] lt;
        using (var document = PdfReader.Open(new MemoryStream(bt), PdfDocumentOpenMode.Append))
        {
            await PdfDocumentSecurityStore.AddValidationDataAsync(document,
                new PdfValidationDataOptions { HttpClient = client, ExtraCertificates = [pki.Root, pki.Intermediate] });
            using var output = new MemoryStream();
            await document.SaveAsync(output);
            lt = output.ToArray();
        }
        using var authority = new TestTimeStampAuthority(null, pki.TimeStampAuthority);
        using (var document = PdfReader.Open(new MemoryStream(lt), PdfDocumentOpenMode.Append))
        {
            DigitalSignatureHandler.ForDocument(document, new PdfTimeStampSigner(authority.RespondAsync), new DigitalSignatureOptions());
            using var output = new MemoryStream();
            await document.SaveAsync(output);
            return output.ToArray();
        }
    }

    static byte[] SignBT(TestPki pki)
    {
        using var authority = new TestTimeStampAuthority(null, pki.TimeStampAuthority);
        return Sign(new PdfSharpDefaultSigner(pki.Signer, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = authority.RespondAsync,
        });
    }

    static byte[] Sign(IDigitalSigner signer, PdfCertificationLevel? certification = null)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("PAdES verifier test", new XFont("FreePDF Synthetic", 12), XBrushes.Black, 36, 100);
        DigitalSignatureHandler.ForDocument(document, signer,
            new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test", Certification = certification });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // An incremental update written by hand (ISO 32000-1 7.5.6), which FreePDF itself would refuse at P=1: a new
    // document information dictionary.
    static byte[] AppendInformationUpdate(byte[] pdf, string title)
    {
        var text = Latin1(pdf);
        var startXref = Regex.Matches(text, @"startxref\s+(\d+)").Cast<Match>().Last().Groups[1].Value;
        var size = int.Parse(Regex.Matches(text, @"/Size\s+(\d+)").Cast<Match>().Last().Groups[1].Value);
        var root = Regex.Matches(text, @"/Root\s+(\d+\s+\d+\s+R)").Cast<Match>().Last().Groups[1].Value;
        var update = new StringBuilder();
        int objectOffset = pdf.Length + 1;
        update.Append('\n').Append($"{size} 0 obj\n<< /Title ({title}) >>\nendobj\n");
        int xrefOffset = pdf.Length + update.Length;
        update.Append($"xref\n{size} 1\n{objectOffset:D10} 00000 n\r\n");
        update.Append($"trailer\n<< /Size {size + 1} /Root {root} /Info {size} 0 R /Prev {startXref} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return pdf.Concat(Encoding.ASCII.GetBytes(update.ToString())).ToArray();
    }

    static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF verifier test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    static string Latin1(byte[] bytes) => new(bytes.Select(b => (char)b).ToArray());
}
#endif
