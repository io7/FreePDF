// Catalog entries whose value is null or a reference to a missing object. ISO 32000-1 7.3.9 and 7.3.10: such an
// entry is the same as an absent one. FreePDF threw a type mismatch instead. Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class NullCatalogEntryTests
{
    static PdfDocument SaveAndReopen(PdfDocument document)
    {
        using var saved = new MemoryStream();
        document.Save(saved);
        return PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Modify);
    }

    [Theory]
    [InlineData("/Metadata null")]
    [InlineData("/Metadata 99 0 R")]
    [InlineData("/Outlines null")]
    [InlineData("/Outlines 99 0 R")]
    public void A_null_or_dangling_optional_entry_is_treated_as_absent(string entry)
    {
        using var document = TestPdf.OpenCatalog(entry);

        Assert.Empty(document.Outlines);
        document.Outlines.Add("Added after opening", document.Pages[0]);

        using var reopened = SaveAndReopen(document);
        Assert.Equal("Added after opening", Assert.Single(reopened.Outlines).Title);
    }

    [Theory]
    [InlineData("<< /Type /Catalog /Pages null >>")]
    [InlineData("<< /Type /Catalog /Pages 99 0 R >>")]
    public void A_null_or_dangling_page_tree_is_a_reader_error(string catalog)
    {
        var bytes = TestPdf.Create(new[] { catalog, "<< /Type /Page /MediaBox [0 0 240 200] >>" });

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify));
    }
}
