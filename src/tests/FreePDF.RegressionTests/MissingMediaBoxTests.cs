// A page without a usable MediaBox, neither its own nor inherited. ISO 32000-1 Table 30 requires one; Poppler and MuPDF
// show such a page at US Letter size. FreePDF threw InvalidOperationException while opening.
// Found by mutation fuzzing. Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class MissingMediaBoxTests
{
    internal static readonly string[] PageWithoutMediaBox = {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R >>" };

    [Theory]
    [InlineData(PdfDocumentOpenMode.Import)]
    [InlineData(PdfDocumentOpenMode.Modify)]
    public void A_page_without_a_media_box_is_US_Letter(PdfDocumentOpenMode mode)
    {
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Create(PageWithoutMediaBox)), mode);

        Assert.Equal(612, document.Pages[0].Width.Point);
        Assert.Equal(792, document.Pages[0].Height.Point);
    }

    [Theory]
    [InlineData("[0 0 240]")]
    [InlineData("[0 0 (wide) 200]")]
    [InlineData("/Letter")]
    public void A_page_with_a_malformed_media_box_is_US_Letter(string mediaBox)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} >>" });

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        Assert.Equal(612, document.Pages[0].Width.Point);
    }

    [Fact]
    public void Saving_writes_the_media_box()
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(TestPdf.Create(PageWithoutMediaBox)), PdfDocumentOpenMode.Modify))
            document.Save(saved);

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        var box = reopened.Pages[0].Elements.GetRectangle("/MediaBox");
        Assert.Equal((0d, 0d, 612d, 792d), (box.X1, box.Y1, box.X2, box.Y2));
    }
}
