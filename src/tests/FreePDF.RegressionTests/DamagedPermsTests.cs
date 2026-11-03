// AES-256 files whose /Perms is not the 16-byte string ISO 32000-2 Table 21 requires. Validating the permissions
// handed it to the cipher, which threw CryptographicException. Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DamagedPermsTests
{
    // An AES-256 file encrypted by FreePDF, with /Perms replaced by a shorter string. The replacement is padded with
    // spaces to the original length, so no offset changes.
    static byte[] WithPerms(string perms)
    {
        using var document = new PdfDocument();
        document.Options.Layout = PdfWriterLayout.Compact;
        document.AddPage();
        document.SecurityHandler.SetEncryptionToV5();
        document.SecuritySettings.UserPassword = "user";
        document.SecuritySettings.OwnerPassword = "owner";
        using var stream = new MemoryStream();
        document.Save(stream);

        var text = TestPdf.Latin1.GetString(stream.ToArray());
        var match = Regex.Match(text, @"/Perms\s*(<[0-9A-Fa-f]*>|\((?:\\.|[^\\)])*\))", RegexOptions.Singleline);
        Assert.True(match.Success, "Expected a /Perms string.");
        var value = match.Groups[1];
        Assert.True(perms.Length <= value.Length);
        return TestPdf.Latin1.GetBytes(text.Substring(0, value.Index) + perms.PadRight(value.Length) + text.Substring(value.Index + value.Length));
    }

    [Theory]
    [InlineData("<00112233>")]
    [InlineData("()")]
    public void A_perms_string_of_the_wrong_length_is_a_reader_error(string perms)
    {
        var bytes = WithPerms(perms);

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), "user", PdfDocumentOpenMode.Import));
    }
}
