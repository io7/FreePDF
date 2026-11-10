// Certifying a document when signing (7.9.0): DigitalSignatureOptions.Certification writes a DocMDP signature
// reference and the catalog's /Perms /DocMDP (ISO 32000-1 12.8.2.2), which FreePDF's own Append-mode permission check
// then enforces. pyHanko's validation of such files is part of the PR's evidence. Original fixtures and a self-signed
// test identity; MIT, see LICENSE.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class CertificationSigningTests
{
    public CertificationSigningTests() => TestFonts.Initialize();

    static byte[] FormBytes(Action<PdfFormSignatureField>? configure = null)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var form = document.Catalog.GetOrCreateAcroForm();
        form.AddTextField("name", page, new XRect(50, 50, 200, 20));
        var signature = form.AddSignatureField("author", page, new XRect(50, 100, 200, 50));
        configure?.Invoke(signature);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static byte[] Certify(byte[] pdf, PdfCertificationLevel level, string field = "author")
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256),
            new DigitalSignatureOptions { FieldName = field, ContactInfo = "forms@example.com", Certification = level });
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static PdfDictionary SignatureDictionary(PdfDocument document, string field)
        => (PdfDictionary)((PdfReference)document.Catalog.GetAcroForm()!.Fields[field]!.Elements["/V"]!).Value;

    [Theory]
    [InlineData(PdfCertificationLevel.NoChanges, 1)]
    [InlineData(PdfCertificationLevel.FormFilling, 2)]
    [InlineData(PdfCertificationLevel.FormFillingAndAnnotations, 3)]
    public void A_certification_signature_records_its_permission(PdfCertificationLevel level, int p)
    {
        using var certified = PdfReader.Open(new MemoryStream(Certify(FormBytes(), level)), PdfDocumentOpenMode.Import);

        var signature = SignatureDictionary(certified, "author");
        var perms = certified.Catalog.Elements.GetDictionary("/Perms")!;
        Assert.Same(signature, ((PdfReference)perms.Elements["/DocMDP"]!).Value);
        var reference = (PdfDictionary)signature.Elements.GetArray("/Reference")!.Elements.Single();
        Assert.Equal("/SigRef", reference.Elements.GetName("/Type"));
        Assert.Equal("/DocMDP", reference.Elements.GetName("/TransformMethod"));
        var parameters = reference.Elements.GetDictionary("/TransformParams")!;
        Assert.Equal(p, parameters.Elements.GetInteger("/P"));
        Assert.Equal("/1.2", parameters.Elements.GetName("/V"));
        Assert.Equal("forms@example.com", signature.Elements.GetString("/ContactInfo"));
    }

    // FreePDF's own permission check enforces the certification it wrote: form filling is allowed at P=2,
    // refused at P=1.
    [Theory]
    [InlineData(PdfCertificationLevel.NoChanges, false)]
    [InlineData(PdfCertificationLevel.FormFilling, true)]
    public void Append_mode_honours_the_new_certification(PdfCertificationLevel level, bool allowed)
    {
        var certified = Certify(FormBytes(), level);

        using var document = PdfReader.Open(new MemoryStream(certified), PdfDocumentOpenMode.Append);
        ((PdfFormTextField)document.Catalog.GetAcroForm()!.Fields["name"]!).Text = "Filled";
        using var output = new MemoryStream();
        if (allowed)
        {
            document.Save(output);
            Assert.True(output.Length > certified.Length);
        }
        else
            Assert.Throws<InvalidOperationException>(() => document.Save(output));
    }

    // A field lock and a certification together give two signature references; the lock's reference replaced the
    // whole /Reference array before.
    [Fact]
    public void A_locked_field_keeps_both_references()
    {
        var pdf = FormBytes(field =>
        {
            var fieldLock = new PdfDictionary(field.Owner);
            fieldLock.Elements.SetName("/Type", "/SigFieldLock");
            fieldLock.Elements.SetName("/Action", "/All");
            field.Owner.Internals.AddObject(fieldLock);
            field.Elements["/Lock"] = fieldLock.Reference;
        });

        using var certified = PdfReader.Open(new MemoryStream(Certify(pdf, PdfCertificationLevel.FormFilling)), PdfDocumentOpenMode.Import);

        var methods = SignatureDictionary(certified, "author").Elements.GetArray("/Reference")!.Elements
            .Select(item => ((PdfDictionary)item).Elements.GetName("/TransformMethod")).OrderBy(m => m).ToList();
        Assert.Equal(new[] { "/DocMDP", "/FieldMDP" }, methods);
    }

    // Only the first signature can certify a document.
    [Fact]
    public void A_signed_document_cannot_be_certified()
    {
        var pdf = FormBytes();
        using (var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify))
        {
            document.Catalog.GetOrCreateAcroForm().AddSignatureField("second", document.Pages[0], new XRect(50, 200, 200, 50));
            using var output = new MemoryStream();
            document.Save(output);
            pdf = output.ToArray();
        }
        var signedOnce = Certify(pdf, PdfCertificationLevel.FormFillingAndAnnotations);

        Assert.Throws<InvalidOperationException>(() => Certify(signedOnce, PdfCertificationLevel.FormFilling, "second"));
    }

    [Fact]
    public void An_invalid_level_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Certify(FormBytes(), (PdfCertificationLevel)4));

    static readonly X509Certificate2 Certificate = CreateCertificate();

    static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF certification test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
