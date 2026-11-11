// An outline nested 20,000 levels deep, as a hostile file can be. Reading outline items recursed once per level, so
// opening such a file in Append mode (which PdfSignatureVerifier does) overflowed the stack, which no catch stops; the
// test host crashed on 7.10.0. Items below PdfOutline.MaxDepth (200) are now not read. Original fixtures; MIT, see
// LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DeepOutlineTests
{
    const int Depth = 20_000;

    // Objects 1-3 are the catalog, the page tree and the page; 4 is the outline root; 5 to Depth + 4 are the items.
    static byte[] DeepOutline()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>",
        };
        for (int level = 0; level < Depth; level++)
        {
            int number = level + 5;
            int parent = level == 0 ? 4 : number - 1;
            objects.Add(level < Depth - 1
                ? $"<< /Title (L) /Parent {parent} 0 R /First {number + 1} 0 R /Last {number + 1} 0 R /Count -1 /Dest [3 0 R /Fit] >>"
                : $"<< /Title (L) /Parent {parent} 0 R /Dest [3 0 R /Fit] >>");
        }
        return TestPdf.Create(objects.ToArray());
    }

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    [Fact]
    public void A_very_deep_outline_opens_in_Append_mode_and_saves_unchanged()
    {
        var original = DeepOutline();

        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);

        Assert.Equal(original, Save(document));
    }

    [Fact]
    public void A_very_deep_outline_opens_in_Modify_mode_and_saves()
    {
        using var document = PdfReader.Open(new MemoryStream(DeepOutline()), PdfDocumentOpenMode.Modify);
        Assert.Single(document.Outlines);

        using var reopened = PdfReader.Open(new MemoryStream(Save(document)), PdfDocumentOpenMode.Import);
        Assert.Single(reopened.Outlines);
    }

    [Fact]
    public void A_very_deep_outline_can_be_merged()
    {
        using var source = PdfReader.Open(new MemoryStream(DeepOutline()), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        merged.AppendPages(source);

        Assert.Single(merged.Pages);
        Save(merged);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public async Task A_very_deep_outline_does_not_stop_the_signature_verifier()
    {
        var results = await PdfSharp.Pdf.Signatures.PdfSignatureVerifier.VerifyAsync(DeepOutline());

        Assert.Empty(results);
    }
#endif
}
