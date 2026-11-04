// With EncryptMetadata false, some writers leave metadata streams unencrypted without the Identity crypt filter, so
// FreePDF guesses whether a /Type /Metadata stream is unencrypted from its content. The guess decoded the stream
// with its filters, so an encrypted compressed stream threw and the file did not open; and an encrypted stream whose
// first byte happened to be '<' (one in 256) was taken for XML and not decrypted. Original fixtures; MIT, see LICENSE.
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

    // With 2,000 streams, about 8 have encrypted data that starts with '<'. The chance that none does is 0.04%.
    [Fact]
    public void Encrypted_metadata_that_starts_with_a_bracket_is_decrypted()
    {
        const int count = 2000;
        var file = WithEncryptedMetadata("AES-256", count, false);
        var startingWithBracket = Regex.Matches(TestPdf.Latin1.GetString(file), "stream\r?\n<").Count;

        using var document = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        var misread = Enumerable.Range(0, count).Count(index => Read(document, "/OtherMetadata" + index) != Xmp);
        Assert.True(misread == 0, $"{misread} of {startingWithBracket} streams whose encrypted data starts with '<' were not decrypted");
    }

    // Metadata that FreePDF leaves unencrypted, compressed or not, is still recognised as such.
    [Theory]
    [InlineData("V4-RC4", false)]
    [InlineData("AES-256", false)]
    [InlineData("AES-256", true)]
    public void Unencrypted_metadata_stays_unencrypted(string kind, bool compressed)
    {
        byte[] file;
        byte[] written;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            written = AddStream(document, "/OtherMetadata", "/Metadata", TestPdf.Latin1.GetBytes(Xmp), compressed).Stream!.Value;
            Encrypt(document, kind);
            file = Save(document);
        }

        Assert.Equal(TestPdf.Latin1.GetString(written), EncryptedEmptyDataTests.RawStream(TestPdf.Latin1.GetString(file), "/OtherMetadata"));
        using var reopened = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        Assert.Equal(Xmp, Read(reopened, "/OtherMetadata"));
    }
}
