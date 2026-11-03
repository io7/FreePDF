// Object streams whose dictionary or header does not describe their data, or references to "object streams" that are
// something else. The reader threw OverflowException or NullReferenceException, allocated /N entries before reading
// any, or converted the referenced object into an object stream. Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class ObjectStreamHeaderTests
{
    const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
    const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>";

    // Catalog (1) at file level; page tree (2) and page (3) compressed in object stream 4; cross-reference stream 5.
    // streamEntries replaces the object stream's /N and /First, header its header, and container the object number
    // the compressed entries name as their object stream.
    static byte[] File(string? streamEntries = null, string? header = null, int container = 4)
    {
        header ??= $"2 0 3 {Pages.Length + 1} ";
        var data = header + Pages + " " + Page;
        streamEntries ??= $"/N 2 /First {header.Length}";

        var text = new StringBuilder("%PDF-1.5\n");
        var offset1 = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var offset4 = text.Length;
        text.Append($"4 0 obj\n<< /Type /ObjStm {streamEntries} /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n");
        var offset5 = text.Length;

        var rows = $"00000000 01{offset1:X4}00 02{container:X4}00 02{container:X4}01 01{offset4:X4}00 01{offset5:X4}00".Replace(" ", "") + ">";
        text.Append($"5 0 obj\n<< /Type /XRef /Size 6 /W [1 2 1] /Root 1 0 R /Filter /ASCIIHexDecode /Length {rows.Length} >>\n" +
                    $"stream\n{rows}\nendstream\nendobj\nstartxref\n{offset5}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    static PdfDocument Open(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);

    [Fact]
    public void A_well_formed_object_stream_opens()
    {
        using var document = Open(File());
        Assert.Equal(240, document.Pages[0].Width.Point);
    }

    [Theory]
    [InlineData("/N 2000000000 /First 10")]
    [InlineData("/N -1 /First 10")]
    [InlineData("/N (two) /First 10")]
    [InlineData("/First 10")]
    [InlineData("/N 2 /First -5")]
    [InlineData("/N 2 /First 99999")]
    public void An_invalid_N_or_First_is_a_reader_error(string streamEntries)
    {
        Assert.Throws<PdfReaderException>(() => Open(File(streamEntries)));
    }

    [Theory]
    [InlineData("2 0 3 2147483647 ")]
    [InlineData("2 0 -3 42 ")]
    [InlineData("2 0 3 -42 ")]
    public void An_invalid_header_entry_is_a_reader_error(string header)
    {
        Assert.Throws<PdfReaderException>(() => Open(File(header: header)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void A_compressed_object_whose_container_is_not_an_object_stream_is_a_reader_error(int container)
    {
        Assert.Throws<PdfReaderException>(() => Open(File(container: container)));
    }
}
