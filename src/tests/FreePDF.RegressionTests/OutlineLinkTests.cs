// Outline /First and /Next links that do not lead to outline items. Reading the outline converted whatever they
// pointed at, so a link to the catalog or a page destroyed that object and saving failed; a link to a non-dictionary
// threw InvalidCastException. Found by mutation fuzzing. Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class OutlineLinkTests
{
    static byte[] Fixture(string first, params string[] more)
        => TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            $"<< /Type /Outlines /First {first} >>" }.Concat(more).ToArray());

    static PdfDocument OpenSaveReopen(byte[] bytes, out int outlinesWhenOpened)
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
        {
            outlinesWhenOpened = document.Outlines.Count;
            document.Save(saved);
        }
        return PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
    }

    [Theory]
    [InlineData("1 0 R")]
    [InlineData("3 0 R")]
    public void A_link_to_another_kind_of_object_ends_the_outline(string first)
    {
        using var reopened = OpenSaveReopen(Fixture(first), out var outlines);

        Assert.Equal(0, outlines);
        Assert.Equal(1, reopened.PageCount);
        Assert.Equal(240, reopened.Pages[0].Width.Point);
    }

    [Fact]
    public void A_link_to_a_number_ends_the_outline()
    {
        using var reopened = OpenSaveReopen(Fixture("5 0 R", "42"), out var outlines);

        Assert.Equal(0, outlines);
        Assert.Equal(1, reopened.PageCount);
    }

    [Fact]
    public void A_next_link_back_to_an_earlier_item_ends_the_outline()
    {
        var bytes = Fixture("5 0 R",
            "<< /Title (One) /Parent 4 0 R /Next 6 0 R >>",
            "<< /Title (Two) /Parent 4 0 R /Prev 5 0 R /Next 5 0 R >>");

        using var reopened = OpenSaveReopen(bytes, out var outlines);

        Assert.Equal(2, outlines);
        Assert.Equal(new[] { "One", "Two" }, reopened.Outlines.Select(o => o.Title));
    }

    [Fact]
    public void Count_and_colour_entries_of_the_wrong_type_are_ignored()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count /Many >>",
            "<< /Title (One) /Parent 4 0 R /Count (two) /C [1 (red) 0] >>" });

        using var reopened = OpenSaveReopen(bytes, out var outlines);

        Assert.Equal(1, outlines);
        Assert.Equal("One", Assert.Single(reopened.Outlines).Title);
    }
}
