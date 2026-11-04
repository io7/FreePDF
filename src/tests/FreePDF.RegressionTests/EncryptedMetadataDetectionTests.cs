// With EncryptMetadata false, some writers leave metadata streams unencrypted without the Identity crypt filter, so
// FreePDF guesses whether a /Type /Metadata stream is unencrypted from its content. The guess decoded the stream
// with its filters, so an encrypted compressed stream threw and the file did not open; and an encrypted stream whose
// first byte happened to be '<' (one in 256) was taken for XML and not decrypted. Original fixtures; MIT, see LICENSE.
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class EncryptedMetadataDetectionTests
{
    const string Xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta><?xpacket end=\"w\"?>";

    static void Encrypt(PdfDocument document, string kind)
    {
        switch (kind)
        {
            case "V4-RC4": document.SecurityHandler.SetEncryptionToV4UsingRC4(false); break;
            case "AES-128": document.SecurityHandler.SetEncryptionToV4UsingAES(false); break;
            case "AES-256": document.SecurityHandler.SetEncryptionToV5(false); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        document.SecuritySettings.UserPassword = "user";
        document.SecuritySettings.OwnerPassword = "owner";
    }

    static PdfDictionary AddStream(PdfDocument document, string key, string type, byte[] data, bool compressed)
    {
        var stream = new PdfDictionary(document);
        stream.Elements.SetName("/Type", type);
        stream.Elements.SetName("/Subtype", "/XML");
        if (compressed)
        {
            stream.Elements.SetName("/Filter", "/FlateDecode");
            data = Filtering.FlateDecode.Encode(data, PdfFlateEncodeMode.Default);
        }
        stream.CreateStream(data);
        document.Internals.AddObject(stream);
        document.Catalog.Elements.SetObject(key, stream);
        return stream;
    }

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // A file whose metadata streams are encrypted, as a writer that follows ISO 32000-1 makes it: EncryptMetadata
    // exempts only the document's own metadata. FreePDF encrypts the streams under another /Type, which is then
    // renamed in place, so the byte offsets stay valid.
    static byte[] WithEncryptedMetadata(string kind, int count, bool compressed)
    {
        using var document = new PdfDocument();
        document.AddPage();
        for (var index = 0; index < count; index++)
            AddStream(document, "/OtherMetadata" + index, "/NotMetadata", TestPdf.Latin1.GetBytes(Xmp), compressed);
        Encrypt(document, kind);
        var text = TestPdf.Latin1.GetString(Save(document));
        text = Regex.Replace(text, @"/Type\s*/NotMetadata", match => "/Type/Metadata".PadRight(match.Length));
        return TestPdf.Latin1.GetBytes(text);
    }

    static string Read(PdfDocument document, string key)
    {
        var stream = document.Catalog.Elements.GetDictionary(key)!.Stream!;
        return TestPdf.Latin1.GetString(stream.UnfilteredValue);
    }

    [Theory]
    [InlineData("V4-RC4")]
    [InlineData("AES-128")]
    [InlineData("AES-256")]
    public void Encrypted_compressed_metadata_opens_and_decrypts(string kind)
    {
        var file = WithEncryptedMetadata(kind, 1, true);

        using var document = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        Assert.Equal("/Metadata", document.Catalog.Elements.GetDictionary("/OtherMetadata0")!.Elements.GetName("/Type"));
        Assert.Equal(Xmp, Read(document, "/OtherMetadata0"));
    }

    // Uncompressed: about 8 of 2,000 streams have encrypted data that starts with '<' (the chance that none does is
    // 0.04%). Compressed: about 2 of 2,000 decode to nothing without an error, which counted as a blank stream.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Many_encrypted_metadata_streams_all_decrypt(bool compressed)
    {
        const int count = 2000;
        var file = WithEncryptedMetadata("AES-256", count, compressed);

        using var document = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        var misread = Enumerable.Range(0, count).Count(index => Read(document, "/OtherMetadata" + index) != Xmp);
        Assert.True(misread == 0, $"{misread} of {count} encrypted metadata streams were not decrypted");
    }

    // EncryptMetadata false exempts only the document's metadata (ISO 32000-1 Table 20). FreePDF wrote every metadata
    // stream that held XML unencrypted, which pyHanko could not read; since 7.6.1 the others are encrypted, and they
    // read back the same, compressed or not, also with non-ASCII text (UTF-8) at their start.
    [Theory]
    [InlineData("V4-RC4", false, false)]
    [InlineData("AES-256", false, false)]
    [InlineData("AES-256", true, false)]
    [InlineData("V4-RC4", false, true)]
    [InlineData("V4-RC4", true, true)]
    public void Metadata_other_than_the_documents_is_encrypted(string kind, bool compressed, bool nonAscii)
    {
        var content = Encoding.UTF8.GetBytes(nonAscii ? "<!-- Créé par Ærø -->" + Xmp : Xmp);
        byte[] file;
        byte[] written;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            written = AddStream(document, "/OtherMetadata", "/Metadata", content, compressed).Stream!.Value;
            Encrypt(document, kind);
            file = Save(document);
        }

        Assert.NotEqual(TestPdf.Latin1.GetString(written), EncryptedEmptyDataTests.RawStream(TestPdf.Latin1.GetString(file), "/OtherMetadata"));
        using var reopened = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        Assert.Equal(content, reopened.Catalog.Elements.GetDictionary("/OtherMetadata")!.Stream!.UnfilteredValue);
    }

    // The document's own metadata stays unencrypted, also with non-ASCII text.
    [Theory]
    [InlineData("V4-RC4")]
    [InlineData("AES-256")]
    public void The_documents_metadata_stays_unencrypted(string kind)
    {
        const string text = "<!-- Créé par Ærø -->";
        byte[] file;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            document.Catalog.GetOrCreateMetadata().SetMetadata(Xmp.Replace("<x:xmpmeta", text + "<x:xmpmeta"));
            Encrypt(document, kind);
            file = Save(document);
        }

        var utf8 = TestPdf.Latin1.GetString(Encoding.UTF8.GetBytes(text));
        Assert.Contains(utf8, TestPdf.Latin1.GetString(file));
        using var reopened = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        Assert.Contains(utf8, TestPdf.Latin1.GetString(reopened.Catalog.Elements.GetDictionary("/Metadata")!.Stream!.UnfilteredValue));
    }

    // Append mode: the document's metadata that an update writes keeps the Identity crypt filter and stays
    // unencrypted, a new metadata stream elsewhere is encrypted.
    [Theory]
    [InlineData("V4-RC4")]
    [InlineData("AES-256")]
    public void An_update_encrypts_only_metadata_other_than_the_documents(string kind)
    {
        byte[] original;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            document.Catalog.GetOrCreateMetadata().SetMetadata(Xmp);
            Encrypt(document, kind);
            original = Save(document);
        }

        byte[] result;
        using (var document = PdfReader.Open(new MemoryStream(original), "owner", PdfDocumentOpenMode.Append))
        {
            document.Catalog.GetOrCreateMetadata().SetMetadata(Xmp.Replace("<rdf:RDF", "<updated/><rdf:RDF"));
            AddStream(document, "/OtherMetadata", "/Metadata", TestPdf.Latin1.GetBytes(Xmp), false);
            result = Save(document);
        }

        Assert.Equal(original, result.Take(original.Length).ToArray());
        var update = TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
        Assert.Contains("<updated/>", update);
        Assert.NotEqual(Xmp, EncryptedEmptyDataTests.RawStream(update, "/OtherMetadata"));
        using var reopened = PdfReader.Open(new MemoryStream(result), "user", PdfDocumentOpenMode.Import);
        Assert.Contains("<updated/>", TestPdf.Latin1.GetString(reopened.Catalog.Elements.GetDictionary("/Metadata")!.Stream!.UnfilteredValue));
        Assert.Equal(Xmp, Read(reopened, "/OtherMetadata"));
    }
}
