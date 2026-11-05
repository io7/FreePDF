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
}
