// Streams whose 'endstream' is not right behind the data /Length describes. FreePDF looked only 20 bytes past
// /Length, so a correct /Length followed by more padding (as IBM i invoice generators and Bluebeam write), or a
// /Length much too short, made the whole file fail to open ("Cannot retrieve stream length"). Upstream PDFsharp
// issues #347, #29, #186 and #216 report it; the fixtures are our own. MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class StreamEndSearchTests
{
    const string Content = "0 0 m 240 200 l S";

    // Object 4 is the page content; object 5 follows it, so a search that runs too far would show.
    static byte[] File(string contentObject) => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R /Resources << >> >>",
        contentObject,
        "<< /Length 6 >>\nstream\nq 1 0 \nendstream",
    });

    static string ReadContent(byte[] bytes, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Import)
    {
        using var document = PdfReader.Open(new MemoryStream(bytes), mode);
        return Encoding.ASCII.GetString(document.Pages[0].Elements.GetDictionary("/Contents")!.Stream!.Value);
    }

    [Theory]
    [InlineData("\n\n\n\n\n\n\n\n\n\n\n\n")]
    [InlineData("\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n")]
    [InlineData("                                        \n")]
    public void A_correct_length_followed_by_padding_keeps_its_data(string padding)
    {
        var bytes = File($"<< /Length {Content.Length} >>\nstream\n{Content}{padding}endstream");

        Assert.Equal(Content, ReadContent(bytes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(17)]
    public void A_length_much_too_short_is_corrected_to_end_before_the_line_break(int missing)
    {
        var bytes = File($"<< /Length {Content.Length - missing} >>\nstream\n{Content}\nendstream");

        Assert.Equal(Content, ReadContent(bytes));
    }

    [Fact]
    public void A_much_too_short_length_in_a_long_stream_is_corrected()
    {
        var content = string.Join("\n", Enumerable.Repeat(Content, 40));
        var bytes = File($"<< /Length {content.Length - 300} >>\nstream\n{content}\r\nendstream");

        Assert.Equal(content, ReadContent(bytes));
    }

    [Fact]
    public void Binary_data_followed_by_padding_keeps_every_byte()
    {
        // A 2 x 1 RGB image: six bytes that include values that look like white space.
        var pixels = "\x0A\x0D\x20\x00\x09\x0C";
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Resources << /XObject << /Im 4 0 R >> >> >>",
            $"<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 6 >>\nstream\n{pixels}\n\n\n\n\n\n\n\n\n\n\n\n\n\nendstream",
        });

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        var image = document.Pages[0].Resources.Elements.GetDictionary("/XObject")!.Elements.GetDictionary("/Im")!;
        Assert.Equal(TestPdf.Latin1.GetBytes(pixels), image.Stream!.Value);
    }

    [Fact]
    public void The_following_object_is_read_as_it_is_and_the_file_saves()
    {
        var bytes = File($"<< /Length {Content.Length - 12} >>\nstream\n{Content}\n\n\n\n\n\nendstream");

        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import))
        {
            var next = (PdfDictionary)document.Internals.GetObject(new PdfObjectID(5))!;
            Assert.Equal("q 1 0 ", Encoding.ASCII.GetString(next.Stream!.Value));
        }
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
            document.Save(saved);
        Assert.Equal(Content + "\n\n\n\n\n", ReadContent(saved.ToArray()));
    }
}
