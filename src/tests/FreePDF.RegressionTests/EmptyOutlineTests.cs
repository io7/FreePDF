// An outline root without items. The writer skipped the empty root's body but kept it in the cross-reference
// table, so the saved file pointed at the xref keyword and could not be reopened. Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class EmptyOutlineTests
{
    static PdfDocument Reopen(MemoryStream saved) => PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);

    [Fact]
    public void A_file_with_an_empty_outline_saves_a_readable_file()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R /PageMode /UseOutlines >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            "<< /Type /Outlines /Count 0 >>" });

        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
            document.Save(saved);

        using var reopened = Reopen(saved);
        Assert.Equal(1, reopened.PageCount);
        Assert.Empty(reopened.Outlines);
    }

    [Fact]
    public void A_new_document_whose_outline_stays_empty_saves_a_readable_file()
    {
        using var saved = new MemoryStream();
        using (var document = new PdfDocument())
        {
            document.AddPage();
            Assert.Empty(document.Outlines);
            document.Save(saved);
        }

        using var reopened = Reopen(saved);
        Assert.Equal(1, reopened.PageCount);
        Assert.Empty(reopened.Outlines);
    }

    [Fact]
    public void An_outline_with_items_still_saves_them()
    {
        using var saved = new MemoryStream();
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            document.Outlines.Add("Chapter", page);
            document.Save(saved);
        }

        using var reopened = Reopen(saved);
        Assert.Equal("Chapter", Assert.Single(reopened.Outlines).Title);
    }
}
