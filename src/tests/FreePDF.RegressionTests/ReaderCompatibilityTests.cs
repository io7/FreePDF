// Original PDF fixtures for inheritance, stream boundaries and editing. MIT.
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Xunit;
using FreePDF.Lab.Fixtures;

namespace FreePDF.RegressionTests;

public class ReaderCompatibilityTests
{
    [Theory]
    [InlineData(PdfDocumentOpenMode.Modify)]
    [InlineData(PdfDocumentOpenMode.Import)]
    public void Page_attributes_inherit_through_missing_intermediate_values_without_leaking_to_siblings(PdfDocumentOpenMode mode)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 3 /MediaBox [0 0 300 400] /CropBox [10 20 290 380] /Rotate 90 /Resources << /Marker (root) >> >>",
            "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] /Count 2 /MediaBox [0 0 100 200] /Resources << /Marker (branch) >> /Rotate 0 >>",
            "<< /Type /Pages /Parent 2 0 R /Kids [7 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 3 0 R >>",
            "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 50 60] /Resources << /Marker (leaf) >> >>",
            "<< /Type /Page /Parent 4 0 R >>"
        });
        using var document = PdfReader.Open(new MemoryStream(bytes), mode);
        Assert.Equal(3, document.PageCount);
        Assert.Equal(new[] {100d,50d,300d}, ((IEnumerable<PdfPage>)document.Pages).Select(p => p.MediaBox.Width));
        Assert.Equal(new[] {200d,60d,400d}, ((IEnumerable<PdfPage>)document.Pages).Select(p => p.MediaBox.Height));
        Assert.Equal(new[] {0,0,90}, ((IEnumerable<PdfPage>)document.Pages).Select(p => p.Rotate));
        Assert.Equal(new[] {"branch","leaf","root"}, ((IEnumerable<PdfPage>)document.Pages).Select(p => p.Elements.GetDictionary("/Resources")!.Elements.GetString("/Marker")));
        Assert.All<PdfPage>(document.Pages, p => Assert.Equal(280d, p.CropBox.Width));
        using var output = new MemoryStream();
        if (mode == PdfDocumentOpenMode.Import)
        {
            using var imported = new PdfDocument();
            foreach (var page in document.Pages) imported.AddPage(page);
            imported.Save(output);
        }
        else document.Save(output);
        output.Position = 0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        Assert.Equal(new[] {100d,50d,300d}, ((IEnumerable<PdfPage>)reopened.Pages).Select(p => p.MediaBox.Width));
        Assert.Equal(new[] {200d,60d,400d}, ((IEnumerable<PdfPage>)reopened.Pages).Select(p => p.MediaBox.Height));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(25)]
    [InlineData(100000)]
    [InlineData(-1)]
    public void Incorrect_stream_lengths_never_consume_the_following_object(int declared)
    {
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>", ReaderFixtureData.SimplePage[1],
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents [4 0 R 5 0 R] >>",
            $"<< /Length {declared} >>\nstream\nq\nQ\n\nendstream",
            "<< /Length 4 >>\nstream\nq\nQ\n\nendstream"
        })), PdfDocumentOpenMode.Modify);
        var contents = document.Pages[0].Contents;
        Assert.Equal(2, contents.Elements.Count);
        Assert.Equal("q\nQ", Encoding.ASCII.GetString(contents.Elements.GetDictionary(0)!.Stream!.UnfilteredValue).TrimEnd('\r','\n'));
        Assert.Equal("q\nQ", Encoding.ASCII.GetString(contents.Elements.GetDictionary(1)!.Stream!.UnfilteredValue).TrimEnd('\r','\n'));
        using var output = new MemoryStream(); document.Save(output); output.Position = 0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        Assert.Equal(4, ContentReader.ReadContent(reopened.Pages[0]).OfType<COperator>().Count());
    }

    [Theory]
    [InlineData(false, false, XGraphicsPdfPageOptions.Append)]
    [InlineData(true, false, XGraphicsPdfPageOptions.Append)]
    [InlineData(true, true, XGraphicsPdfPageOptions.Append)]
    [InlineData(true, true, XGraphicsPdfPageOptions.Prepend)]
    [InlineData(false, false, XGraphicsPdfPageOptions.Prepend)]
    [InlineData(false, true, XGraphicsPdfPageOptions.Append)]
    public void Editing_multiple_content_streams_preserves_original_operators_and_graphics_state(bool filtered, bool comment, XGraphicsPdfPageOptions options)
    {
        var last = "0 0 10 10 re f" + (comment ? " % last comment" : "");
        string Stream(string data)
        {
            var encoded = filtered ? BitConverter.ToString(Encoding.ASCII.GetBytes(data)).Replace("-", "") + ">" : data;
            return $"<< /Length {encoded.Length} {(filtered ? "/Filter /ASCIIHexDecode" : "")} >>\nstream\n{encoded}\nendstream";
        }
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Create(new[] {
            ReaderFixtureData.SimplePage[0], ReaderFixtureData.SimplePage[1],
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents [4 0 R 5 0 R] >>",
            Stream("1 0 0 rg"), Stream(last)
        })), PdfDocumentOpenMode.Modify);
        using (var graphics = XGraphics.FromPdfPage(document.Pages[0], options))
            graphics.DrawRectangle(XBrushes.Blue, 20, 20, 20, 20);
        using var output = new MemoryStream(); document.Save(output); output.Position = 0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        var names = ContentReader.ReadContent(reopened.Pages[0]).OfType<COperator>().Select(c => c.Name).ToArray();
        Assert.Equal(2, names.Count(n => n == "re"));
        Assert.Equal(names.Count(n => n == "q"), names.Count(n => n == "Q"));
        Assert.Equal(2, names.Count(n => n == "f"));
    }

    [Fact]
    public void Editing_a_page_does_not_change_a_stream_shared_with_another_page()
    {
        const string paint = "0 1 0 rg 10 10 20 20 re f % retained comment";
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Create(new[] {
            ReaderFixtureData.SimplePage[0], "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 5 0 R >>",
            $"<< /Length {paint.Length} >>\nstream\n{paint}\nendstream"
        })), PdfDocumentOpenMode.Modify);
        using (var graphics = XGraphics.FromPdfPage(document.Pages[0])) graphics.DrawRectangle(XBrushes.Blue, 50, 50, 20, 20);
        Assert.Equal(paint, Encoding.ASCII.GetString(document.Pages[1].Contents.Elements.GetDictionary(0)!.Stream!.UnfilteredValue));
        using var output = new MemoryStream(); document.Save(output); output.Position=0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Modify);
        Assert.Equal(paint, Encoding.ASCII.GetString(reopened.Pages[1].Contents.Elements.GetDictionary(0)!.Stream!.UnfilteredValue));
        var names = ContentReader.ReadContent(reopened.Pages[0]).OfType<COperator>().Select(c => c.Name).ToArray();
        Assert.Equal(names.Count(n => n == "q"), names.Count(n => n == "Q"));
    }
}
