// Original text extraction tests: limits on hostile content. MIT; see LICENSE.
using System.Text;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class TextExtractionTests
{
    static string Extract(byte[] pdf, PdfTextExtractionOptions options)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return PdfTextExtractor.GetText(document.Pages[0], options: options);
    }

    [Fact]
    public void MaxDecodedBytes_and_MaxResults_stop_the_page()
    {
        var pdf = TextPdf.Page("BT /F1 12 Tf 72 700 Td " + String.Concat(Enumerable.Repeat("(x) Tj ", 100)) + "ET", WinAnsiHelvetica());
        Assert.Contains("MaxDecodedBytes", Assert.Throws<PdfReaderException>(() => Extract(pdf, new PdfTextExtractionOptions { MaxDecodedBytes = 100 })).Message);
        Assert.Contains("MaxResults", Assert.Throws<PdfReaderException>(() => Extract(pdf, new PdfTextExtractionOptions { MaxResults = 50 })).Message);
    }

    [Fact]
    public void A_flood_of_saved_states_is_counted_not_stored()
    {
        var content = new StringBuilder();
        for (int i = 0; i < 200_000; i++)
            content.Append("q ");
        content.Append("BT /F1 12 Tf 72 700 Td (after) Tj ET");
        Assert.Equal("after", TextPdf.Text(TextPdf.Page(content.ToString(), WinAnsiHelvetica())));
    }

    [Theory]
    [InlineData(0L, 32, 1)]
    [InlineData(1L, 0, 1)]
    [InlineData(1L, 257, 1)]
    [InlineData(1L, 32, 0)]
    public void Invalid_options_are_refused(long maxDecodedBytes, int maxFormDepth, int maxResults)
    {
        var pdf = TextPdf.Page(Show("x"), WinAnsiHelvetica());
        Assert.Throws<ArgumentOutOfRangeException>(() => Extract(pdf,
            new PdfTextExtractionOptions { MaxDecodedBytes = maxDecodedBytes, MaxFormDepth = maxFormDepth, MaxResults = maxResults }));
    }
}
