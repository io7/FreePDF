// PdfDocumentSecurityStore.AddValidationDataAsync (7.10.0): the chains of a document's signatures and time-stamps, an
// OCSP response or the CRLs for each certificate below the root, written into the DSS. Against TestPki, whose OCSP
// responder and CRLs are answered in-process. Original tests; MIT, see LICENSE.
#if NET8_0_OR_GREATER
using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class PadesRevocationTests
{
    static readonly DateTimeOffset StampTime = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public PadesRevocationTests() => TestFonts.Initialize();

    // The signer and the TSA get OCSP responses; the intermediate, which names only a CRL, gets the root's CRL.
    [Fact]
    public async Task The_chains_get_ocsp_responses_and_crls()
    {
        using var pki = new TestPki();
        var signed = SignBT(pki);

        var (pdf, report) = await AddValidationData(signed, pki);

        Assert.Empty(report.Problems);
        Assert.Equal(2, report.OcspResponses);
        Assert.Equal(1, report.Crls);
        Assert.Equal(2, pki.OcspRequests);
        Assert.Equal(1, pki.CrlRequests);
        Assert.Equal(signed, pdf.Take(signed.Length).ToArray());

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var dss = document.Catalog.Elements.GetDictionary("/DSS")!;
        var certificates = Streams(dss, "/Certs");
        foreach (var expected in new[] { pki.Root, pki.Intermediate, pki.Signer, pki.TimeStampAuthority })
            Assert.Contains(certificates, data => data.AsSpan().SequenceEqual(expected.RawData));
        Assert.Equal(4, certificates.Count);
        Assert.All(Streams(dss, "/OCSPs"), response => Assert.Equal(0, OcspStatus(response)));
        Assert.Equal(2, Streams(dss, "/OCSPs").Count);
        Assert.Single(Streams(dss, "/CRLs"));
    }

    [Fact]
    public async Task A_revoked_signer_is_reported_and_its_response_kept()
    {
        using var pki = new TestPki();
        pki.Revoked.Add(pki.Signer.SerialNumberBytes.ToArray());

        var (_, report) = await AddValidationData(SignBT(pki), pki);

        Assert.Contains(report.Problems, problem => problem.Contains("revoked") && problem.Contains("FreePDF test signer"));
        Assert.Equal(2, report.OcspResponses);
    }

    // A responder that fails: the certificate's CRL is used instead.
    [Fact]
    public async Task A_failing_responder_falls_back_to_the_crl()
    {
        using var pki = new TestPki { OcspStatus = HttpStatusCode.InternalServerError };

        var (_, report) = await AddValidationData(SignBT(pki), pki);

        Assert.Equal(0, report.OcspResponses);
        Assert.Equal(2, report.Crls);   // the intermediate's CRL (signer, TSA) and the root's (intermediate)
        Assert.Contains(report.Problems, problem => problem.Contains("500"));
        Assert.DoesNotContain(report.Problems, problem => problem.StartsWith("No revocation data"));
    }

    [Theory]
    [InlineData(TestPki.OcspAnswer.Unknown, "does not know")]
    [InlineData(TestPki.OcspAnswer.OtherCertificate, "no answer for the certificate")]
    public async Task An_unusable_ocsp_answer_falls_back_to_the_crl(TestPki.OcspAnswer answer, string problem)
    {
        using var pki = new TestPki { Answer = answer };

        var (_, report) = await AddValidationData(SignBT(pki), pki);

        Assert.Equal(0, report.OcspResponses);
        Assert.Equal(2, report.Crls);
        Assert.Contains(report.Problems, p => p.Contains(problem));
        Assert.DoesNotContain(report.Problems, p => p.StartsWith("No revocation data"));
    }

    [Fact]
    public async Task A_download_that_is_no_crl_is_not_taken()
    {
        using var pki = new TestPki { OcspStatus = HttpStatusCode.InternalServerError, CrlBody = [0x30, 0x03, 0x02, 0x01, 0x01] };

        var (_, report) = await AddValidationData(SignBT(pki), pki);

        Assert.Equal(0, report.Crls);
        Assert.Contains(report.Problems, p => p.Contains("is not a CRL"));
        Assert.Contains(report.Problems, p => p.StartsWith("No revocation data for 'CN=FreePDF test signer"));
    }

    [Fact]
    public async Task A_response_larger_than_the_limit_is_not_taken()
    {
        using var pki = new TestPki();
        using var document = PdfReader.Open(new MemoryStream(SignBT(pki)), PdfDocumentOpenMode.Append);
        using var client = pki.CreateClient();

        var report = await PdfDocumentSecurityStore.AddValidationDataAsync(document, new PdfValidationDataOptions
        {
            HttpClient = client,
            ExtraCertificates = [pki.Root, pki.Intermediate],
            MaxResponseSize = 100,
        });

        Assert.Equal(0, report.OcspResponses + report.Crls);
        Assert.Contains(report.Problems, p => p.Contains("more than 100 bytes"));
    }

    // The addresses come from the certificates; the default client does not go to loopback or private networks.
    [Fact]
    public async Task The_default_client_does_not_reach_private_addresses()
    {
        using var pki = new TestPki();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF private responder", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(["http://127.0.0.1:9/ocsp"], null));
        request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension(["http://10.0.0.1/ca.crl"]));
        using var issued = request.Create(pki.Intermediate, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), [0x01, 0x02]);
        using var certificate = issued.CopyWithPrivateKey(key);
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });
        using var document = PdfReader.Open(new MemoryStream(signed), PdfDocumentOpenMode.Append);

        var report = await PdfDocumentSecurityStore.AddValidationDataAsync(document,
            new PdfValidationDataOptions { ExtraCertificates = [pki.Root, pki.Intermediate] });

        Assert.Contains(report.Problems, p => p.Contains("http://127.0.0.1:9/ocsp") && p.Contains("not a public address"));
        Assert.Contains(report.Problems, p => p.Contains("http://10.0.0.1/ca.crl") && p.Contains("not a public address"));
    }

    // Without the intermediate and the root, the chain stops at the signer.
    [Fact]
    public async Task A_chain_without_its_root_is_reported()
    {
        using var pki = new TestPki();

        var (_, report) = await AddValidationData(SignBT(pki), pki, extra: []);

        Assert.Contains(report.Problems, problem => problem.Contains("not a root"));
    }

    // A self-signed signer is its own root: its certificate is added, nothing is fetched.
    [Fact]
    public async Task A_self_signed_signer_needs_no_revocation_data()
    {
        using var pki = new TestPki();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF self-signed", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signed = Sign(new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256) { Format = PdfSignatureFormat.Pades });

        var (_, report) = await AddValidationData(signed, pki, extra: []);

        Assert.Empty(report.Problems);
        Assert.Equal(1, report.Added);
        Assert.Equal(0, pki.OcspRequests + pki.CrlRequests);
    }

    // Validation data may be added under a certification that permits no changes.
    [Fact]
    public async Task Validation_data_is_added_to_a_document_certified_with_no_changes()
    {
        using var pki = new TestPki();
        var certified = SignBT(pki, PdfCertificationLevel.NoChanges);

        var (pdf, report) = await AddValidationData(certified, pki);

        Assert.Empty(report.Problems);
        Assert.Equal(certified, pdf.Take(certified.Length).ToArray());
    }

    static async Task<(byte[] Pdf, PdfValidationDataReport Report)> AddValidationData(byte[] signed, TestPki pki, X509Certificate2[]? extra = null)
    {
        using var document = PdfReader.Open(new MemoryStream(signed), PdfDocumentOpenMode.Append);
        using var client = pki.CreateClient();
        var report = await PdfDocumentSecurityStore.AddValidationDataAsync(document, new PdfValidationDataOptions
        {
            HttpClient = client,
            ExtraCertificates = extra ?? [pki.Root, pki.Intermediate],
        });
        using var output = new MemoryStream();
        await document.SaveAsync(output);
        return (output.ToArray(), report);
    }

    static byte[] SignBT(TestPki pki, PdfCertificationLevel? certification = null)
    {
        using var authority = new TestTimeStampAuthority(StampTime, pki.TimeStampAuthority);
        return Sign(new PdfSharpDefaultSigner(pki.Signer, PdfMessageDigestType.SHA256)
        {
            Format = PdfSignatureFormat.Pades,
            TimeStampClient = authority.RespondAsync,
        }, certification);
    }

    static byte[] Sign(IDigitalSigner signer, PdfCertificationLevel? certification = null)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("PAdES revocation test", new XFont("FreePDF Synthetic", 12), XBrushes.Black, 36, 100);
        DigitalSignatureHandler.ForDocument(document, signer,
            new DigitalSignatureOptions { Rectangle = new XRect(36, 36, 160, 40), Reason = "Test", Certification = certification });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static List<byte[]> Streams(PdfDictionary dss, string key)
    {
        var array = dss.Elements.GetArray(key);
        return array == null ? [] : Enumerable.Range(0, array.Elements.Count).Select(i => array.Elements.GetDictionary(i)!.Stream!.UnfilteredValue).ToList();
    }

    // The OCSPResponse's single response status: 0 good, 1 revoked, 2 unknown.
    static int OcspStatus(byte[] der)
    {
        var response = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
        response.ReadEnumeratedBytes();
        var bytes = response.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)).ReadSequence();
        bytes.ReadObjectIdentifier();
        var data = new AsnReader(bytes.ReadOctetString(), AsnEncodingRules.DER).ReadSequence().ReadSequence();
        data.ReadEncodedValue();
        data.ReadGeneralizedTime();
        var single = data.ReadSequence().ReadSequence();
        single.ReadSequence();
        return single.PeekTag().TagValue;
    }
}
#endif
