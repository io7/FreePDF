// Append mode for encrypted documents: the update keeps the file's encryption, so new and changed objects are
// encrypted with the file's key, the trailer keeps /Encrypt and the first /ID, and signatures stay valid. Original
// fixtures and a self-signed test identity; MIT, see LICENSE.
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class EncryptedAppendTests
{
    static byte[] Plain() => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>",
        "<< /Length 25 >>\nstream\n0 1 0 rg 20 20 80 60 re f\nendstream",
        "<< /Title (Original title) >>",
    }, _ => "/Info 5 0 R /ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]");

    static byte[] Encrypted(string kind)
    {
        using var document = PdfReader.Open(new MemoryStream(Plain()), PdfDocumentOpenMode.Modify);
        switch (kind)
        {
            case "RC4-128": document.SecurityHandler.SetEncryptionToV2With128Bits(); break;
            case "RC4-128-V4": document.SecurityHandler.SetEncryptionToV4UsingRC4(); break;
            case "AES-128": document.SecurityHandler.SetEncryptionToV4UsingAES(); break;
            case "AES-256": document.SecurityHandler.SetEncryptionToV5(); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        document.SecuritySettings.UserPassword = "user";
        document.SecuritySettings.OwnerPassword = "owner";
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    public static TheoryData<string> Kinds() => new() { "RC4-128", "RC4-128-V4", "AES-128", "AES-256" };

    static byte[] Save(byte[] original, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append);
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

    [Theory]
    [MemberData(nameof(Kinds))]
    public void An_unchanged_encrypted_document_is_saved_as_it_was(string kind)
    {
        var original = Encrypted(kind);
        Assert.Equal(original, Save(original, _ => { }));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_change_is_encrypted_with_the_files_key(string kind)
    {
        var original = Encrypted(kind);

        var result = Save(original, document => document.Info.Title = "Secret new title");

        var update = Update(original, result);
        Assert.DoesNotContain("Secret new title", update);
        Assert.Matches(@"/Encrypt\s+\d+ 0 R", update);
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Equal("Secret new title", reopened.Info.Title);
        Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF },
            reopened.Internals.FirstDocumentID.Select(character => (byte)character).ToArray());
        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Drawing_on_a_page_writes_an_encrypted_content_stream(string kind)
    {
        var original = Encrypted(kind);

        var result = Save(original, document => {
            using var gfx = XGraphics.FromPdfPage(document.Pages[0]);
            gfx.DrawRectangle(XBrushes.Red, 120, 20, 60, 60);
        });

        var update = Update(original, result);
        Assert.DoesNotContain(" re f", update);
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        var contents = reopened.Pages[0].Contents.Elements;
        Assert.True(contents.Count >= 2);
        var all = string.Concat(Enumerable.Range(0, contents.Count)
            .Select(index => TestPdf.Latin1.GetString(contents.GetDictionary(index)!.Stream!.UnfilteredValue)));
        Assert.Contains("20 20 80 60 re f", all);
        Assert.True(Regex.Matches(all, @" re\b").Count >= 2, "the drawn rectangle is in the new content");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_signature_on_an_encrypted_update_verifies(string kind)
    {
        var original = Encrypted(kind);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF encrypted append test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));

        byte[] result;
        using (var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append))
        {
            DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256),
                new DigitalSignatureOptions { Rectangle = new XRect(20, 120, 100, 40), AppearanceHandler = new EmptyAppearance() });
            using var output = new MemoryStream();
            await document.SaveAsync(output);
            result = output.ToArray();
        }

        Update(original, result);
        var match = Regex.Match(TestPdf.Latin1.GetString(result), @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        var range = Enumerable.Range(1, 4).Select(group => long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(result.Length, range[2] + range[3]);
        var signed = result.Take((int)range[1]).Concat(result.Skip((int)range[2]).Take((int)range[3])).ToArray();
        // ISO 32000-1 12.8.1: the signature's /Contents is not encrypted; it is the DER of the CMS in hex.
        var hex = TestPdf.Latin1.GetString(result, (int)range[1] + 1, (int)(range[2] - range[1] - 2));
        var der = Enumerable.Range(0, hex.Length / 2).Select(index => Convert.ToByte(hex.Substring(2 * index, 2), 16)).ToArray();
        var cms = new SignedCms(new ContentInfo(signed), true);
        cms.Decode(der.Take(DerLength(der)).ToArray());
        cms.CheckSignature(true);
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.NotNull(reopened.Catalog.GetAcroForm());
    }

    // AES-256 is PDF 2.0, and reading it makes the document 2.0. Files from Chrome, LibreOffice or qpdf say 1.7 in
    // their header; an update wrote a catalog /Version 2.0 even when nothing else changed.
    [Fact]
    public void An_AES_256_file_with_a_1_7_header_gets_no_new_version()
    {
        var original = Encrypted("AES-256");
        var header = TestPdf.Latin1.GetString(original, 0, 8);
        Assert.Equal("%PDF-2.0", header);
        TestPdf.Latin1.GetBytes("%PDF-1.7").CopyTo(original, 0);

        Assert.Equal(original, Save(original, _ => { }));
        var update = Update(original, Save(original, document => document.Info.Title = "New title"));
        Assert.DoesNotContain("/Version", update);
    }

    // Review findings for PR #39.

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changing_the_permissions_is_refused_before_saving(bool directEncryptionDictionary)
    {
        // The key of RC4 and AES-128 depends on /P, so a written change made the file unreadable; a dictionary held
        // directly in the trailer was not checked at all.
        var original = directEncryptionDictionary ? DirectEncryptionDictionaryTests.DirectlyEncrypted(false, "user") : Encrypted("AES-128");
        using var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append);
        document.SecuritySettings.PermitPrint = false;
        document.Info.Title = "Changed";
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => document.Save(output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_change_to_a_file_with_a_direct_encryption_dictionary_is_encrypted()
    {
        var original = DirectEncryptionDictionaryTests.DirectlyEncrypted(true, "user");

        var result = Save(original, document => document.Info.Title = "Secret new title");

        Assert.DoesNotContain("Secret new title", Update(original, result));
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Equal("Secret new title", reopened.Info.Title);
    }

    [Fact]
    public void A_file_with_an_integrity_code_is_refused_when_changed()
    {
        // ISO/TS 32004: the code covers the whole file; an update by another tool cannot extend it.
        var encrypted = Encrypted("AES-256");
        var text = TestPdf.Latin1.GetString(encrypted);
        var trailerAt = text.LastIndexOf("trailer", StringComparison.Ordinal);
        var original = TestPdf.Latin1.GetBytes(text.Substring(0, trailerAt)
            + text.Substring(trailerAt).Replace("/Size", "/AuthCode << /MACLocation /Standalone >> /Size"));

        Assert.Equal(original, Save(original, _ => { }));
        using var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append);
        document.Info.Title = "Changed";
        Assert.Throws<NotSupportedException>(() => document.Save(new MemoryStream()));
    }

    [Fact]
    public void Unencrypted_metadata_written_in_an_update_gets_the_identity_crypt_filter()
    {
        byte[] original;
        using (var source = PdfReader.Open(new MemoryStream(Plain()), PdfDocumentOpenMode.Modify))
        {
            source.SecurityHandler.SetEncryptionToV5(encryptMetadata: false);
            source.SecuritySettings.UserPassword = "user";
            source.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            source.Save(output);
            original = output.ToArray();
        }

        var result = Save(original, document =>
            document.Catalog.GetOrCreateMetadata().SetMetadata("<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><updated/></x:xmpmeta><?xpacket end=\"w\"?>"));

        var update = Update(original, result);
        var metadata = Regex.Match(update, @"(?s)\d+ 0 obj\s*(<<[^\n]*?/Type\s*/Metadata.*?)stream").Groups[1].Value;
        Assert.Contains("/Crypt", metadata);
        Assert.Contains("/Identity", metadata);
        Assert.Contains("<updated/>", update);
    }

    [Fact]
    public void An_encrypted_file_without_an_identifier_keeps_none()
    {
        // The RC4 key is made from the first identifier, empty here; an update that wrote a new /ID made every
        // password fail.
        byte[] encrypted;
        using (var source = PdfReader.Open(new MemoryStream(Plain()), PdfDocumentOpenMode.Modify))
        {
            source.SecurityHandler.SetEncryptionToV2With128Bits();
            source.SecuritySettings.UserPassword = "user";
            source.SecuritySettings.OwnerPassword = "owner";
            source.Internals.FirstDocumentID = "";
            using var output = new MemoryStream();
            source.Save(output);
            encrypted = output.ToArray();
        }
        var text = TestPdf.Latin1.GetString(encrypted);
        var id = Regex.Match(text.Substring(text.LastIndexOf("trailer", StringComparison.Ordinal)), @"/ID\s*\[[^\]]*\]");
        var idAt = text.LastIndexOf("trailer", StringComparison.Ordinal) + id.Index;
        var original = TestPdf.Latin1.GetBytes(text.Substring(0, idAt) + new string(' ', id.Length) + text.Substring(idAt + id.Length));
        using (var check = PdfReader.Open(new MemoryStream(original), "user", PdfDocumentOpenMode.Import))
            Assert.Equal("Original title", check.Info.Title);

        var result = Save(original, document => document.Info.Title = "New title");

        Assert.DoesNotContain("/ID", Update(original, result));
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Equal("New title", reopened.Info.Title);
    }

    [Fact]
    public void Opening_with_only_the_user_password_is_refused_as_in_Modify_mode()
    {
        var original = Encrypted("AES-256");
        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(original), "user", PdfDocumentOpenMode.Append));
    }

    [Fact]
    public void The_encryption_cannot_be_changed_in_an_update()
    {
        var original = Encrypted("AES-128");
        using var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append);
        document.SecuritySettings.UserPassword = "other";
        Assert.Throws<NotSupportedException>(() => document.Save(new MemoryStream()));
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

    sealed class EmptyAppearance : IAnnotationAppearanceHandler
    {
        public void DrawAppearance(XGraphics gfx, XRect rect) { }
    }
}
