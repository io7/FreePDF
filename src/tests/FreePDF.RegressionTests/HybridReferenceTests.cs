// Original hybrid-reference PDF, following ISO 32000-1 section 7.5.8.4. MIT.
using System.Globalization;
using System.Text;
using PdfSharp.Pdf.IO;
using Xunit;
using FreePDF.Lab.Fixtures;

namespace FreePDF.RegressionTests;

public class HybridReferenceTests
{
    [Theory]
    [InlineData(false, "compressed marker")]
    [InlineData(true, "newer marker")]
    public void Hybrid_reference_resolves_compressed_objects_and_keeps_newer_revisions(bool newer, string expected)
    {
        using var document = PdfReader.Open(new MemoryStream(PdfFixtureData.Hybrid(newer)), PdfDocumentOpenMode.Modify);
        Assert.Equal(expected, document.Catalog.Elements.GetString("/HybridMarker"));
        Assert.Equal(1, document.PageCount);
        using var output = new MemoryStream(); document.Save(output); output.Position=0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        Assert.Equal(expected, reopened.Catalog.Elements.GetString("/HybridMarker"));
    }

    [Fact]
    public void New_compressed_version_wins_over_an_older_file_level_object()
    {
        using var document = PdfReader.Open(new MemoryStream(PdfFixtureData.Hybrid(false, true)), PdfDocumentOpenMode.Modify);
        Assert.Equal("compressed marker", document.Catalog.Elements.GetString("/HybridMarker"));
    }
}
