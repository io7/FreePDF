// Encrypted files with an incremental update whose trailer names the encryption dictionary again, as Acrobat and
// pyHanko write them. Reading them failed in every open mode: the newest trailer's /Encrypt reference was a placeholder
// (the dictionary is in the older section), so the dictionary was read into it and the table's copy was found dead
// ("TODO: REPORT A BUG"); and a second trailer naming the same dictionary converted it again. Original fixtures;
// MIT, see LICENSE.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class EncryptedIncrementalUpdateTests
{
    static byte[] Encrypted(string kind)
    {
        var plain = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            "<< /Title (Original title) >>",
        }, _ => "/Info 4 0 R /ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]");
        using var document = PdfReader.Open(new MemoryStream(plain), PdfDocumentOpenMode.Modify);
        if (kind == "AES-256")
            document.SecurityHandler.SetEncryptionToV5();
        else
            document.SecurityHandler.SetEncryptionToV4UsingAES();
        document.SecuritySettings.UserPassword = "user";
        document.SecuritySettings.OwnerPassword = "owner";
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // Appends an update that adds an object without strings and a trailer that names /Encrypt, /Root and /Info again.
    static byte[] WithUpdate(byte[] original)
    {
        var text = TestPdf.Latin1.GetString(original);
        var trailer = text.Substring(text.LastIndexOf("trailer", StringComparison.Ordinal));
        string Entry(string key) => Regex.Match(trailer, $@"/{key}\s+(\d+ \d+ R)").Groups[1].Value;
        var size = int.Parse(Regex.Match(trailer, @"/Size\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var id = Regex.Match(trailer, @"/ID\s*\[[^\]]*\]").Value;
        var startXRef = Regex.Match(text.Substring(text.LastIndexOf("startxref", StringComparison.Ordinal)), @"startxref\s+(\d+)").Groups[1].Value;

        var update = new StringBuilder();
        var position = original.Length;
        var obj = $"{size} 0 obj\n<< /Marker 42 >>\nendobj\n";
        update.Append(obj);
        var xref = position + obj.Length;
        update.Append($"xref\n{size} 1\n{position:0000000000} 00000 n \n");
        update.Append($"trailer\n<< /Size {size + 1} /Prev {startXRef} /Root {Entry("Root")} /Info {Entry("Info")} /Encrypt {Entry("Encrypt")} {id} >>\n");
        update.Append($"startxref\n{xref}\n%%EOF\n");
        return original.Concat(TestPdf.Latin1.GetBytes(update.ToString())).ToArray();
    }

    [Theory]
    [InlineData("AES-128", PdfDocumentOpenMode.Import)]
    [InlineData("AES-128", PdfDocumentOpenMode.Modify)]
    [InlineData("AES-256", PdfDocumentOpenMode.Import)]
    [InlineData("AES-256", PdfDocumentOpenMode.Modify)]
    public void An_encrypted_file_with_an_update_opens(string kind, PdfDocumentOpenMode mode)
    {
        var file = WithUpdate(Encrypted(kind));

        using var document = PdfReader.Open(new MemoryStream(file), "owner", mode);

        Assert.Equal("Original title", document.Info.Title);
        Assert.Equal(1, document.PageCount);
    }
}
