// FreePDF 7.6.0 gave an encrypted document without metadata a catalog /Metadata entry that was a direct dictionary
// holding only the Identity crypt filter and no stream, which ISO 32000-1 Table 28 does not allow (the entry is an
// indirect reference to a metadata stream). Original fixtures; MIT, see LICENSE.
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class StrayMetadataEntryTests
{
    const string Stray = "/Metadata << /Filter [/Crypt] /DecodeParms [<< /Type /CryptFilterDecodeParms /Name /Identity >>] >>";

    static byte[] WithStrayEntry() => TestPdf.Create(new[] {
        $"<< /Type /Catalog /Pages 2 0 R {Stray} >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
    });

    static byte[] Save(byte[] original, PdfDocumentOpenMode mode, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), mode);
        change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    [Fact]
    public void A_full_save_leaves_the_stray_entry_out()
    {
        var result = Save(WithStrayEntry(), PdfDocumentOpenMode.Modify, document => document.Info.Title = "Changed");

        Assert.DoesNotMatch(new Regex(@"/Metadata\s*<<"), TestPdf.Latin1.GetString(result));
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal("Changed", reopened.Info.Title);
    }

    [Fact]
    public void An_update_that_does_not_change_the_catalog_keeps_the_file()
    {
        var original = WithStrayEntry();
        var result = Save(original, PdfDocumentOpenMode.Append, document => document.Info.Title = "Changed");

        Assert.Equal(original, result.Take(original.Length).ToArray());
        var update = TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
        Assert.DoesNotContain("/Catalog", update);
    }
}
