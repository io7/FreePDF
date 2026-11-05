// A page whose /Rotate is an indirect integer object, as pdf-lib writes it, made importing the page throw
// NullReferenceException in the deep copy (upstream PDFsharp issue #363). Original fixture; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class ImportedIndirectRotationTests
{
    static byte[] Source() => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox 5 0 R /Rotate 4 0 R >>",
        "90",
        "[0 0 400 300]",
    });

    static PdfDocument Saved(Action<PdfDocument, PdfDocument> import)
    {
        using var source = PdfReader.Open(new MemoryStream(Source()), PdfDocumentOpenMode.Import);
        using var target = new PdfDocument();
        import(target, source);
        var output = new MemoryStream();
        target.Save(output);
        return PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import);
    }

    public static TheoryData<string> Ways() => new() { "AddPage", "InsertRange", "AppendPages" };

    [Theory]
    [MemberData(nameof(Ways))]
    public void The_imported_page_keeps_its_rotation_and_size(string way)
    {
        using var result = Saved((target, source) => {
            switch (way)
            {
                case "AddPage": target.AddPage(source.Pages[0]); break;
                case "InsertRange": target.Pages.InsertRange(0, source); break;
                default: target.AppendPages(source); break;
            }
        });

        Assert.Equal(1, result.PageCount);
        Assert.Equal(90, result.Pages[0].Rotate);
        Assert.Equal(400, result.Pages[0].MediaBox.Width, 3);
    }
}
