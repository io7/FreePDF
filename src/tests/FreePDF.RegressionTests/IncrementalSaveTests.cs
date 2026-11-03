// Saving in Append mode: the original bytes stay unchanged and one incremental update holds the changes, so
// existing signatures stay valid. Original fixtures and a self-signed test identity; MIT, see LICENSE.
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using FreePDF.Lab.Fixtures;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class IncrementalSaveTests
{
    const string Id = "/ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]";

    static byte[] Classic() => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>",
        "<< /Length 25 >>\nstream\n0 1 0 rg 20 20 80 60 re f\nendstream",
        "<< /Title (Original title) /Producer (Original producer) >>",
    }, _ => "/Info 5 0 R " + Id);

    static byte[] Save(byte[] original, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static string Update(byte[] original, byte[] result)
    {
        Assert.True(result.Length > original.Length);
        Assert.Equal(original, result.Take(original.Length).ToArray());
        return TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
    }

    static int[] WrittenObjects(string update)
        => Regex.Matches(update, @"(?m)^(\d+) \d+ obj\b").Cast<Match>().Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();

    static long StartXRef(byte[] file)
    {
        var text = TestPdf.Latin1.GetString(file);
        var match = Regex.Match(text.Substring(text.LastIndexOf("startxref", StringComparison.Ordinal)), @"startxref\s+(\d+)");
        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Changes_are_appended_after_the_unchanged_original()
    {
        var original = Classic();
        var result = Save(original, document => document.Pages[0].Elements.SetName("/Marker", "/Changed"));

        var update = Update(original, result);
        Assert.Equal(new[] { 3, 5 }, WrittenObjects(update));
        Assert.Contains("\nxref\n", update);
        Assert.Contains($"/Prev {StartXRef(original)}", update);
        Assert.DoesNotContain("%PDF-", update);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal("/Changed", reopened.Pages[0].Elements.GetName("/Marker"));
        Assert.Equal(3, reopened.Pages[0].Reference!.ObjectNumber);
        Assert.Equal("Original title", reopened.Info.Title);
        Assert.NotNull(reopened.Info.ModificationDate);
    }

    [Fact]
    public void An_unchanged_document_is_saved_as_it_was()
    {
        var original = Classic();
        Assert.Equal(original, Save(original, _ => { }));
    }

    [Fact]
    public void Reading_the_document_information_is_no_change()
    {
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
        });
        Assert.Equal(original, Save(original, document => _ = document.Info.Title));
    }

    [Fact]
    public void Changing_only_the_document_information_writes_it()
    {
        var original = Classic();
        var result = Save(original, document => document.Info.Title = "New title");

        Assert.Equal(new[] { 5 }, WrittenObjects(Update(original, result)));
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal("New title", reopened.Info.Title);
    }

    [Fact]
    public void The_first_file_identifier_is_kept_and_the_second_changes()
    {
        var original = Classic();
        var result = Save(original, document => document.Pages[0].Elements.SetName("/Marker", "/Changed"));

        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF },
            Bytes(reopened.Internals.FirstDocumentID));
        Assert.NotEqual(Bytes(reopened.Internals.FirstDocumentID), Bytes(reopened.Internals.SecondDocumentID));
        Assert.Equal(16, reopened.Internals.SecondDocumentID.Length);
    }

    static byte[] Bytes(string value) => value.Select(character => (byte)character).ToArray();

    [Fact]
    public void New_objects_are_numbered_after_the_original_ones()
    {
        var original = Classic();
        var result = Save(original, document => {
            var marker = new PdfDictionary(document);
            document.Internals.AddObject(marker);
            marker.Elements.SetString("/Note", "added");
            document.Catalog.Elements["/Marker"] = marker.Reference;
        });

        var written = WrittenObjects(Update(original, result));
        Assert.Contains(1, written);
        Assert.Contains(6, written);
        Assert.DoesNotContain(2, written);
        Assert.DoesNotContain(3, written);
        Assert.DoesNotContain(4, written);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal("added", reopened.Catalog.Elements.GetDictionary("/Marker")!.Elements.GetString("/Note"));
    }

    [Fact]
    public void A_file_with_cross_reference_streams_gets_a_cross_reference_stream_update()
    {
        var original = PreservationFixtures.Incremental();
        var result = Save(original, document => document.Catalog.Elements.SetBoolean("/Changed", true));

        var update = Update(original, result);
        Assert.Contains("/Type /XRef", update);
        Assert.DoesNotContain("\nxref\n", update);
        Assert.Contains($"/Prev {StartXRef(original)}", update);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.True(reopened.Catalog.Elements.GetBoolean("/Changed"));
        Assert.Equal("current marker", reopened.Catalog.Elements.GetString("/Marker"));
    }

    [Fact]
    public void Saving_to_the_file_it_was_opened_from_appends_to_it()
    {
        var original = Classic();
        var path = Path.Combine(Path.GetTempPath(), "freepdf-append-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            File.WriteAllBytes(path, original);
            using (var document = PdfReader.Open(path, PdfDocumentOpenMode.Append))
            {
                document.Pages[0].Elements.SetName("/Marker", "/Changed");
                document.Save(path);
            }
            var update = Update(original, File.ReadAllBytes(path));
            Assert.Contains("3 0 obj", update);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_page_can_be_added_to_a_flat_page_tree()
    {
        var original = Classic();
        var result = Save(original, document => document.AddPage());

        Update(original, result);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(2, reopened.PageCount);
    }

    [Fact]
    public void A_nested_page_tree_is_kept_and_refuses_page_insertion()
    {
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R] /Count 1 /MediaBox [0 0 240 200] >>",
            "<< /Type /Page /Parent 3 0 R >>",
        });
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);

        Assert.Equal(1, document.PageCount);
        Assert.Equal(4, document.Pages[0].Reference!.ObjectNumber);
        Assert.Throws<NotSupportedException>(() => document.AddPage());
        document.Pages[0].Elements.SetName("/Marker", "/Changed");
        using var output = new MemoryStream();
        document.Save(output);
        var written = WrittenObjects(Update(original, output.ToArray()));
        Assert.Contains(4, written);
        Assert.DoesNotContain(2, written);
        Assert.DoesNotContain(3, written);
    }

    [Fact]
    public void Drawing_on_a_page_appends_a_content_stream_and_keeps_the_old_one()
    {
        var original = Classic();
        var result = Save(original, document => {
            using var gfx = XGraphics.FromPdfPage(document.Pages[0], XGraphicsPdfPageOptions.Append);
            gfx.DrawRectangle(XBrushes.Red, 100, 100, 40, 40);
        });

        var written = WrittenObjects(Update(original, result));
        Assert.Contains(3, written);
        Assert.DoesNotContain(4, written);
        // The original stream stays in place between the q and Q streams that isolate its graphics state.
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        var contents = reopened.Pages[0].Contents.Elements;
        Assert.Equal(4, contents.Count);
        Assert.Equal(4, ((PdfSharp.Pdf.Advanced.PdfReference)contents[1]).ObjectNumber);
    }

    [Fact]
    public void Encrypted_files_are_refused()
    {
        using var source = PdfReader.Open(new MemoryStream(Classic()), PdfDocumentOpenMode.Modify);
        source.SecuritySettings.UserPassword = "user";
        source.SecuritySettings.OwnerPassword = "owner";
        using var encrypted = new MemoryStream();
        source.Save(encrypted);

        Assert.Throws<NotSupportedException>(() =>
            PdfReader.Open(new MemoryStream(encrypted.ToArray()), "owner", PdfDocumentOpenMode.Append));
    }

    [Fact]
    public void Encryption_cannot_be_added_in_an_update()
    {
        using var document = PdfReader.Open(new MemoryStream(Classic()), PdfDocumentOpenMode.Append);
        document.SecuritySettings.UserPassword = "user";
        Assert.Throws<NotSupportedException>(() => document.Save(new MemoryStream()));
    }

    [Fact]
    public void A_cross_reference_table_the_reader_had_to_repair_is_refused()
    {
        // The entry for object 4 points at an object that says it is object 3.
        var text = TestPdf.Latin1.GetString(Classic()).Replace("\n4 0 obj", "\n3 0 obj");
        var damaged = TestPdf.Latin1.GetBytes(text);

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(damaged), PdfDocumentOpenMode.Append));
    }

    [Fact]
    public void Filling_a_field_writes_that_field_without_touching_the_others()
    {
        var original = FormFixture.Create();
        var result = Save(original, document =>
            ((PdfFormTextField)document.Catalog.GetAcroForm()!.Fields["name"]!).Text = "Ada");

        var update = Update(original, result);
        var written = WrittenObjects(update);
        Assert.Contains(5, written);
        foreach (var untouched in new[] { 2, 3, 6, 7, 8, 9, 10, 11, 12, 13, 16, 17 })
            Assert.DoesNotContain(untouched, written);
        // Signature validators accept a new value and appearance; a new modification date (/M) counts as an
        // unexplained change to the field.
        var field = Regex.Match(update, @"(?s)(?:^|\n)5 0 obj\n(.*?)endobj").Groups[1].Value;
        Assert.Contains("(Ada)", field);
        Assert.DoesNotMatch(@"/M\s*\(", field);
    }

    [Fact]
    public async Task A_form_filled_after_signing_keeps_the_signature_valid()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = Certificate(rsa, "CN=FreePDF first signer");
        var signed = await SignAsync(FormFixture.Create(), PdfDocumentOpenMode.Modify, certificate);

        var filled = Save(signed, document =>
            ((PdfFormTextField)document.Catalog.GetAcroForm()!.Fields["name"]!).Text = "Ada Lovelace");

        var update = Update(signed, filled);
        Assert.DoesNotContain("/ByteRange", update);
        Assert.DoesNotContain("/Contents <", update);
        var signature = Assert.Single(Signatures(filled));
        Assert.Equal(signed.Length, signature.CoveredEnd);
        Verify(filled, signature);
        using var reopened = PdfReader.Open(new MemoryStream(filled), PdfDocumentOpenMode.Import);
        Assert.Equal("Ada Lovelace", ((PdfFormTextField)reopened.Catalog.GetAcroForm()!.Fields["name"]!).Text);
    }

    [Fact]
    public async Task A_second_signature_covers_the_first_and_both_stay_valid()
    {
        using var firstKey = RSA.Create(2048);
        using var first = Certificate(firstKey, "CN=FreePDF first signer");
        using var secondKey = RSA.Create(2048);
        using var second = Certificate(secondKey, "CN=FreePDF second signer");
        var signedOnce = await SignAsync(FormFixture.Create(), PdfDocumentOpenMode.Modify, first);

        var signedTwice = await SignAsync(signedOnce, PdfDocumentOpenMode.Append, second);

        var update = Update(signedOnce, signedTwice);
        Assert.Single(Regex.Matches(update, "/ByteRange").Cast<Match>());
        var signatures = Signatures(signedTwice);
        Assert.Equal(2, signatures.Count);
        Assert.Equal(signedOnce.Length, signatures[0].CoveredEnd);
        Assert.Equal(signedTwice.Length, signatures[1].CoveredEnd);
        foreach (var signature in signatures)
            Verify(signedTwice, signature);
        using var reopened = PdfReader.Open(new MemoryStream(signedTwice), PdfDocumentOpenMode.Import);
        var names = reopened.Catalog.GetAcroForm()!.Fields.Elements
            .Select(item => ((PdfDictionary)((PdfSharp.Pdf.Advanced.PdfReference)item).Value).Elements.GetString("/T")).ToArray();
        Assert.Contains("Signature1", names);
        Assert.Contains("Signature2", names);
    }

    static async Task<byte[]> SignAsync(byte[] source, PdfDocumentOpenMode mode, X509Certificate2 certificate)
    {
        using var document = PdfReader.Open(new MemoryStream(source), mode);
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256),
            new DigitalSignatureOptions { Rectangle = new XRect(20, 20, 120, 40), AppearanceHandler = new EmptyAppearance() });
        using var output = new MemoryStream();
        await document.SaveAsync(output);
        return output.ToArray();
    }

    // A class, not a record: records need IsExternalInit, which .NET Framework 4.7.2 lacks.
    sealed class SignatureRange(long[] range)
    {
        public long[] Range { get; } = range;
        public long CoveredEnd => Range[2] + Range[3];
    }

    // Signature dictionaries in file order, read from the raw bytes as a validator would.
    static List<SignatureRange> Signatures(byte[] file)
    {
        var text = TestPdf.Latin1.GetString(file);
        return Regex.Matches(text, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]").Cast<Match>()
            .Select(match => new SignatureRange(Enumerable.Range(1, 4)
                .Select(group => long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture)).ToArray()))
            .ToList();
    }

    static void Verify(byte[] file, SignatureRange signature)
    {
        var range = signature.Range;
        Assert.Equal(0, range[0]);
        var signedBytes = file.Take((int)range[1]).Concat(file.Skip((int)range[2]).Take((int)range[3])).ToArray();
        var hex = TestPdf.Latin1.GetString(file, (int)range[1] + 1, (int)(range[2] - range[1] - 2));
        var der = Enumerable.Range(0, hex.Length / 2).Select(index => Convert.ToByte(hex.Substring(2 * index, 2), 16)).ToArray();
        var cms = new SignedCms(new ContentInfo(signedBytes), true);
        cms.Decode(der.Take(DerLength(der)).ToArray());
        cms.CheckSignature(true);
    }

    // The signature is padded with zeros up to the reserved size; DER states its own length.
    static int DerLength(byte[] der)
    {
        Assert.Equal(0x30, der[0]);
        if (der[1] < 0x80)
            return 2 + der[1];
        var lengthBytes = der[1] & 0x7F;
        var length = 0;
        for (var index = 0; index < lengthBytes; index++)
            length = (length << 8) | der[2 + index];
        return 2 + lengthBytes + length;
    }

    static X509Certificate2 Certificate(RSA key, string subject)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    sealed class EmptyAppearance : IAnnotationAppearanceHandler
    {
        public void DrawAppearance(XGraphics gfx, XRect rect) { }
    }
}
