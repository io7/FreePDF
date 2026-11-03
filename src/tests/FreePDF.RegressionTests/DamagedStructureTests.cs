// Damaged outline destinations, form field arrays and numbers found by the third fuzzing campaign. Each failed the
// document while opening or saving. Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DamagedStructureTests
{
    static PdfDocument OpenSaveReopen(byte[] bytes)
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
        {
            _ = document.Outlines.Count;
            document.Save(saved);
        }
        return PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
    }

    static byte[] WithOutlineDestination(string destination)
        => TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R >>",
            $"<< /Title (Chapter) /Parent 4 0 R /Dest {destination} >>" });

    [Fact]
    public void An_XYZ_destination_without_its_numbers_reads_them_as_null()
    {
        using var document = PdfReader.Open(new MemoryStream(WithOutlineDestination("[3 0 R /XYZ]")), PdfDocumentOpenMode.Modify);

        var chapter = Assert.Single(document.Outlines);
        Assert.Same(document.Pages[0], chapter.DestinationPage);
        Assert.Null(chapter.Left);
        Assert.Null(chapter.Top);
    }

    [Theory]
    [InlineData("[3 0 R /Bogus]")]
    [InlineData("[3 0 R]")]
    [InlineData("[0 /Fit]")]
    [InlineData("[1 0 R /Fit]")]
    [InlineData("[3 0 R /FitR 10]")]
    [InlineData("[3 0 R /FitH (top)]")]
    public void A_damaged_destination_does_not_stop_the_document(string destination)
    {
        using var reopened = OpenSaveReopen(WithOutlineDestination(destination));

        Assert.Equal(240, reopened.Pages[0].Width.Point);
        Assert.Equal("Chapter", Assert.Single(reopened.Outlines).Title);
    }

    static byte[] WithFields(string fields, string extraField = "<< /FT /Tx /T (name) /V (Ada) >>")
        => TestPdf.Create(new[] {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields {fields} >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            extraField });

    [Theory]
    [InlineData("[42 4 0 R]")]
    [InlineData("[1 0 R 4 0 R]")]
    [InlineData("[3 0 R 4 0 R]")]
    public void Field_array_entries_that_cannot_be_fields_are_dropped(string fields)
    {
        using var reopened = OpenSaveReopen(WithFields(fields));

        Assert.Equal(240, reopened.Pages[0].Width.Point);
        var form = reopened.Catalog.GetAcroForm()!;
        var field = Assert.Single(form.Fields.GetAllFields());
        Assert.Equal("name", field.Name);
        Assert.Equal("Ada", field.Elements.GetString("/V"));
    }

    [Fact]
    public void A_field_that_lists_itself_as_its_kid_does_not_recurse_forever()
    {
        var bytes = WithFields("[4 0 R]", "<< /FT /Tx /T (name) /V (Ada) /Kids [4 0 R] >>");

        using var reopened = OpenSaveReopen(bytes);
        Assert.Equal(1, reopened.PageCount);
    }

    [Fact]
    public void A_number_beyond_the_single_precision_range_is_clamped()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Huge 1000000000000000000000000000000000000000000000000000000000000.5 >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" });

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        Assert.Equal(float.MaxValue, document.Catalog.Elements.GetReal("/Huge"));
    }
}
