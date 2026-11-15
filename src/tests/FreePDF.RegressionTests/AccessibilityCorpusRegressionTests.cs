// Minimized original reproducer for a Chromium corpus rendering difference.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using Xunit;

namespace FreePDF.RegressionTests;

public class AccessibilityCorpusRegressionTests
{
    [Fact]
    public void Round_trip_preserves_page_box_precision_used_by_content_transforms()
    {
        using var bytes = new MemoryStream();
        using (var pdf = new PdfDocument())
        {
            var page = pdf.AddPage();
            page.Elements.SetRectangle("/MediaBox", new PdfRectangle(new XPoint(0, 0), new XPoint(841.91998, 594.95996)));
            pdf.Save(bytes);
        }
        using var imported = PdfReader.Open(new MemoryStream(bytes.ToArray()), PdfDocumentOpenMode.Modify);
        using var resaved = new MemoryStream(); imported.Save(resaved);
        using var reopened = PdfReader.Open(new MemoryStream(resaved.ToArray()), PdfDocumentOpenMode.Import);
        var box = reopened.Pages[0].MediaBox;
        Assert.Equal(841.91998, box.X2, 7);
        Assert.Equal(594.95996, box.Y2, 7);
    }
}
