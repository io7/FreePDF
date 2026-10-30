// FreePDF regression tests. Licensed under MIT; see LICENSE.
using System.Text;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using Xunit;

namespace FreePDF.RegressionTests;

public class InlineImageTests
{
    [Theory]
    [InlineData("BI ")]
    [InlineData("BI /W 1 /H 1 ")]
    [InlineData("BI /F /A85 % truncated header\n")]
    public async Task Truncated_header_throws_instead_of_looping(string content)
    {
        var parse = Task.Run(() => Record.Exception(() => ContentReader.ReadContent(Encoding.ASCII.GetBytes(content))));
        var completed = await Task.WhenAny(parse, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.True(completed == parse, "Reading a truncated inline-image header did not terminate within two seconds.");
        Assert.IsType<ContentReaderException>(await parse);
    }

    [Theory]
    [InlineData("q BI /W 1 /H 1 /BPC 8 /CS /RGB ID abc EI Q\n")]
    [InlineData("q BI /W 1 /H 1 /F /A85 ID z~> EI Q\n")]
    public void Valid_inline_image_keeps_following_operators(string content)
    {
        var operators = ContentReader.ReadContent(Encoding.ASCII.GetBytes(content)).OfType<COperator>().Select(op => op.Name);
        Assert.Equal(new[] { "q", "BI", "ID", "EI", "Q" }, operators);
    }

    [Theory]
    [InlineData("BI /W 1 ID abc")]
    [InlineData("BI /F /A85 ID abc")]
    public void Truncated_data_throws(string content)
    {
        Assert.Throws<ContentReaderException>(() => ContentReader.ReadContent(Encoding.ASCII.GetBytes(content)));
    }
}
