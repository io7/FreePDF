// Original text extraction tests: painted line segments. MIT; see LICENSE.
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    static IReadOnlyList<PdfLineSegment> Segments(string content)
    {
        using var document = PdfReader.Open(new MemoryStream(TextPdf.Page(content)), PdfDocumentOpenMode.Import);
        return PdfLineExtractor.GetLineSegments(document.Pages[0]);
    }

    [Fact]
    public void Stroked_lines_and_filled_rectangles_become_segments_in_page_space()
    {
        var segments = Segments("q 2 0 0 2 0 0 cm 10 20 m 10 70 l S Q 0 0 50 10 re f 5 5 m 9 9 9 9 20 20 c S 1 1 m 2 2 l n");
        Assert.Equal(5, segments.Count);
        var rule = segments[0];
        Assert.True(rule.Stroked);
        Assert.Equal((20.0, 40.0, 20.0, 140.0), (rule.X1, rule.Y1, rule.X2, rule.Y2));
        Assert.All(segments.Skip(1), s => Assert.False(s.Stroked));
    }
}
