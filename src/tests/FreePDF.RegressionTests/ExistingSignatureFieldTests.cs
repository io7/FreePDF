// Signing an existing, empty signature field (DigitalSignatureOptions.FieldName) instead of adding a new one: the
// field gets the signature as its value and its widgets show it, its lock is recorded in the signature (FieldMDP),
// its required seed values are checked, and Append-mode permissions apply. Original fixtures and a self-signed
// test identity; MIT, see LICENSE.
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class ExistingSignatureFieldTests
{
    // The default signature appearance draws text: the test font resolver must be installed before any font is used.
    public ExistingSignatureFieldTests() => TestFonts.Initialize();

    // Objects: 1 catalog, 2 pages, 3 page, 4 AcroForm, 5 text field "name", 6 font, 7 page content, 8 empty signature
    // field "approval" (field and widget in one), 9 empty signature field "witness" with widget 10 that has a "sign
    // here" appearance 12, 11 invisible empty signature field "hidden"; further objects from 13.
    static byte[] Fixture(string approvalEntries = "", string catalogEntries = "", string moreFields = "",
        string moreAnnots = "", params string[] more)
        => TestPdf.Create(new[] {
            /* 1 */ $"<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R {catalogEntries} >>",
            /* 2 */ "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            /* 3 */ $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Contents 7 0 R /Annots [5 0 R 8 0 R 10 0 R 11 0 R {moreAnnots}] >>",
            /* 4 */ $"<< /Fields [5 0 R 8 0 R 9 0 R 11 0 R {moreFields}] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 6 0 R >> >> >>",
            /* 5 */ "<< /FT /Tx /T (name) /V (Old) /DA (/Helv 12 Tf 0 g) /Type /Annot /Subtype /Widget /Rect [20 350 220 370] /P 3 0 R /F 4 >>",
            /* 6 */ "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            /* 7 */ "<< /Length 20 >>\nstream\n0 0 1 rg 0 0 9 9 re f\nendstream",
            /* 8 */ $"<< /FT /Sig /T (approval) /Type /Annot /Subtype /Widget /Rect [250 20 370 60] /P 3 0 R /F 4 {approvalEntries} >>",
            /* 9 */ "<< /FT /Sig /T (witness) /Kids [10 0 R] >>",
            /* 10 */ "<< /Parent 9 0 R /Type /Annot /Subtype /Widget /Rect [20 20 140 60] /P 3 0 R /F 4 /AP << /N 12 0 R /D 12 0 R >> >>",
            /* 11 */ "<< /FT /Sig /T (hidden) /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /P 3 0 R /F 4 >>",
            /* 12 */ "<< /Type /XObject /Subtype /Form /BBox [0 0 120 40] /Length 3 >>\nstream\nq Q\nendstream",
        }.Concat(more).ToArray());

    // A certification signature (placeholder bytes: the permission check reads its permission, not its bytes) in
    // field "author", object 14, with signature dictionary 13.
    static byte[] Certified(int p, string authorReference = "")
        => Fixture(catalogEntries: "/Perms << /DocMDP 13 0 R >>", moreFields: "14 0 R", moreAnnots: "14 0 R", more: new[] {
            /* 13 */ "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00> " +
                $"/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P {p} /V /1.2 >> >> {authorReference}] >>",
            /* 14 */ "<< /FT /Sig /T (author) /V 13 0 R /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /P 3 0 R /F 132 >>",
        });

    static readonly X509Certificate2 Certificate = CreateCertificate();

    static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF existing field test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // The certificate keeps a copy of the key.
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    static void Sign(PdfDocument document, DigitalSignatureOptions options)
        => DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256), options);

    static Action<PdfDocument> SignField(string name, string reason = "")
        => document => Sign(document, new DigitalSignatureOptions { FieldName = name, Reason = reason });

    static byte[] Save(byte[] original, Action<PdfDocument> change, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Append)
    {
        using var document = PdfReader.Open(new MemoryStream(original), mode);
        change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static PdfDictionary Object(PdfDocument document, int number)
        => (PdfDictionary)document.Internals.GetObject(new PdfObjectID(number))!;

    static PdfDocument Open(byte[] file) => PdfReader.Open(new MemoryStream(file), PdfDocumentOpenMode.Import);

    // The last signature in the file is valid and covers all of it.
    static void AssertLastSignatureCoversTheFile(byte[] file)
    {
        var match = Regex.Matches(TestPdf.Latin1.GetString(file), @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]").Cast<Match>().Last();
        var range = Enumerable.Range(1, 4).Select(group => long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(file.Length, range[2] + range[3]);
        var signed = file.Take((int)range[1]).Concat(file.Skip((int)range[2]).Take((int)range[3])).ToArray();
        var hex = TestPdf.Latin1.GetString(file, (int)range[1] + 1, (int)(range[2] - range[1] - 2));
        var der = Enumerable.Range(0, hex.Length / 2).Select(index => Convert.ToByte(hex.Substring(2 * index, 2), 16)).ToArray();
        var cms = new SignedCms(new ContentInfo(signed), true);
        cms.Decode(der.Take(DerLength(der)).ToArray());
        cms.CheckSignature(true);
    }

    static int DerLength(byte[] der)
    {
        if (der[1] < 0x80)
            return 2 + der[1];
        var lengthBytes = der[1] & 0x7F;
        var length = 0;
        for (var index = 0; index < lengthBytes; index++)
            length = (length << 8) | der[2 + index];
        return 2 + lengthBytes + length;
    }

    static List<string> FieldNames(PdfDocument document)
        => document.Catalog.GetAcroForm()!.Fields.GetAllFields().Select(field => field.FullName).ToList();

    [Theory]
    [InlineData("approval", 8, 8)]
    [InlineData("witness", 9, 10)]
    public void The_named_field_is_signed_in_place(string name, int field, int widget)
    {
        var original = Fixture();
        var result = Save(original, SignField(name));

        Assert.Equal(original, result.Take(original.Length).ToArray());
        AssertLastSignatureCoversTheFile(result);
        using var before = Open(original);
        using var document = Open(result);
        Assert.Equal(FieldNames(before), FieldNames(document));
        Assert.Equal(4, document.Pages[0].Elements.GetArray("/Annots")!.Elements.Count);
        Assert.Equal("/Sig", Object(document, field).Elements.GetDictionary("/V")!.Elements.GetName("/Type"));
        var appearance = Object(document, widget).Elements.GetDictionary("/AP")!;
        Assert.Equal(new[] { "/N" }, appearance.Elements.Keys);
        Assert.Equal("/Form", appearance.Elements.GetDictionary("/N")!.Elements.GetName("/Subtype"));
        Assert.Equal(3, document.Catalog.GetAcroForm()!.Elements.GetInteger("/SigFlags"));
    }

    [Fact]
    public void An_invisible_field_is_signed_without_an_appearance()
    {
        var result = Save(Fixture(), SignField("hidden"));

        AssertLastSignatureCoversTheFile(result);
        using var document = Open(result);
        Assert.NotNull(Object(document, 11).Elements.GetDictionary("/V"));
        Assert.False(Object(document, 11).Elements.ContainsKey("/AP"));
    }

    [Fact]
    public void A_new_document_in_Modify_mode_signs_the_existing_field_too()
    {
        var result = Save(Fixture(), SignField("approval"), PdfDocumentOpenMode.Modify);

        AssertLastSignatureCoversTheFile(result);
        using var document = Open(result);
        Assert.Equal(new[] { "name", "approval", "witness", "hidden" }, FieldNames(document));
        Assert.NotNull(document.Catalog.GetAcroForm()!.Fields["approval"]!.Elements.GetDictionary("/V"));
    }

    [Fact]
    public void Mistakes_are_reported_when_the_handler_is_created()
    {
        using var document = PdfReader.Open(new MemoryStream(Fixture()), PdfDocumentOpenMode.Append);
        Assert.Throws<ArgumentException>(() => Sign(document, new DigitalSignatureOptions { FieldName = "missing" }));
        Assert.Throws<ArgumentException>(() => Sign(document, new DigitalSignatureOptions { FieldName = "name" }));
        Assert.Throws<ArgumentException>(() => Sign(document,
            new DigitalSignatureOptions { FieldName = "approval", Rectangle = new XRect(0, 0, 100, 40) }));
        Assert.Throws<ArgumentException>(() => Sign(document, new DigitalSignatureOptions { FieldName = "approval", PageIndex = 1 }));

        // A rejected handler is not kept: the document can still be signed.
        SignField("approval")(document);
        using var output = new MemoryStream();
        document.Save(output);
        AssertLastSignatureCoversTheFile(output.ToArray());
    }

    [Fact]
    public void A_signed_field_cannot_be_signed_again()
    {
        var signed = Save(Fixture(), SignField("approval"));

        using var document = PdfReader.Open(new MemoryStream(signed), PdfDocumentOpenMode.Append);
        Assert.Throws<InvalidOperationException>(() => SignField("approval")(document));
    }

    [Fact]
    public void A_field_with_fields_below_it_cannot_be_named()
    {
        var original = Fixture(moreFields: "13 0 R", moreAnnots: "14 0 R", more: new[] {
            /* 13 */ "<< /FT /Sig /T (group) /Kids [14 0 R] >>",
            /* 14 */ "<< /T (member) /Parent 13 0 R /Type /Annot /Subtype /Widget /Rect [150 100 270 140] /P 3 0 R /F 4 >>",
        });

        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        Assert.Throws<ArgumentException>(() => SignField("group")(document));
        SignField("group.member")(document);
    }

    [Fact]
    public void A_lock_is_recorded_in_the_signature_and_then_enforced()
    {
        var original = Fixture("/Lock << /Type /SigFieldLock /Action /Include /Fields [(name)] >>");
        Save(original, document => ((PdfFormTextField)document.Catalog.GetAcroForm()!.Fields["name"]!).Text = "Before");

        var signed = Save(original, SignField("approval"));

        AssertLastSignatureCoversTheFile(signed);
        using (var document = Open(signed))
        {
            var reference = Object(document, 8).Elements.GetDictionary("/V")!.Elements.GetArray("/Reference")!.Elements.GetDictionary(0)!;
            Assert.Equal("/SigRef", reference.Elements.GetName("/Type"));
            Assert.Equal("/FieldMDP", reference.Elements.GetName("/TransformMethod"));
            Assert.Equal(document.Catalog.Reference!.ObjectID, ((PdfReference)reference.Elements["/Data"]!).ObjectID);
            var parameters = reference.Elements.GetDictionary("/TransformParams")!;
            Assert.Equal("/Include", parameters.Elements.GetName("/Action"));
            Assert.Equal("name", parameters.Elements.GetArray("/Fields")!.Elements.GetString(0));
            Assert.Equal("/1.2", parameters.Elements.GetName("/V"));
            Assert.False(parameters.Elements.ContainsKey("/P"));
        }

        using var again = PdfReader.Open(new MemoryStream(signed), PdfDocumentOpenMode.Append);
        ((PdfFormTextField)again.Catalog.GetAcroForm()!.Fields["name"]!).Text = "After";
        Assert.Throws<InvalidOperationException>(() => again.Save(new MemoryStream()));
    }

    [Fact]
    public void A_lock_with_a_document_permission_passes_it_on()
    {
        var signed = Save(Fixture("/Lock << /Type /SigFieldLock /Action /All /P 2 >>"), SignField("approval"));

        using var document = Open(signed);
        var parameters = Object(document, 8).Elements.GetDictionary("/V")!.Elements.GetArray("/Reference")!
            .Elements.GetDictionary(0)!.Elements.GetDictionary("/TransformParams")!;
        Assert.Equal("/All", parameters.Elements.GetName("/Action"));
        Assert.False(parameters.Elements.ContainsKey("/Fields"));
        Assert.Equal(2, parameters.Elements.GetInteger("/P"));
    }

    [Fact]
    public void A_lock_without_a_valid_action_is_refused()
    {
        using var document = PdfReader.Open(new MemoryStream(Fixture("/Lock << /Type /SigFieldLock /Fields [(name)] >>")),
            PdfDocumentOpenMode.Append);
        Assert.Throws<InvalidOperationException>(() => SignField("approval")(document));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void A_certification_permits_signing_an_existing_field_from_P2(int p, bool allowed)
    {
        var original = Certified(p);
        if (allowed)
        {
            AssertLastSignatureCoversTheFile(Save(original, SignField("approval")));
            return;
        }
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        SignField("approval")(document);
        Assert.Throws<InvalidOperationException>(() => document.Save(new MemoryStream()));
    }

    [Fact]
    public void A_field_that_an_earlier_signature_locks_cannot_be_signed()
    {
        var original = Certified(2,
            "<< /Type /SigRef /TransformMethod /FieldMDP /TransformParams << /Type /TransformParams /Action /Include /Fields [(approval)] /V /1.2 >> >>");

        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        SignField("approval")(document);
        Assert.Throws<InvalidOperationException>(() => document.Save(new MemoryStream()));
        AssertLastSignatureCoversTheFile(Save(original, SignField("witness")));
    }

    [Fact]
    public void Required_reasons_must_be_met()
    {
        var original = Fixture("/SV << /Type /SV /Reasons [(Approved) (Reviewed)] /Ff 8 >>");

        AssertLastSignatureCoversTheFile(Save(original, SignField("approval", "Reviewed")));
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        var refused = Assert.Throws<ArgumentException>(() => SignField("approval", "Because")(document));
        Assert.Contains("'Approved', 'Reviewed'", refused.Message);
    }

    [Theory]
    [InlineData("/SubFilter [/ETSI.CAdES.detached] /Ff 2")]
    [InlineData("/DigestMethod [/SHA512] /Ff 64")]
    [InlineData("/MDP << /P 4 >>")]
    [InlineData("/TimeStamp << /URL (http://tsa.example) /Ff 1 >>")]
    [InlineData("/Cert << /Ff 1 /Subject [] >>")]
    public void Required_seed_values_that_cannot_be_met_are_refused(string seedValues)
    {
        using var document = PdfReader.Open(new MemoryStream(Fixture($"/SV << /Type /SV {seedValues} >>")), PdfDocumentOpenMode.Append);
        Assert.Throws<NotSupportedException>(() => SignField("approval")(document));
    }

    // ISO 32000-2 Table 237: with the Reasons bit set, no listed reason or a single period means that the signature
    // has no /Reason; otherwise one of the listed reasons, an empty string included, is required.
    [Theory]
    [InlineData("/Reasons [(.)] /Ff 8", "", false)]
    [InlineData("/Ff 8", "", false)]
    [InlineData("/Reasons [] /Ff 8", "", false)]
    [InlineData("/Reasons [()] /Ff 8", "", true)]
    [InlineData("/Reasons [(.)]", "Approved", true)]
    public void Required_reasons_follow_the_period_convention(string seedValues, string reason, bool hasReason)
    {
        var signed = Save(Fixture($"/SV << /Type /SV {seedValues} >>"), SignField("approval", reason));

        AssertLastSignatureCoversTheFile(signed);
        using var document = Open(signed);
        var signature = Object(document, 8).Elements.GetDictionary("/V")!;
        Assert.Equal(hasReason, signature.Elements.ContainsKey("/Reason"));
    }

    [Theory]
    [InlineData("/Reasons [(.)] /Ff 8")]
    [InlineData("/Ff 8")]
    public void A_reason_is_refused_where_the_field_allows_none(string seedValues)
    {
        using var document = PdfReader.Open(new MemoryStream(Fixture($"/SV << /Type /SV {seedValues} >>")), PdfDocumentOpenMode.Append);
        Assert.Throws<ArgumentException>(() => SignField("approval", "Approved")(document));
    }

    [Theory]
    [InlineData("/SubFilter [/ETSI.CAdES.detached]")]
    [InlineData("/SubFilter [/ETSI.CAdES.detached /adbe.pkcs7.detached] /Ff 2")]
    [InlineData("/Reasons [(Approved)] /DigestMethod [/SHA512]")]
    [InlineData("/MDP << /P 0 >> /TimeStamp << /URL (http://tsa.example) >>")]
    [InlineData("/LegalAttestation [(No dynamic content)] /Ff 16")]
    public void Optional_or_met_seed_values_do_not_stop_signing(string seedValues)
    {
        AssertLastSignatureCoversTheFile(Save(Fixture($"/SV << /Type /SV {seedValues} >>"), SignField("approval")));
    }

    // A seed /MDP /P names the signature the field takes (ISO 32000-2 Table 237), whatever /Ff says: 0 an approval
    // signature, 1 to 3 a certification with that permission level. Mistakes surface when the handler is made.
    [Theory]
    [InlineData(0, null, true)]
    [InlineData(0, PdfCertificationLevel.FormFilling, false)]
    [InlineData(2, PdfCertificationLevel.FormFilling, true)]
    [InlineData(2, null, false)]
    [InlineData(2, PdfCertificationLevel.NoChanges, false)]
    public void A_seed_MDP_decides_between_approval_and_certification(int p, PdfCertificationLevel? level, bool allowed)
    {
        var original = Fixture($"/SV << /Type /SV /MDP << /P {p} >> >>");
        var options = new DigitalSignatureOptions { FieldName = "approval", Certification = level };
        if (!allowed)
        {
            using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
            Assert.Throws<ArgumentException>(() => Sign(document, options));
            return;
        }

        var signed = Save(original, document => Sign(document, options));

        AssertLastSignatureCoversTheFile(signed);
        using var result = Open(signed);
        Assert.Equal(level != null, result.Catalog.Elements.GetDictionary("/Perms")?.Elements.ContainsKey("/DocMDP") == true);
    }

    // /FT and /V are inheritable: a signed signature field whose type sits on its parent still makes the document
    // signed, so it cannot be certified any more.
    [Fact]
    public void A_signature_below_a_parent_field_stops_certification()
    {
        var original = Fixture(moreFields: "13 0 R", moreAnnots: "14 0 R", more: new[] {
            /* 13 */ "<< /FT /Sig /T (parent) /Kids [14 0 R] >>",
            /* 14 */ "<< /Parent 13 0 R /T (child) /V 15 0 R /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /P 3 0 R /F 132 >>",
            /* 15 */ "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00> >>",
        });
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);

        Assert.Throws<InvalidOperationException>(() =>
            Sign(document, new DigitalSignatureOptions { FieldName = "approval", Certification = PdfCertificationLevel.FormFilling }));
    }

    [Fact]
    public void A_widget_whose_rectangle_corners_are_swapped_gets_the_signature()
    {
        var original = Fixture(moreFields: "13 0 R", moreAnnots: "13 0 R", more: new[] {
            /* 13 */ "<< /FT /Sig /T (swapped) /Type /Annot /Subtype /Widget /Rect [370 160 250 120] /P 3 0 R /F 4 /AP << /N 12 0 R >> >>",
        });
        var handler = new RecordingAppearance();

        var signed = Save(original, document => Sign(document, new DigitalSignatureOptions { FieldName = "swapped", AppearanceHandler = handler }));

        Assert.Equal(new XRect(250, 120, 120, 40), handler.Rectangle);
        using var document = Open(signed);
        var normal = Object(document, 13).Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!;
        Assert.NotEqual(12, normal.Reference!.ObjectID.ObjectNumber);
    }

    [Fact]
    public void A_field_whose_value_is_an_indirect_null_is_not_signed_yet()
    {
        var original = Fixture("/V 13 0 R", more: new[] { /* 13 */ "null" });

        var signed = Save(original, SignField("approval"));

        AssertLastSignatureCoversTheFile(signed);
        using var document = Open(signed);
        Assert.Equal("/Sig", Object(document, 8).Elements.GetDictionary("/V")!.Elements.GetName("/Type"));
    }

    [Fact]
    public void A_custom_appearance_is_drawn_at_the_widgets_size()
    {
        var handler = new RecordingAppearance();
        Save(Fixture(), document => Sign(document, new DigitalSignatureOptions { FieldName = "approval", AppearanceHandler = handler }));

        Assert.Equal(new XRect(250, 20, 120, 40), handler.Rectangle);
    }

    sealed class RecordingAppearance : IAnnotationAppearanceHandler
    {
        public XRect Rectangle { get; private set; }

        public void DrawAppearance(XGraphics gfx, XRect rect)
        {
            Rectangle = rect;
            gfx.DrawRectangle(XPens.Black, 0, 0, rect.Width, rect.Height);
        }
    }
}
