// Encryption dictionaries whose values do not fit together, for example crypt filters with a revision that has none.
// The checks that guard writing threw InvalidOperationException while the file was opened. Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class InvalidEncryptionDictionaryTests
{
    const string Hash = "<00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF>";

    [Theory]
    [InlineData("/V 2 /R 3 /Length 128 /CF << /StdCF << /CFM /V2 >> >> /StmF /StdCF /StrF /StdCF")]
    [InlineData("/V 4 /R 4 /Length 128 /CF << /StdCF << /CFM /AESV3 >> >> /StmF /StdCF /StrF /StdCF")]
    [InlineData("/V 5 /R 6 /Length 64")]
    public void An_inconsistent_encryption_dictionary_is_a_reader_error(string entries)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            $"<< /Filter /Standard {entries} /O {Hash} /U {Hash} /P -4 >>" },
            _ => "/Encrypt 4 0 R /ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]");

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import));
    }
}
