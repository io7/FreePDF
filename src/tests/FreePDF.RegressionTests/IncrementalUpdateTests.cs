// Original incremental PDF fixtures. MIT; see LICENSE.
using FreePDF.Lab.Fixtures;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class IncrementalUpdateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Compressed_object_indices_must_identify_the_expected_object(int markerIndex)
    {
        var error = Assert.Throws<PdfReaderException>(() => {
            using var doc = PdfReader.Open(new MemoryStream(PreservationFixtures.Incremental(markerIndex)), PdfDocumentOpenMode.Modify);
            _ = doc.PageCount;
        });
        Assert.Contains("object stream header",error.Message);
    }

    [Theory]
    [InlineData(PdfDocumentOpenMode.Modify)]
    [InlineData(PdfDocumentOpenMode.Import)]
    public void Each_compressed_object_uses_its_active_xref_entry(PdfDocumentOpenMode mode)
    {
        using var doc = PdfReader.Open(new MemoryStream(PreservationFixtures.Incremental()), mode);
        Assert.Contains(ContentReader.ReadContent(doc.Pages[0]).OfType<COperator>(), x => x.Name == "re");
        Assert.Equal("current marker", doc.Internals.Catalog.Elements.GetString("/Marker"));
        using var output = new MemoryStream();
        if (mode == PdfDocumentOpenMode.Import)
        {
            using var imported = new PdfDocument(); imported.AddPage(doc.Pages[0]); imported.Save(output);
        }
        else doc.Save(output);
        output.Position = 0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        Assert.Contains(ContentReader.ReadContent(reopened.Pages[0]).OfType<COperator>(), x => x.Name == "re");
    }

}
