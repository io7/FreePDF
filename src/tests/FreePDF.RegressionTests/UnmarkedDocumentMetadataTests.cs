// With EncryptMetadata false, some writers leave the document's metadata unencrypted without marking it with the
// Identity crypt filter. A Modify-mode save that keeps the encryption (PdfReaderOptions.DoNotResetEncryption) wrote it
// unencrypted and still unmarked, so pyHanko and MuPDF decrypted the plain XML and failed. Original fixtures; MIT,
// see LICENSE.
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class UnmarkedDocumentMetadataTests
{
    const string Xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
        "<unmarked-check/></x:xmpmeta><?xpacket end=\"w\"?>";

    static readonly Regex CryptMarker = new(@"/Filter\s*(\[\s*)?/Crypt\s*(\]\s*)?/DecodeParms\s*(\[\s*)?<<[^>]*/Identity\s*>>\s*(\])?");

    // FreePDF marks the metadata; blanking the marker in place keeps the byte offsets valid.
    static byte[] WithUnmarkedMetadata(string kind)
    {
        byte[] marked;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            document.Catalog.GetOrCreateMetadata().SetMetadata(Xmp);
            switch (kind)
            {
                case "V4-RC4": document.SecurityHandler.SetEncryptionToV4UsingRC4(false); break;
                case "AES-128": document.SecurityHandler.SetEncryptionToV4UsingAES(false); break;
                case "AES-256": document.SecurityHandler.SetEncryptionToV5(false); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            document.Save(output);
            marked = output.ToArray();
        }
        var text = TestPdf.Latin1.GetString(marked);
        Assert.Matches(CryptMarker, text);
        text = CryptMarker.Replace(text, match => new string(' ', match.Length));
        return TestPdf.Latin1.GetBytes(text);
    }

    static PdfDictionary DocumentMetadata(PdfDocument document) => document.Catalog.Elements.GetDictionary("/Metadata")!;

    [Theory]
    [InlineData("V4-RC4")]
    [InlineData("AES-128")]
    [InlineData("AES-256")]
    public void A_save_that_keeps_the_encryption_marks_the_document_metadata(string kind)
    {
        var original = WithUnmarkedMetadata(kind);
        using (var unmarked = PdfReader.Open(new MemoryStream(original), "user", PdfDocumentOpenMode.Import))
            Assert.Null(DocumentMetadata(unmarked).Elements["/Filter"]);

        byte[] result;
        using (var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Modify,
                   new PdfReaderOptions { DoNotResetEncryption = true }))
        {
            document.Info.Title = "Changed";
            using var output = new MemoryStream();
            document.Save(output);
            result = output.ToArray();
        }

        Assert.Matches(CryptMarker, TestPdf.Latin1.GetString(result));
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Contains("<unmarked-check/>", TestPdf.Latin1.GetString(DocumentMetadata(reopened).Stream!.UnfilteredValue));
        Assert.Equal("Changed", reopened.Info.Title);
    }

    [Theory]
    [InlineData("V4-RC4")]
    [InlineData("AES-128")]
    [InlineData("AES-256")]
    public void A_crypt_filter_that_encrypts_the_document_metadata_is_kept(string kind)
    {
        // The file encrypts its metadata on purpose although EncryptMetadata is false.
        var original = WithUnmarkedMetadata(kind);

        byte[] result;
        using (var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Modify,
                   new PdfReaderOptions { DoNotResetEncryption = true }))
        {
            document.SecurityHandler.SetCryptFilter(DocumentMetadata(document), "StdCF");
            using var output = new MemoryStream();
            document.Save(output);
            result = output.ToArray();
        }

        var text = TestPdf.Latin1.GetString(result);
        Assert.Contains("/StdCF", Regex.Match(text, @"/Type\s*/Metadata.*?>>\s*stream", RegexOptions.Singleline).Value);
        Assert.DoesNotContain("<unmarked-check/>", text);
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Contains("<unmarked-check/>", TestPdf.Latin1.GetString(DocumentMetadata(reopened).Stream!.UnfilteredValue));
    }

    [Fact]
    public void A_file_without_crypt_filters_saves_changed_metadata_in_Append_mode()
    {
        // EncryptMetadata applies from V4 on; a V2 file that carries it anyway made the save throw "Crypt filters are
        // not supported for the chosen encryption version".
        byte[] original;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            document.Catalog.GetOrCreateMetadata().SetMetadata(Xmp);
            document.SecurityHandler.SetEncryptionToV2With128Bits();
            document.SecurityHandler.Elements.SetBoolean("/EncryptMetadata", false);
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            document.Save(output);
            original = output.ToArray();
        }

        byte[] result;
        using (var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append))
        {
            DocumentMetadata(document).Elements.SetName("/Marker", "/Changed");
            ((PdfMetadata)DocumentMetadata(document)).SetMetadata(Xmp.Replace("<unmarked-check/>", "<changed/>"));
            using var output = new MemoryStream();
            document.Save(output);
            result = output.ToArray();
        }

        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Contains("<changed/>", TestPdf.Latin1.GetString(DocumentMetadata(reopened).Stream!.UnfilteredValue));
    }

    // Saves with DoNotResetEncryption after the change and returns the file.
    static byte[] KeepEncryption(byte[] original, Action<PdfDocument, PdfMetadata> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Modify,
            new PdfReaderOptions { DoNotResetEncryption = true });
        change(document, document.Catalog.GetMetadata()!);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static void AssertEncryptedAndReadable(byte[] file, string marker)
    {
        Assert.DoesNotContain(marker, TestPdf.Latin1.GetString(file));
        using var reopened = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        Assert.Contains(marker, TestPdf.Latin1.GetString(DocumentMetadata(reopened).Stream!.UnfilteredValue));
    }

    [Theory]
    [InlineData("V4-RC4")]
    [InlineData("AES-128")]
    public void Crypt_filter_parameters_without_a_type_are_kept(string kind)
    {
        // ISO 32000-1 Table 14 makes /Type optional.
        var result = KeepEncryption(WithUnmarkedMetadata(kind), (_, metadata) => {
            metadata.Elements.SetName("/Filter", "/Crypt");
            var parameters = new PdfDictionary();
            parameters.Elements.SetName("/Name", "/StdCF");
            metadata.Elements["/DecodeParms"] = parameters;
        });

        AssertEncryptedAndReadable(result, "<unmarked-check/>");
    }

    [Theory]
    [InlineData("V4-RC4")]
    [InlineData("AES-256")]
    public void New_content_keeps_the_crypt_filter_of_the_old(string kind)
    {
        var result = KeepEncryption(WithUnmarkedMetadata(kind), (document, metadata) => {
            document.SecurityHandler.SetCryptFilter(metadata, "StdCF");
            metadata.SetMetadata(Xmp.Replace("<unmarked-check/>", "<replaced/>"));
        });

        AssertEncryptedAndReadable(result, "<replaced/>");
    }

    [Fact]
    public void New_content_of_metadata_its_writer_left_unencrypted_stays_unencrypted()
    {
        // EncryptMetadata true, but the writer marked the metadata with the Identity crypt filter.
        byte[] original;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            var metadata = document.Catalog.GetOrCreateMetadata();
            metadata.SetMetadata(Xmp);
            document.SecurityHandler.SetEncryptionToV4UsingAES();
            document.SecurityHandler.SetIdentityCryptFilter(metadata);
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            document.Save(output);
            original = output.ToArray();
        }
        Assert.Contains("<unmarked-check/>", TestPdf.Latin1.GetString(original));

        var result = KeepEncryption(original, (_, metadata) => metadata.SetMetadata(Xmp.Replace("<unmarked-check/>", "<replaced/>")));

        Assert.Contains("<replaced/>", TestPdf.Latin1.GetString(result));
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Contains("<replaced/>", TestPdf.Latin1.GetString(DocumentMetadata(reopened).Stream!.UnfilteredValue));
    }
}
