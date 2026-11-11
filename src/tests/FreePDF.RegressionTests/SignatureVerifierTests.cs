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

    // ===== Attacks and edge cases from the review =====

    // A time-stamp from an authority that is not trusted gives no time: the signature is not valid "as of" it.
    [Fact]
    public async Task A_time_stamp_from_an_untrusted_authority_is_not_used()
    {
        using var certificate = SelfSigned();
        using var authority = new TestTimeStampAuthority(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var pdf = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = authority.RespondAsync,
        });

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Null(result.TimeStampTime);
        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("signature time-stamp is not used"));
    }

    // An expired signer with a time-stamp made by anyone (here a self-made authority) is not valid.
    [Fact]
    public async Task An_expired_signer_is_not_rescued_by_an_untrusted_time_stamp()
    {
        using var pki = new TestPki();
        using var forger = new TestTimeStampAuthority();   // valid key, self-signed, not in the PKI
        var pdf = Sign(new PdfSharpDefaultSigner(pki.Signer, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = forger.RespondAsync,
        });

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions
        {
            TrustedRoots = [pki.Root],
            Revocation = PdfRevocationCheck.None,
            ValidationTime = DateTimeOffset.UtcNow.AddDays(60),
            Online = new PdfValidationDataOptions { ExtraCertificates = [pki.Intermediate] },
        }));

        Assert.NotEqual(PdfSignatureStatus.Valid, result.Status);
        Assert.False(result.Trusted);
        Assert.Null(result.TimeStampTime);
    }

    // RFC 3161: a time-stamp authority's certificate has the timeStamping extended key usage.
    [Fact]
    public async Task A_time_stamp_authority_without_the_time_stamping_usage_is_not_used()
    {
        using var certificate = SelfSigned();
        using var tsaKey = RSA.Create(2048);
        var tsaRequest = new CertificateRequest("CN=FreePDF not a TSA", tsaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        tsaRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var tsaCertificate = tsaRequest.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var authority = new TestTimeStampAuthority(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero), tsaCertificate);
        // .NET refuses such a token when it requests one, so this signer attaches it as other tools might.
        var pdf = Sign(new RawTimeStampSigner(certificate, authority));

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf,
            new PdfSignatureVerificationOptions { TrustedRoots = [certificate, tsaCertificate] }));

        // .NET's token reader already refuses such an authority; either way the time is not used.
        Assert.Null(result.TimeStampTime);
        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("time-stamp"));
    }

    // OCSP signed by a responder certificate: accepted when the CA issued it, not when it only carries the CA's name.
    [Theory]
    [InlineData(TestPki.OcspResponder.Delegated, PdfRevocationStatus.Good, PdfSignatureStatus.Valid)]
    [InlineData(TestPki.OcspResponder.ForgedDelegated, PdfRevocationStatus.Unknown, PdfSignatureStatus.Indeterminate)]
    public async Task A_delegated_ocsp_responder_must_be_issued_by_the_ca(TestPki.OcspResponder responder, PdfRevocationStatus revocation,
        PdfSignatureStatus status)
    {
        using var pki = new TestPki { Responder = responder };
        var lta = await SignLta(pki);

        var signature = (await PdfSignatureVerifier.VerifyAsync(lta, new PdfSignatureVerificationOptions { TrustedRoots = [pki.Root] }))[0];

        Assert.Equal(revocation, signature.Revocation);
        Assert.Equal(status, signature.Status);
    }

    // A later trailer that points at another catalog (the shadow attack): no changed object shows it.
    [Theory]
    [InlineData(null, PdfSignatureStatus.Indeterminate)]
    [InlineData(PdfCertificationLevel.FormFilling, PdfSignatureStatus.Invalid)]
    public async Task A_replaced_catalog_is_reported(PdfCertificationLevel? level, PdfSignatureStatus status)
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades }, level);
        var text = Latin1(signed);
        var pages = Regex.Matches(text, @"/Pages\s+(\d+\s+\d+\s+R)").Cast<Match>().Last().Groups[1].Value;
        var form = Regex.Matches(text, @"/AcroForm\s+(\d+\s+\d+\s+R)").Cast<Match>().Last().Groups[1].Value;
        var perms = Regex.Matches(text, @"/Perms\s+(<<[^>]*>>|\d+\s+\d+\s+R)").Cast<Match>().LastOrDefault()?.Groups[1].Value;
        int number = Size(signed);
        var updated = AppendUpdate(signed, [(number, $"<< /Type /Catalog /Pages {pages} /AcroForm {form}{(perms != null ? " /Perms " + perms : "")} >>")],
            root: $"{number} 0 R");

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.True(result.Intact);
        Assert.Equal(status, result.Status);
        Assert.Contains("catalog", (result.ForbiddenChange ?? result.UnexpectedChange)!);
    }

    // After an approval signature, ISO 32000 permits any update; one that changes the page content is not Valid.
    [Fact]
    public async Task Changed_page_content_after_an_approval_signature_is_reported()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var content = int.Parse(Regex.Match(Latin1(signed), @"/Contents\s*\[?\s*(\d+)\s+0\s+R").Groups[1].Value);
        const string stream = "0 0 1 rg 36 36 100 100 re f";
        var updated = AppendUpdate(signed, [(content, $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream")]);

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.True(result.Intact);
        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.NotNull(result.UnexpectedChange);
        Assert.Null(result.ForbiddenChange);
    }

    // A malformed signature dictionary is reported, and does not stop the check.
    [Fact]
    public async Task A_malformed_signature_dictionary_is_reported_not_thrown()
    {
        var pdf = TestPdf.Create([
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /SigFlags 3 >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [4 0 R] >>",
            "<< /FT /Sig /T (Broken) /V 5 0 R /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /P 3 0 R >>",
            "<< /Type 3 /SubFilter 5 /ByteRange [0 10 20 30] /Contents <00> /M 42 >>",
        ]);

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf));

        Assert.NotEqual(PdfSignatureStatus.Valid, result.Status);
        Assert.NotEmpty(result.Reasons);
    }

    // A certificate whose key usage does not include digital signatures does not make a valid signature.
    [Fact]
    public async Task A_certificate_not_for_signatures_is_not_valid()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF encryption only", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pdf = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("key usage"));
    }

    // B-LTA needs a document time-stamp from a trusted authority; an untrusted one leaves the signature at B-LT.
    [Fact]
    public async Task A_document_time_stamp_from_an_untrusted_authority_does_not_make_b_lta()
    {
        using var pki = new TestPki();
        using var outsider = new TestTimeStampAuthority();
        var lta = await SignLta(pki, outsider);

        var results = await PdfSignatureVerifier.VerifyAsync(lta, new PdfSignatureVerificationOptions { TrustedRoots = [pki.Root] });

        Assert.Equal(PdfPadesLevel.BaselineLT, results[0].PadesLevel);
        Assert.False(results[1].Trusted);
        Assert.NotEqual(PdfSignatureStatus.Valid, results[1].Status);
    }

    // ===== Second review round =====

    // After an approval signature, a comment is outside form filling and signing: it might cover what was signed.
    [Fact]
    public async Task A_comment_after_an_approval_signature_is_reported()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var commented = Update(signed, document => document.Pages[0].Annotations.Add(new PdfSharp.Pdf.Annotations.PdfTextAnnotation(document)
        {
            Contents = "Covering note",
            Rectangle = new PdfRectangle(new XRect(0, 0, 595, 842)),
        }));

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(commented, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.Contains("annotation", result.UnexpectedChange);
    }

    // A later signature field whose value is not an intact signature (here no /ByteRange) is reported, and the earlier
    // signature is no longer Valid.
    [Fact]
    public async Task A_later_field_without_an_intact_signature_is_reported()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var updated = Update(signed, document =>
        {
            var value = new PdfDictionary(document);
            value.Elements.SetName("/Type", "/Sig");
            value.Elements.SetName("/SubFilter", "/ETSI.CAdES.detached");
            document.Internals.AddObject(value);
            var field = new PdfDictionary(document);
            field.Elements.SetName("/FT", "/Sig");
            field.Elements.SetString("/T", "Overlay");
            field.Elements["/V"] = value.Reference;
            field.Elements.SetName("/Type", "/Annot");
            field.Elements.SetName("/Subtype", "/Widget");
            field.Elements["/Rect"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842));
            field.Elements["/P"] = document.Pages[0].Reference;
            document.Internals.AddObject(field);
            document.Pages[0].Elements.GetArray("/Annots")!.Elements.Add(field.Reference!);
            document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!.Elements.Add(field.Reference!);
        });

        var results = await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] });

        Assert.Equal(2, results.Count);
        Assert.Equal(PdfSignatureStatus.Invalid, results[1].Status);
        Assert.NotEqual(PdfSignatureStatus.Valid, results[0].Status);
    }

    // Revocation data in the DSS that says "revoked before the signature" wins over an earlier "good" answer.
    [Fact]
    public async Task A_revocation_in_the_dss_wins_over_a_good_answer()
    {
        using var pki = new TestPki();
        var lta = await SignLta(pki);
        pki.Revoked.Add(pki.Signer.SerialNumberBytes.ToArray());
        var withCrl = Update(lta, document => PdfDocumentSecurityStore.Add(document, crls: [pki.Crl(pki.Intermediate)]));

        var signature = (await PdfSignatureVerifier.VerifyAsync(withCrl, new PdfSignatureVerificationOptions { TrustedRoots = [pki.Root] }))[0];

        Assert.Equal(PdfRevocationStatus.Revoked, signature.Revocation);
        Assert.Equal(PdfSignatureStatus.Invalid, signature.Status);
    }

    // A later update that takes the signed field out of the form does not make the signature disappear.
    [Fact]
    public async Task A_signature_removed_from_the_form_is_still_reported()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var form = int.Parse(Regex.Matches(Latin1(signed), @"/AcroForm\s+(\d+)\s+0\s+R").Cast<Match>().Last().Groups[1].Value);
        var emptied = AppendUpdate(signed, [(form, "<< /Fields [] >>")]);

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(emptied, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Invalid, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("removed"));
    }

    // A DSS stream that inflates to more than the limit is not read (also with /DecodeParms, which sends other readers
    // through an unlimited decoder), and the check goes on.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dss_stream_that_inflates_without_limit_is_not_read(bool decodeParms)
    {
        using var pki = new TestPki();
        var lta = await SignLta(pki);
        byte[] bomb;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(buffer, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(new byte[64 * 1024 * 1024]);
            bomb = buffer.ToArray();
        }
        var updated = Update(lta, document =>
        {
            var stream = new PdfDictionary(document);
            stream.CreateStream(bomb);
            stream.Elements.SetName("/Filter", "/FlateDecode");
            if (decodeParms)
                stream.Elements["/DecodeParms"] = new PdfDictionary(document);
            document.Internals.AddObject(stream);
            var dss = document.Catalog.Elements.GetDictionary("/DSS")!;
            dss.Elements.GetArray("/Certs")!.Elements.Add(stream.Reference!);
        });

        var results = await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [pki.Root] });

        Assert.Equal(PdfSignatureStatus.Valid, results[0].Status);
    }

    // ===== Codex review =====

    // A later update adds a field whose new signature dictionary copies another signature's /ByteRange and /Contents:
    // its own entries are not signed, so it is not a signature.
    [Fact]
    public async Task A_copied_signature_dictionary_is_not_a_signature()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var updated = Update(signed, document =>
        {
            var original = (PdfDictionary)((PdfSharp.Pdf.Advanced.PdfReference)document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!
                .Elements.Select(f => ((PdfSharp.Pdf.Advanced.PdfReference)f).Value).Cast<PdfDictionary>().First().Elements["/V"]!).Value;
            var clone = new PdfDictionary(document);
            clone.Elements.SetName("/Type", "/Sig");
            clone.Elements.SetName("/Filter", "/Adobe.PPKLite");
            clone.Elements.SetName("/SubFilter", "/ETSI.CAdES.detached");
            clone.Elements.SetString("/Reason", "Approved by the board");
            var range = original.Elements.GetArray("/ByteRange")!;
            clone.Elements["/ByteRange"] = new PdfArray(document, range.Elements.Select(e => (PdfItem)new PdfInteger(((PdfInteger)e).Value)).ToArray());
            clone.Elements["/Contents"] = original.Elements["/Contents"]!;
            document.Internals.AddObject(clone);
            var field = new PdfDictionary(document);
            field.Elements.SetName("/FT", "/Sig");
            field.Elements.SetString("/T", "Board");
            field.Elements["/V"] = clone.Reference;
            field.Elements.SetName("/Type", "/Annot");
            field.Elements.SetName("/Subtype", "/Widget");
            field.Elements["/Rect"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(0), new PdfInteger(0));
            field.Elements["/P"] = document.Pages[0].Reference;
            document.Internals.AddObject(field);
            document.Pages[0].Elements.GetArray("/Annots")!.Elements.Add(field.Reference!);
            document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!.Elements.Add(field.Reference!);
        });

        var results = await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] });

        var copy = Assert.Single(results, r => r.FieldName == "Board");
        Assert.Equal(PdfSignatureStatus.Invalid, copy.Status);
        Assert.False(copy.Intact);
    }

    // RFC 5035: the signing-certificate attribute must name the signer's certificate.
    [Fact]
    public async Task A_signing_certificate_attribute_for_another_certificate_is_invalid()
    {
        using var certificate = SelfSigned();
        using var other = SelfSigned();
        var pdf = Sign(new BoundToOtherSigner(certificate, other));

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.True(result.Intact);
        Assert.Equal(PdfSignatureStatus.Invalid, result.Status);
        Assert.Contains(result.Reasons, r => r.Contains("another certificate"));
    }

    // A CA may drop an expired certificate from its CRLs, so a CRL issued after the signer's certificate expired does not
    // show that it was not revoked; one issued before does.
    [Theory]
    [InlineData(false, PdfSignatureStatus.Valid)]
    [InlineData(true, PdfSignatureStatus.Indeterminate)]
    public async Task A_crl_issued_after_the_certificate_expired_shows_nothing(bool afterExpiry, PdfSignatureStatus status)
    {
        using var pki = new TestPki();
        var bt = SignBT(pki);
        var issued = afterExpiry ? new DateTimeOffset(pki.Signer.NotAfter.ToUniversalTime()).AddDays(1) : DateTimeOffset.UtcNow;
        var crl = new CertificateRevocationListBuilder().Build(pki.Intermediate, 7, issued.AddDays(30), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1, issued);
        var withData = Update(bt, document => PdfDocumentSecurityStore.Add(document,
            [pki.Root.RawData, pki.Intermediate.RawData, pki.Signer.RawData, pki.TimeStampAuthority.RawData],
            crls: [crl, pki.Crl(pki.Root)]));

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(withData, new PdfSignatureVerificationOptions
        {
            TrustedRoots = [pki.Root],
            ValidationTime = new DateTimeOffset(pki.Signer.NotAfter.ToUniversalTime()).AddDays(2),
        }));

        Assert.Equal(status, result.Status);
    }

    // ===== Codex re-check =====

    // A field added later that points at the signed dictionary itself (with a page-sized widget) is not that signature.
    [Fact]
    public async Task A_later_field_pointing_at_a_signed_dictionary_is_not_a_signature()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var updated = Update(signed, document =>
        {
            var original = (PdfSharp.Pdf.Advanced.PdfReference)((PdfDictionary)((PdfSharp.Pdf.Advanced.PdfReference)document.Catalog.GetAcroForm()!
                .Elements.GetArray("/Fields")!.Elements[0]).Value).Elements["/V"]!;
            var field = new PdfDictionary(document);
            field.Elements.SetName("/FT", "/Sig");
            field.Elements.SetString("/T", "Again");
            field.Elements["/V"] = original;
            field.Elements.SetName("/Type", "/Annot");
            field.Elements.SetName("/Subtype", "/Widget");
            field.Elements["/Rect"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842));
            field.Elements["/P"] = document.Pages[0].Reference;
            document.Internals.AddObject(field);
            document.Pages[0].Elements.GetArray("/Annots")!.Elements.Add(field.Reference!);
            document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!.Elements.Add(field.Reference!);
        });

        var results = await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] });

        Assert.Equal(PdfSignatureStatus.Invalid, Assert.Single(results, r => r.FieldName == "Again").Status);
        Assert.NotEqual(PdfSignatureStatus.Valid, Assert.Single(results, r => r.FieldName != "Again").Status);
    }

    // RFC 3161 4: a time-stamp authority revoked for compromise, or for no stated reason, voids its tokens even when the
    // revocation came after them, so the expired signer is not rescued; one retired for another reason keeps them.
    [Theory]
    [InlineData(null, false)]
    [InlineData(X509RevocationReason.KeyCompromise, false)]
    [InlineData(X509RevocationReason.Unspecified, true)]
    [InlineData(X509RevocationReason.Superseded, true)]
    public async Task A_time_stamp_authority_revoked_later_keeps_its_tokens_only_when_retired(X509RevocationReason? reason, bool kept)
    {
        using var pki = new TestPki();
        var bt = SignBT(pki);
        pki.Revoked.Add(pki.TimeStampAuthority.SerialNumberBytes.ToArray());
        pki.RevocationTime = DateTimeOffset.UtcNow.AddMinutes(5);
        pki.RevocationReason = reason;
        using var client = pki.CreateClient();
        byte[] withData;
        using (var document = PdfReader.Open(new MemoryStream(bt), PdfDocumentOpenMode.Append))
        {
            await PdfDocumentSecurityStore.AddValidationDataAsync(document,
                new PdfValidationDataOptions { HttpClient = client, ExtraCertificates = [pki.Root, pki.Intermediate] });
            using var output = new MemoryStream();
            await document.SaveAsync(output);
            withData = output.ToArray();
        }

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(withData, new PdfSignatureVerificationOptions
        {
            TrustedRoots = [pki.Root],
            ValidationTime = new DateTimeOffset(pki.Signer.NotAfter.ToUniversalTime()).AddDays(2),
        }));

        if (kept)
        {
            Assert.NotNull(result.TimeStampTime);
            Assert.Equal(PdfSignatureStatus.Valid, result.Status);
        }
        else
        {
            Assert.Null(result.TimeStampTime);
            Assert.NotEqual(PdfSignatureStatus.Valid, result.Status);
            Assert.Contains(result.Reasons, r => r.Contains("tokens cannot be trusted"));
        }
    }

    // An empty signature field added later that shows something (here a page-sized appearance) may cover what was signed.
    [Fact]
    public async Task A_visible_empty_signature_field_added_later_is_reported()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var updated = Update(signed, document =>
        {
            var appearance = new PdfDictionary(document);
            appearance.Elements.SetName("/Type", "/XObject");
            appearance.Elements.SetName("/Subtype", "/Form");
            appearance.Elements["/BBox"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842));
            appearance.CreateStream("1 1 1 rg 0 0 595 842 re f"u8.ToArray());
            document.Internals.AddObject(appearance);
            var field = new PdfDictionary(document);
            field.Elements.SetName("/FT", "/Sig");
            field.Elements.SetString("/T", "Cover");
            field.Elements.SetName("/Type", "/Annot");
            field.Elements.SetName("/Subtype", "/Widget");
            field.Elements["/Rect"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842));
            field.Elements["/AP"] = new PdfDictionary(document) { Elements = { ["/N"] = appearance.Reference! } };
            field.Elements["/P"] = document.Pages[0].Reference;
            field.Elements.SetInteger("/F", 4);
            document.Internals.AddObject(field);
            document.Pages[0].Elements.GetArray("/Annots")!.Elements.Add(field.Reference!);
            document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!.Elements.Add(field.Reference!);
        });

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.Contains("visible signature field", result.UnexpectedChange);
    }

    // The same with the field type inherited from a grandparent field (ISO 32000-1 12.7.3.1).
    [Fact]
    public async Task A_visible_empty_signature_widget_that_inherits_its_type_is_reported()
    {
        using var certificate = SelfSigned();
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        var updated = Update(signed, document =>
        {
            var grandparent = new PdfDictionary(document);
            grandparent.Elements.SetName("/FT", "/Sig");
            grandparent.Elements.SetString("/T", "Group");
            document.Internals.AddObject(grandparent);
            var parent = new PdfDictionary(document);
            parent.Elements.SetString("/T", "Inner");
            parent.Elements["/Parent"] = grandparent.Reference;
            document.Internals.AddObject(parent);
            var widget = CoveringWidget(document);
            widget.Elements["/Parent"] = parent.Reference;
            parent.Elements["/Kids"] = new PdfArray(document, widget.Reference!);
            grandparent.Elements["/Kids"] = new PdfArray(document, parent.Reference!);
            document.Pages[0].Elements.GetArray("/Annots")!.Elements.Add(widget.Reference!);
            document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!.Elements.Add(grandparent.Reference!);
        });

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Indeterminate, result.Status);
        Assert.NotNull(result.UnexpectedChange);   // refused as a widget the rules do not take, or as a visible empty signature field
    }

    // An empty signature field that was there when signing gets a new appearance stream later: the widget dictionary is
    // unchanged, the stream it refers to is not.
    [Fact]
    public async Task A_new_appearance_for_an_existing_empty_signature_field_is_reported()
    {
        using var certificate = SelfSigned();
        PdfObjectID appearanceId;
        byte[] signed;
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("PAdES verifier test", new XFont("FreePDF Synthetic", 12), XBrushes.Black, 36, 100);
            var widget = CoveringWidget(document, appearance: "");
            appearanceId = ((PdfSharp.Pdf.Advanced.PdfReference)((PdfDictionary)widget.Elements["/AP"]!).Elements["/N"]!).ObjectID;
            page.Elements["/Annots"] = new PdfArray(document, widget.Reference!);
            document.Catalog.GetOrCreateAcroForm().Elements["/Fields"] = new PdfArray(document, widget.Reference!);
            DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades },
                new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test" });
            using var output = new MemoryStream();
            document.Save(output);
            signed = output.ToArray();
        }
        var updated = Update(signed, document =>
            ((PdfDictionary)document.Internals.GetObject(appearanceId)!).Stream!.Value = "1 1 1 rg 0 0 595 842 re f"u8.ToArray());

        var results = await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] });

        var signature = Assert.Single(results);
        Assert.Equal(PdfSignatureStatus.Indeterminate, signature.Status);
        Assert.Contains("visible signature field", signature.UnexpectedChange);
    }

    // An empty signature widget the size of the page, with an appearance stream that paints over it.
    static PdfDictionary CoveringWidget(PdfDocument document, string appearance = "1 1 1 rg 0 0 595 842 re f")
    {
        var stream = new PdfDictionary(document);
        stream.Elements.SetName("/Type", "/XObject");
        stream.Elements.SetName("/Subtype", "/Form");
        stream.Elements["/BBox"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842));
        stream.CreateStream(System.Text.Encoding.ASCII.GetBytes(appearance));
        document.Internals.AddObject(stream);
        var widget = new PdfDictionary(document);
        widget.Elements.SetName("/Type", "/Annot");
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements["/Rect"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842));
        widget.Elements["/AP"] = new PdfDictionary(document) { Elements = { ["/N"] = stream.Reference! } };
        widget.Elements["/P"] = document.Pages[0].Reference;
        widget.Elements.SetInteger("/F", 4);
        if (appearance.Length == 0)
        {
            widget.Elements.SetName("/FT", "/Sig");
            widget.Elements.SetString("/T", "Later");
        }
        document.Internals.AddObject(widget);
        return widget;
    }

    // Every signing-certificate attribute must name the signer: a matching v1 does not cover a v2 for another certificate.
    [Fact]
    public async Task A_second_signing_certificate_attribute_for_another_certificate_is_invalid()
    {
        using var certificate = SelfSigned();
        using var other = SelfSigned();
        var pdf = Sign(new BoundToOtherSigner(certificate, other, matchingV1: true));

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Invalid, result.Status);
    }

    // "endobj" inside a signed string is text, not the end of the signature dictionary's object.
    [Fact]
    public async Task A_signed_string_that_contains_endobj_does_not_confuse_the_check()
    {
        using var certificate = SelfSigned();
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("PAdES verifier test", new XFont("FreePDF Synthetic", 12), XBrushes.Black, 36, 100);
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades },
            new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), ContactInfo = "endobj@example.test", Reason = "endobj 3 0 obj" });
        using var output = new MemoryStream();
        document.Save(output);

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(output.ToArray(), new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));

        Assert.Equal(PdfSignatureStatus.Valid, result.Status);
    }

    // A page without a transparency group (as pyHanko and most producers write them) whose /Annots a countersignature
    // changes: comparing the page must not add the group FreePDF writes on full saves and then call that a change.
    [Fact]
    public async Task A_countersignature_on_a_page_without_a_transparency_group_keeps_the_first_valid()
    {
        using var certificate = SelfSigned();
        var plain = TestPdf.Create([
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>",
            "<< /Length 20 >>\nstream\n0 0 1 rg 0 0 9 9 re f\nendstream",
        ]);
        byte[] SignOnto(byte[] pdf, string reason)
            => Update(pdf, document => DigitalSignatureHandler.ForDocument(document,
                new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades },
                new DigitalSignatureOptions { Reason = reason }));
        var twice = SignOnto(SignOnto(plain, "First"), "Second");
        Assert.DoesNotContain("/Group", Latin1(twice));

        var results = await PdfSignatureVerifier.VerifyAsync(twice, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(PdfSignatureStatus.Valid, r.Status));
    }

    // A PAdES signer whose signing-certificate-v2 attribute names another certificate.
    sealed class BoundToOtherSigner(X509Certificate2 certificate, X509Certificate2 named, bool matchingV1 = false) : IDigitalSigner, IDigitalSignerFormat
    {
        public string CertificateName => certificate.Subject;
        public PdfSignatureFormat Format => PdfSignatureFormat.Pades;
        public async Task<int> GetSignatureSizeAsync() => (await GetSignatureAsync(new MemoryStream([0]))).Length + 64;

        public Task<byte[]> GetSignatureAsync(Stream stream)
        {
            var content = new byte[stream.Length];
            stream.Position = 0;
            stream.ReadExactly(content);
            var cms = new System.Security.Cryptography.Pkcs.SignedCms(new System.Security.Cryptography.Pkcs.ContentInfo(content), detached: true);
            var signer = new System.Security.Cryptography.Pkcs.CmsSigner(certificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") };
            if (matchingV1)
            {
                // RFC 2634 signing-certificate: ESSCertID with the SHA-1 hash of the signer's own certificate.
                var v1 = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
                using (v1.PushSequence())
                using (v1.PushSequence())
                using (v1.PushSequence())
                    v1.WriteOctetString(SHA1.HashData(certificate.RawData));
                signer.SignedAttributes.Add(new AsnEncodedData("1.2.840.113549.1.9.16.2.12", v1.Encode()));
            }
            var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
            using (writer.PushSequence())
            using (writer.PushSequence())
            using (writer.PushSequence())
                writer.WriteOctetString(SHA256.HashData(named.RawData));
            signer.SignedAttributes.Add(new AsnEncodedData("1.2.840.113549.1.9.16.2.47", writer.Encode()));
            cms.ComputeSignature(signer, true);
            return Task.FromResult(cms.Encode());
        }
    }

    static byte[] Update(byte[] pdf, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Append);
        change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // Bookmarks are compared as the file wrote them. Opening a revision must not rewrite their /Count: a later update that
    // changes only a /Count would vanish from the comparison, and an unchanged bookmark could look changed.
    [Fact]
    public async Task A_later_update_that_changes_only_a_bookmark_count_is_a_change()
    {
        using var certificate = SelfSigned();
        var pdf = SignWithBookmarks(certificate, PdfCertificationLevel.FormFilling);
        var text = Latin1(pdf);
        var number = Regex.Matches(text, @"/Outlines\s+(\d+)\s+0\s+R").Cast<Match>().Last().Groups[1].Value;
        var body = Regex.Matches(text, $@"(?s)(?<!\d){number} 0 obj\s*(.*?)\s*endobj").Cast<Match>().Last().Groups[1].Value;
        Assert.Matches(@"/Count\s+2\b", body);   // Chapter (open) and Section

        var updated = AppendUpdate(pdf, [(int.Parse(number), Regex.Replace(body, @"/Count\s+2\b", "/Count 99"))]);

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(updated, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));
        Assert.NotNull(result.ForbiddenChange);
        Assert.Equal(PdfSignatureStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Bookmarks_are_not_a_change_when_a_permitted_update_follows()
    {
        using var certificate = SelfSigned();
        var pdf = Update(SignWithBookmarks(certificate, PdfCertificationLevel.FormFilling),
            document => PdfDocumentSecurityStore.Add(document, [certificate.RawData]));

        var result = Assert.Single(await PdfSignatureVerifier.VerifyAsync(pdf, new PdfSignatureVerificationOptions { TrustedRoots = [certificate] }));
        Assert.Null(result.ForbiddenChange);
        Assert.Null(result.UnexpectedChange);
        Assert.Equal(PdfSignatureStatus.Valid, result.Status);
    }

    static byte[] SignWithBookmarks(X509Certificate2 certificate, PdfCertificationLevel certification)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        document.Outlines.Add("Chapter", page, true).Outlines.Add("Section", page);
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256),
            new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Certification = certification });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // ===== Helpers =====

    static async Task<byte[]> SignLta(TestPki pki, TestTimeStampAuthority? documentTimeStampAuthority = null)
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
            DigitalSignatureHandler.ForDocument(document, new PdfTimeStampSigner((documentTimeStampAuthority ?? authority).RespondAsync),
                new DigitalSignatureOptions());
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
        int number = Size(pdf);
        return AppendUpdate(pdf, [(number, $"<< /Title ({title}) >>")], info: $"{number} 0 R");
    }

    static int Size(byte[] pdf) => int.Parse(Regex.Matches(Latin1(pdf), @"/Size\s+(\d+)").Cast<Match>().Last().Groups[1].Value);

    // An incremental update written by hand: the objects, a cross-reference section for them, and a trailer that keeps
    // the file's /Root and /Info unless given.
    static byte[] AppendUpdate(byte[] pdf, (int Number, string Body)[] objects, string? root = null, string? info = null)
    {
        var text = Latin1(pdf);
        var startXref = Regex.Matches(text, @"startxref\s+(\d+)").Cast<Match>().Last().Groups[1].Value;
        root ??= Regex.Matches(text, @"/Root\s+(\d+\s+\d+\s+R)").Cast<Match>().Last().Groups[1].Value;
        info ??= Regex.Matches(text, @"/Info\s+(\d+\s+\d+\s+R)").Cast<Match>().LastOrDefault()?.Groups[1].Value;
        int size = Math.Max(Size(pdf), objects.Max(o => o.Number) + 1);
        var update = new StringBuilder("\n");
        var offsets = new List<(int Number, int Offset)>();
        foreach (var (number, body) in objects)
        {
            offsets.Add((number, pdf.Length + update.Length));
            update.Append($"{number} 0 obj\n{body}\nendobj\n");
        }
        int xrefOffset = pdf.Length + update.Length;
        update.Append("xref\n");
        foreach (var (number, offset) in offsets.OrderBy(o => o.Number))
            update.Append($"{number} 1\n{offset:D10} 00000 n\r\n");
        update.Append($"trailer\n<< /Size {size} /Root {root}{(info != null ? " /Info " + info : "")} /Prev {startXref} >>\n");
        update.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        return pdf.Concat(Encoding.ASCII.GetBytes(update.ToString())).ToArray();
    }

    // An adbe.pkcs7.detached signer that puts the authority's token into the signature-time-stamp attribute without
    // .NET's checks of the reply (which refuse an authority without the timeStamping usage).
    sealed class RawTimeStampSigner(X509Certificate2 certificate, TestTimeStampAuthority authority) : IDigitalSigner
    {
        public string CertificateName => certificate.Subject;

        public async Task<int> GetSignatureSizeAsync() => (await GetSignatureAsync(new MemoryStream([0]))).Length + 16384;

        public async Task<byte[]> GetSignatureAsync(Stream stream)
        {
            var content = new byte[stream.Length];
            stream.Position = 0;
            stream.ReadExactly(content);
            var cms = new System.Security.Cryptography.Pkcs.SignedCms(new System.Security.Cryptography.Pkcs.ContentInfo(content), detached: true);
            cms.ComputeSignature(new System.Security.Cryptography.Pkcs.CmsSigner(certificate) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") }, true);
            var signerInfo = cms.SignerInfos[0];
            var request = System.Security.Cryptography.Pkcs.Rfc3161TimestampRequest.CreateFromSignerInfo(signerInfo, HashAlgorithmName.SHA256,
                requestSignerCertificates: true);
            var reply = await authority.RespondAsync(request.Encode());
            // TimeStampResp ::= SEQUENCE { status PKIStatusInfo, timeStampToken ContentInfo }
            var sequence = new System.Formats.Asn1.AsnReader(reply, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
            sequence.ReadSequence();
            var token = sequence.ReadEncodedValue().ToArray();
            signerInfo.AddUnsignedAttribute(new AsnEncodedData("1.2.840.113549.1.9.16.2.14", token));
            return cms.Encode();
        }
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
