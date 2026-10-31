// Original shared-stream ownership fixtures. MIT; see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DictionaryOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accessing_another_page_does_not_invalidate_an_existing_typed_content(bool array)
    {
        const string content = "0 1 0 rg 10 10 20 20 re f";
        var contents = array ? "[5 0 R]" : "5 0 R";
        using var doc = PdfReader.Open(new MemoryStream(TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents {contents} >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents {contents} >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"
        })), PdfDocumentOpenMode.Modify);
        var first = doc.Pages[0].Contents.Elements.GetDictionary(0)!;
        var second = doc.Pages[1].Contents.Elements.GetDictionary(0)!;
        Assert.Equal(content, Encoding.ASCII.GetString(first.Stream!.UnfilteredValue));
        Assert.Same(first, second);
        using var saved = new MemoryStream(); doc.Save(saved); saved.Position = 0;
        using var reopened = PdfReader.Open(saved, PdfDocumentOpenMode.Modify);
        Assert.Equal(2, reopened.PageCount);
    }
}
