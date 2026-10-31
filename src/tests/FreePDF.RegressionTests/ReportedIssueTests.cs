// Independent reproducers from issue symptoms, using original fixtures. MIT.
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using Xunit;

namespace FreePDF.RegressionTests;

public class ReportedIssueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartFigure_creates_separate_open_lines(bool clone)
    {
        using var document = new PdfDocument();
        document.Options.CompressContentStreams = false;
        var page = document.AddPage();
        var path = new XGraphicsPath();
        path.AddLine(10, 10, 50, 10);
        path.StartFigure();
        if (clone) path = path.Clone();
        path.AddLine(10, 30, 50, 30);
        using (var gfx = XGraphics.FromPdfPage(page)) gfx.DrawPath(XPens.Black, path);
        var names = ContentReader.ReadContent(page).OfType<COperator>().Select(op => op.Name).ToArray();
        Assert.Equal(2, names.Count(name => name == "m"));
        Assert.Equal(2, names.Count(name => name == "l"));
        Assert.DoesNotContain("h", names);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void Full_width_vertically_merged_cells_render(int columns, int rows)
    {
        TestFonts.Initialize();
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = "FreePDF Synthetic";
        var table = document.AddSection().AddTable();
        for (var i = 0; i < columns; i++) table.AddColumn(Unit.FromCentimeter(2));
        for (var i = 0; i < rows; i++) table.AddRow();
        table[0, 0].MergeRight = columns - 1;
        table[0, 0].MergeDown = rows - 1;
        table[0, 0].AddParagraph("Merged across every column and row");
        table.Borders.Width = 1;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        Assert.True(renderer.PdfDocument.PageCount > 0);
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output);
        Assert.True(output.Length > 500);
        renderer.PdfDocument.Dispose();
    }

    [Theory]
    [InlineData("<")]
    [InlineData("<4")]
    [InlineData("<41")]
    [InlineData("<41 ")]
    public async Task Truncated_hex_content_throws_promptly(string text)
    {
        var parse = Task.Run(() => Record.Exception(() => ContentReader.ReadContent(Encoding.ASCII.GetBytes(text))));
        Assert.Same(parse, await Task.WhenAny(parse, Task.Delay(TimeSpan.FromSeconds(2))));
        Assert.IsType<ContentReaderException>(await parse);
    }

    [Theory]
    [InlineData("<41> Tj", "A")]
    [InlineData("<4> Tj", "@")]
    [InlineData("<4 1> Tj", "A")]
    public void Hex_content_keeps_valid_odd_length_and_whitespace_behavior(string text, string expected)
    {
        var op = Assert.Single(ContentReader.ReadContent(Encoding.ASCII.GetBytes(text)).OfType<COperator>());
        Assert.Equal(expected, Assert.IsType<CString>(Assert.Single(op.Operands)).Value);
    }
}
