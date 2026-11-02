// Encrypted files whose trailer holds the encryption dictionary directly (as MuPDF writes them) instead of an
// indirect reference. ISO 32000 allows both. Found by the corpus round trip. Original fixtures; MIT, see LICENSE.
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DirectEncryptionDictionaryTests
{
    // A one-page PDF with a filled rectangle, encrypted by FreePDF, then rewritten so that the trailer carries the
    // encryption dictionary itself. The indirect object stays in the file, unreferenced; xref offsets do not change
    // because only the trailer, after the cross-reference table, is edited.
    static byte[] DirectlyEncrypted(bool aes256, string userPassword)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
            graphics.DrawRectangle(XBrushes.Red, 10, 10, 50, 50);
        if (aes256) document.SecurityHandler.SetEncryptionToV5();
        else document.SecurityHandler.SetEncryptionToV4UsingAES();
        document.SecuritySettings.UserPassword = userPassword;
        document.SecuritySettings.OwnerPassword = "owner";
        using var stream = new MemoryStream();
        document.Save(stream);

        var text = Encoding.Latin1.GetString(stream.ToArray());
        var trailerAt = text.LastIndexOf("trailer", StringComparison.Ordinal);
        Assert.True(trailerAt > 0, "Expected a classic trailer.");
        var reference = Regex.Match(text[trailerAt..], @"/Encrypt\s+(\d+)\s+(\d+)\s+R");
        Assert.True(reference.Success, "Expected an indirect /Encrypt in the trailer.");
        var body = Regex.Match(text, $@"(?<!\d){reference.Groups[1].Value}\s+{reference.Groups[2].Value}\s+obj\s*(<<.*?>>)\s*endobj", RegexOptions.Singleline);
        Assert.True(body.Success, "Expected the encryption dictionary object.");
        var trailer = text[trailerAt..].Replace(reference.Value, "/Encrypt " + body.Groups[1].Value);
        return Encoding.Latin1.GetBytes(text[..trailerAt] + trailer);
    }

    static string Content(PdfDocument document) =>
        Encoding.Latin1.GetString(document.Pages[0].Contents.Elements.GetDictionary(0)!.Stream!.UnfilteredValue);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_user_password_is_required(bool aes256)
    {
        var bytes = DirectlyEncrypted(aes256, "user");

        var refused = Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import));
        Assert.Contains("password", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_user_password_decrypts_the_content(bool aes256)
    {
        var bytes = DirectlyEncrypted(aes256, "user");

        using var document = PdfReader.Open(new MemoryStream(bytes), "user", PdfDocumentOpenMode.Import);

        Assert.Contains(" re", Content(document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_owner_only_file_opens_for_reading_but_not_for_editing_without_the_owner_password(bool aes256)
    {
        var bytes = DirectlyEncrypted(aes256, "");

        using (var reading = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import))
            Assert.Contains(" re", Content(reading));
        var refused = Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify));
        Assert.Contains("owner password", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_owner_password_allows_editing_and_the_saved_file_stays_readable(bool aes256)
    {
        var bytes = DirectlyEncrypted(aes256, "user");

        using var saved = new MemoryStream();
        using (var editing = PdfReader.Open(new MemoryStream(bytes), "owner", PdfDocumentOpenMode.Modify))
            editing.Save(saved);
        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), "user", PdfDocumentOpenMode.Import);

        Assert.Contains(" re", Content(reopened));
    }
}
