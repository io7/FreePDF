// Cross-reference stream dictionaries that do not describe their data. The reader threw InvalidOperationException,
// IndexOutOfRangeException or a type mismatch instead of PdfReaderException. Found by mutation fuzzing.
// Original fixtures; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class XRefStreamHeaderTests
{
    // Catalog, page tree and page, then the cross-reference stream as object 4. Each entry is a type byte (left out
    // when typeField is false), a 2-byte offset and a generation byte; entries cover objects 0 to 4 unless
    // firstObject skips the free entry for object 0.
    static byte[] File(string entries, int keepEntries = 5, bool typeField = true, int firstObject = 0, string filter = "/ASCIIHexDecode", int pageOffset = -1)
    {
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>" };
        var text = new StringBuilder("%PDF-1.5\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(text.Length);
            text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = text.Length;
        offsets.Add(xref);
        if (pageOffset >= 0)
            offsets[2] = pageOffset;

        var rows = new List<string> { (typeField ? "00" : "") + "000000" };
        rows.AddRange(offsets.Select(o => (typeField ? "01" : "") + $"{o:X4}00"));
        var data = string.Concat(rows.Skip(firstObject).Take(keepEntries)) + ">";
        text.Append($"4 0 obj\n<< /Type /XRef {entries} /Root 1 0 R /Filter {filter} /Length {data.Length} >>\n" +
                    $"stream\n{data}\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    static PdfDocument Open(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);

    [Fact]
    public void A_well_formed_stream_opens()
    {
        using var document = Open(File("/Size 5 /W [1 2 1]"));
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void Entries_without_a_type_field_are_in_use_objects()
    {
        // ISO 32000-1 Table 17: a zero first /W element means the type field is absent and defaults to 1.
        using var document = Open(File("/Size 5 /W [0 2 1] /Index [1 4]", keepEntries: 4, typeField: false, firstObject: 1));
        Assert.Equal(1, document.PageCount);
    }

    [Theory]
    [InlineData("/W [1 2 1]")]
    [InlineData("/Size (five) /W [1 2 1]")]
    [InlineData("/Size -5 /W [1 2 1]")]
    [InlineData("/Size 5")]
    [InlineData("/Size 5 /W [1 2]")]
    [InlineData("/Size 5 /W [1 9999999999 1]")]
    [InlineData("/Size 5 /W [0 0 0]")]
    [InlineData("/Size 5 /W [1 2 1] /Index [0 5 9]")]
    [InlineData("/Size 5 /W [1 2 1] /Index [0 -5]")]
    [InlineData("/Size 5 /W [1 2 1] /Index [0 2000000000]")]
    [InlineData("/Size 5 /W [1 2 1] /Prev (x)")]
    public void A_header_that_does_not_describe_the_data_is_a_reader_error(string entries)
    {
        Assert.Throws<PdfReaderException>(() => Open(File(entries)));
    }

    [Theory]
    [InlineData("/FlateDecmde")]
    [InlineData("/FlateDecode")]
    [InlineData("[/ASCIIHexDecode /RunLengthDecode]")]
    public void Data_that_cannot_be_decoded_is_a_reader_error(string filter)
    {
        Assert.Throws<PdfReaderException>(() => Open(File("/Size 5 /W [1 2 1]", filter: filter)));
    }

    [Fact]
    public void An_offset_far_beyond_the_end_of_the_file_is_a_reader_error()
    {
        // Building the error message dumped the bytes around the offset and overflowed for offsets outside the file.
        Assert.Throws<PdfReaderException>(() => Open(File("/Size 5 /W [1 2 1]", pageOffset: 0xFFF0)));
    }

    [Fact]
    public void Data_shorter_than_the_header_describes_is_a_reader_error()
    {
        Assert.Throws<PdfReaderException>(() => Open(File("/Size 5 /W [1 2 1]", keepEntries: 3)));
    }
}
