// Append-mode saves of documents whose signatures restrict later changes: a certification signature's DocMDP
// permission (ISO 32000-1 12.8.2.2) and fields locked by FieldMDP (12.8.2.4) or a signature field's /Lock dictionary
// (ISO 32000-2 Table 233). A save that would change more than they permit is refused before anything is written.
// The signatures in these synthetic fixtures are placeholders: the check reads their permissions, not their bytes.
// Original fixtures and a self-signed test identity; MIT, see LICENSE.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class CertificationPermissionTests
{
    // Objects: 1 catalog, 2 pages, 3 page, 4 AcroForm, 5 text field "name", 6 text field "address" with kid 12
    // "address.city", 7 comment, 8 signature field "approval" signed with signature 10, 9 font, 11 page content;
    // further objects from 13.
    static byte[] Fixture(string catalogEntries = "", string signatureEntries = "", string signatureFieldEntries = "",
        string nameFieldEntries = "", params string[] more)
        => TestPdf.Create(new[] {
            /* 1 */ $"<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R {catalogEntries} >>",
            /* 2 */ "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            /* 3 */ "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Contents 11 0 R /Annots [5 0 R 12 0 R 7 0 R 8 0 R] >>",
            /* 4 */ "<< /Fields [5 0 R 6 0 R 8 0 R] /SigFlags 3 /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 9 0 R >> >> >>",
            /* 5 */ $"<< /FT /Tx /T (name) /V (Old) /DA (/Helv 12 Tf 0 g) /Type /Annot /Subtype /Widget /Rect [20 350 220 370] /P 3 0 R /F 4 {nameFieldEntries} >>",
            /* 6 */ "<< /FT /Tx /T (address) /Kids [12 0 R] /DA (/Helv 12 Tf 0 g) >>",
            /* 7 */ "<< /Type /Annot /Subtype /Text /Rect [300 300 320 320] /Contents (Note) /P 3 0 R >>",
            /* 8 */ $"<< /FT /Sig /T (approval) /V 10 0 R /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /P 3 0 R /F 132 {signatureFieldEntries} >>",
            /* 9 */ "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            /* 10 */ $"<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00> {signatureEntries} >>",
            /* 11 */ "<< /Length 20 >>\nstream\n0 0 1 rg 0 0 9 9 re f\nendstream",
            /* 12 */ "<< /T (city) /Parent 6 0 R /Type /Annot /Subtype /Widget /Rect [20 320 220 340] /P 3 0 R /F 4 >>",
        }.Concat(more).ToArray());

    static PdfDictionary Object(PdfDocument document, int number)
        => (PdfDictionary)document.Internals.GetObject(new PdfObjectID(number))!;

    static byte[] Certified(int p) => Fixture("/Perms << /DocMDP 10 0 R >>",
        $"/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P {p} /V /1.2 >> >>]");

    static byte[] FieldMdp(string action, string fields) => Fixture(signatureEntries:
        $"/Reference [<< /Type /SigRef /TransformMethod /FieldMDP /TransformParams << /Type /TransformParams /Action /{action} {fields} /V /1.2 >> >>]");

    static byte[] Locked(string lockEntries) => Fixture(signatureFieldEntries: $"/Lock << /Type /SigFieldLock {lockEntries} >>");

    static PdfFormFields Fields(PdfDocument document) => document.Catalog.GetAcroForm()!.Fields;

    static readonly Action<PdfDocument> FillName = document => ((PdfFormTextField)Fields(document)["name"]!).Text = "Ada";
    static readonly Action<PdfDocument> FillCity = document => ((PdfFormTextField)Fields(document)["address.city"]!).Text = "Oslo";
    static readonly Action<PdfDocument> Comment = document => document.Pages[0].Annotations.Add(
        new PdfTextAnnotation(document) { Contents = "New", Rectangle = new PdfRectangle(new XRect(100, 100, 20, 20)) });
    static readonly Action<PdfDocument> ChangeComment = document =>
        ((PdfDictionary)document.Internals.GetObject(new PdfObjectID(7))!).Elements.SetString("/Contents", "Changed");
    static readonly Action<PdfDocument> RemoveComment = document =>
        document.Pages[0].Elements.GetArray("/Annots")!.Elements.RemoveAt(2);
    static readonly Action<PdfDocument> Draw = document => {
        using var gfx = XGraphics.FromPdfPage(document.Pages[0]);
        gfx.DrawRectangle(XBrushes.Red, 100, 100, 50, 50);
    };
    static readonly Action<PdfDocument> AddPage = document => document.AddPage();
    static readonly Action<PdfDocument> SetTitle = document => document.Info.Title = "New title";
    static readonly Action<PdfDocument> SetPageLayout = document => document.PageLayout = PdfPageLayout.TwoColumnLeft;

    static void Sign(PdfDocument document)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF permission test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256),
            new DigitalSignatureOptions { Rectangle = new XRect(250, 20, 120, 40), AppearanceHandler = new EmptyAppearance() });
    }

    static byte[] Save(byte[] original, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static void Allowed(byte[] original, Action<PdfDocument> change)
    {
        var result = Save(original, change);
        Assert.True(result.Length > original.Length);
        Assert.Equal(original, result.Take(original.Length).ToArray());
    }

    static InvalidOperationException Refused(byte[] original, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        change(document);
        using var output = new MemoryStream();
        var refused = Assert.Throws<InvalidOperationException>(() => document.Save(output));
        Assert.Equal(0, output.Length);
        return refused;
    }

    public static TheoryData<string, int, bool> Matrix() => new()
    {
        // change, P, allowed
        { "fill", 1, false }, { "fill", 2, true }, { "fill", 3, true },
        { "fill-kid", 1, false }, { "fill-kid", 2, true }, { "fill-kid", 3, true },
        { "sign", 1, false }, { "sign", 2, true }, { "sign", 3, true },
        { "title", 1, false }, { "title", 2, true }, { "title", 3, true },
        { "comment", 1, false }, { "comment", 2, false }, { "comment", 3, true },
        { "change-comment", 1, false }, { "change-comment", 2, false }, { "change-comment", 3, true },
        { "remove-comment", 1, false }, { "remove-comment", 2, false }, { "remove-comment", 3, true },
        { "draw", 1, false }, { "draw", 2, false }, { "draw", 3, false },
        { "add-page", 1, false }, { "add-page", 2, false }, { "add-page", 3, false },
        { "page-layout", 1, false }, { "page-layout", 2, false }, { "page-layout", 3, false },
    };

    static Action<PdfDocument> Change(string name) => name switch
    {
        "fill" => FillName,
        "fill-kid" => FillCity,
        "sign" => Sign,
        "title" => SetTitle,
        "comment" => Comment,
        "change-comment" => ChangeComment,
        "remove-comment" => RemoveComment,
        "draw" => Draw,
        "add-page" => AddPage,
        "page-layout" => SetPageLayout,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void A_certified_document_permits_what_its_level_permits(string change, int p, bool allowed)
    {
        if (allowed)
            Allowed(Certified(p), Change(change));
        else
            Refused(Certified(p), Change(change));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Saving_without_changes_keeps_the_bytes(int p)
    {
        var original = Certified(p);
        Assert.Equal(original, Save(original, _ => { }));
    }

    [Fact]
    public void A_certification_without_P_permits_form_filling()
    {
        var original = Fixture("/Perms << /DocMDP 10 0 R >>",
            "/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /V /1.2 >> >>]");
        Allowed(original, FillName);
        Refused(original, Comment);
    }

    [Fact]
    public void The_refusal_names_the_permission_and_the_change()
    {
        var refused = Refused(Certified(2), Draw);
        Assert.Contains("DocMDP", refused.Message);
        Assert.Contains("P=2", refused.Message);
        Assert.Contains("page 1", refused.Message);
        Assert.Contains("/Contents", refused.Message);
    }

    [Fact]
    public void Adding_a_text_field_is_refused_when_only_form_filling_is_permitted()
    {
        Refused(Certified(3), document => {
            var field = new PdfDictionary(document);
            document.Internals.AddObject(field);
            field.Elements.SetName("/FT", "/Tx");
            field.Elements.SetString("/T", "extra");
            field.Elements.SetName("/Type", "/Annot");
            field.Elements.SetName("/Subtype", "/Widget");
            field.Elements["/Rect"] = new PdfRectangle(new XRect(20, 200, 200, 20));
            document.Pages[0].Elements.GetArray("/Annots")!.Elements.Add(field.Reference!);
            document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!.Elements.Add(field.Reference!);
        });
    }

    [Theory]
    [InlineData("Include", "/Fields [(name)]", "fill", false)]
    [InlineData("Include", "/Fields [(name)]", "fill-kid", true)]
    [InlineData("Include", "/Fields [(address)]", "fill-kid", false)]
    [InlineData("Exclude", "/Fields [(name)]", "fill", true)]
    [InlineData("Exclude", "/Fields [(name)]", "fill-kid", false)]
    [InlineData("All", "", "fill", false)]
    [InlineData("All", "", "fill-kid", false)]
    [InlineData("All", "", "sign", true)]
    [InlineData("All", "", "comment", true)]
    public void Fields_locked_by_FieldMDP_cannot_change(string action, string fields, string change, bool allowed)
    {
        if (allowed)
            Allowed(FieldMdp(action, fields), Change(change));
        else
            Refused(FieldMdp(action, fields), Change(change));
    }

    [Theory]
    [InlineData("/Action /Include /Fields [(name)]", "fill", false)]
    [InlineData("/Action /Include /Fields [(name)]", "fill-kid", true)]
    [InlineData("/Action /All /P 1", "title", false)]
    [InlineData("/Action /Include /Fields [(name)] /P 2", "comment", false)]
    [InlineData("/Action /Include /Fields [(name)] /P 3", "comment", true)]
    public void Fields_locked_by_a_signature_field_lock_cannot_change(string lockEntries, string change, bool allowed)
    {
        if (allowed)
            Allowed(Locked(lockEntries), Change(change));
        else
            Refused(Locked(lockEntries), Change(change));
    }

    [Fact]
    public void Without_certification_or_locks_any_change_is_saved()
    {
        Allowed(Fixture(), Draw);
        Allowed(Fixture(), AddPage);
    }

    [Fact]
    public void A_refused_save_to_the_opened_file_leaves_the_file_unchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"freepdf-docmdp-{Guid.NewGuid():N}.pdf");
        var original = Certified(1);
        File.WriteAllBytes(path, original);
        try
        {
            using (var document = PdfReader.Open(path, PdfDocumentOpenMode.Append))
            {
                FillName(document);
                Assert.Throws<InvalidOperationException>(() => document.Save(path));
            }
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Review findings for PR #37.

    static void SetXmpMetadata(PdfDocument document)
    {
        var metadata = new PdfDictionary(document);
        document.Internals.AddObject(metadata);
        metadata.Elements.SetName("/Type", "/Metadata");
        metadata.Elements.SetName("/Subtype", "/XML");
        metadata.CreateStream(TestPdf.Latin1.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"));
        document.Catalog.Elements["/Metadata"] = metadata.Reference;
    }

    [Fact]
    public void A_permitted_catalog_change_in_a_document_with_an_outline_gets_no_page_mode()
    {
        // Saving added /PageMode /UseOutlines to the catalog of a document with an outline and no /PageMode.
        var original = Fixture("/Perms << /DocMDP 10 0 R >> /Outlines 13 0 R",
            "/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P 2 /V /1.2 >> >>]",
            more: new[] {
                "<< /Type /Outlines /First 14 0 R /Last 14 0 R /Count 1 >>",
                "<< /Title (One) /Parent 13 0 R /Dest [3 0 R /Fit] >>" });

        var result = Save(original, SetXmpMetadata);

        var update = TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
        Assert.Contains("/Metadata", update);
        Assert.DoesNotContain("/PageMode", update);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_signed_signature_field_cannot_be_emptied_or_changed(bool certified)
    {
        var original = certified ? Certified(2) : Fixture();
        Refused(original, document => Object(document, 8).Elements.Remove("/V"));
        Refused(original, document => Object(document, 8).Elements["/AP"] = new PdfDictionary(document));
        Refused(original, document => Object(document, 8).Elements.SetRectangle("/Rect", new PdfRectangle(new XRect(0, 0, 50, 50))));
    }

    [Fact]
    public void The_form_cannot_be_removed_or_replaced_at_P2()
    {
        Refused(Certified(2), document => document.Catalog.Elements.Remove("/AcroForm"));
        Refused(Certified(2), document => {
            var form = new PdfDictionary(document);
            document.Internals.AddObject(form);
            form.Elements["/Fields"] = new PdfArray(document, Object(document, 4).Elements.GetArray("/Fields")!.Elements[2]);
            document.Catalog.Elements["/AcroForm"] = form.Reference;
        });
    }

    [Fact]
    public void A_locked_field_cannot_be_removed_and_its_parent_cannot_change()
    {
        var name = FieldMdp("Include", "/Fields [(name)]");
        Refused(name, document => {
            Object(document, 4).Elements.GetArray("/Fields")!.Elements.RemoveAt(0);
            document.Pages[0].Elements.GetArray("/Annots")!.Elements.RemoveAt(0);
        });
        var city = FieldMdp("Include", "/Fields [(address.city)]");
        Refused(city, document => Object(document, 6).Elements.SetString("/T", "addr"));
        Refused(city, document => Object(document, 6).Elements.SetString("/V", "Oslo"));
        Allowed(city, FillName);
    }

    [Fact]
    public void A_locked_fields_indirect_appearance_cannot_change()
    {
        var original = Fixture(
            signatureEntries: "/Reference [<< /Type /SigRef /TransformMethod /FieldMDP /TransformParams << /Type /TransformParams /Action /Include /Fields [(name)] /V /1.2 >> >>]",
            nameFieldEntries: "/AP 13 0 R",
            more: new[] { "<< /N 14 0 R >>", "<< /Type /XObject /Subtype /Form /BBox [0 0 200 20] /Length 3 >>\nstream\nq Q\nendstream" });
        Refused(original, document => Object(document, 13).Elements["/N"] = new PdfDictionary(document));
        Refused(original, document => Object(document, 14).Stream!.Value = TestPdf.Latin1.GetBytes("0 g 0 0 9 9 re f"));
    }

    [Fact]
    public void An_indirect_permission_and_an_indirect_field_name_are_read()
    {
        var indirectP = Fixture("/Perms << /DocMDP 10 0 R >>",
            "/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P 13 0 R /V /1.2 >> >>]",
            more: new[] { "1" });
        Refused(indirectP, FillName);
        var indirectName = Fixture(signatureEntries:
            "/Reference [<< /Type /SigRef /TransformMethod /FieldMDP /TransformParams << /Type /TransformParams /Action /Include /Fields [13 0 R] /V /1.2 >> >>]",
            more: new[] { "(name)" });
        Refused(indirectName, FillName);
    }

    [Fact]
    public void A_save_to_a_path_that_cannot_be_written_can_be_retried()
    {
        var target = Path.Combine(Path.GetTempPath(), $"freepdf-retry-{Guid.NewGuid():N}.pdf");
        var original = Certified(2);
        try
        {
            using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
            FillName(document);
            Assert.Throws<DirectoryNotFoundException>(() => document.Save(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "out.pdf")));
            document.Save(target);
            Assert.Equal(original, File.ReadAllBytes(target).Take(original.Length).ToArray());
        }
        finally
        {
            File.Delete(target);
        }
    }

    // The default appearance draws text, which would resolve fonts before TestFonts installs its resolver.
    sealed class EmptyAppearance : IAnnotationAppearanceHandler
    {
        public void DrawAppearance(XGraphics gfx, XRect rect) { }
    }
}
