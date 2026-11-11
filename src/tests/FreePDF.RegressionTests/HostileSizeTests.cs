// Small hostile files that expand into large data: XMP metadata that inflates to many megabytes, and form fields whose
// full names are very long. Found in a Codex review of 7.10.1. Original fixtures; MIT, see LICENSE.
using System.IO.Compression;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class HostileSizeTests
{
    // XMP that claims PDF/A-1b, padded with spaces (which XMP allows) to the given size.
    static byte[] Packet(string title, int size)
    {
        var start = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
                    + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
                    + "<rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
                    + "<pdfaid:part>1</pdfaid:part><pdfaid:conformance>B</pdfaid:conformance>"
                    + $"<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">{title}</rdf:li></rdf:Alt></dc:title>"
                    + "</rdf:Description></rdf:RDF></x:xmpmeta>\n";
        const string end = "<?xpacket end=\"w\"?>";
        var padding = Math.Max(0, size - start.Length - end.Length);
        return Encoding.ASCII.GetBytes(start + new string(' ', padding) + end);
    }

    // zlib (ISO 32000-1 7.4.4): a two-byte header, Deflate data and the Adler-32 checksum.
    static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0xDA);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
            deflate.Write(data, 0, data.Length);
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        var adler = (b << 16) | a;
        output.Write(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler }, 0, 4);
        return output.ToArray();
    }

    // A one-page file whose catalog's /Metadata stream holds data, written with the given /Filter.
    static byte[] WithMetadata(byte[] data, string filter)
    {
        var placeholder = new string('@', data.Length);
        var pdf = TestPdf.Create(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            $"<< /Type /Metadata /Subtype /XML /Filter {filter} /Length {data.Length} >>\nstream\n{placeholder}\nendstream",
            "/FlateDecode",   // object 5, for a filter given by reference
        });
        int at = TestPdf.Latin1.GetString(pdf).IndexOf("stream\n@", StringComparison.Ordinal) + "stream\n".Length;
        Array.Copy(data, 0, pdf, at, data.Length);
        return pdf;
    }

    // Opened in Modify mode with a new title and saved: the metadata stream of the saved file, as stored.
    static PdfDictionary SavedMetadata(byte[] pdf)
    {
        var output = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify))
        {
            document.Info.Title = "New title";
            document.Save(output);
        }
        var saved = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import);
        return saved.Catalog.Elements.GetDictionary("/Metadata")!;
    }

    // A small compressed packet that claims PDF/A is still read, and kept in agreement with the document information:
    // Flate, Flate named by an indirect object, and Flate behind ASCIIHex.
    [Theory]
    [InlineData("/FlateDecode")]
    [InlineData("5 0 R")]
    [InlineData("[/ASCIIHexDecode /FlateDecode]")]
    [InlineData("/FlateDecode /DecodeParms << /Predictor 1 >>")]
    public void Compressed_XMP_of_ordinary_size_is_updated(string filter)
    {
        var data = Zlib(Packet("Old title", 4096));
        if (filter.Contains("ASCIIHex"))
            data = Encoding.ASCII.GetBytes(string.Concat(data.Select(value => value.ToString("X2"))) + ">");

        var metadata = SavedMetadata(WithMetadata(data, filter));

        Assert.Contains("New title", Encoding.UTF8.GetString(metadata.Stream!.UnfilteredValue));
    }

    // A tiny Flate stream whose PNG predictor claims rows of 268,435,455 bytes: the decoder allocated two rows (512 MiB)
    // before it looked at the data, also for metadata read on save. Rows are now no longer than the data, also when
    // /Colors × /Columns overflows.
    [Theory]
    [InlineData(1, 268_435_455)]
    [InlineData(1_073_741_824, 1_073_741_825)]
    public void A_predictor_with_huge_rows_allocates_no_more_than_its_data(int colors, int columns)
    {
        using var document = new PdfDocument();
        var stream = new PdfDictionary(document);
        stream.CreateStream(Zlib(new byte[16]));
        stream.Elements["/Filter"] = new PdfName("/FlateDecode");
        var parameters = new PdfDictionary(document);
        parameters.Elements.SetInteger("/Predictor", 12);
        parameters.Elements.SetInteger("/Colors", colors);
        parameters.Elements.SetInteger("/Columns", columns);
        stream.Elements["/DecodeParms"] = parameters;
#if NET8_0_OR_GREATER
        long before = GC.GetAllocatedBytesForCurrentThread();
#endif

        Assert.Empty(stream.Stream!.UnfilteredValue);
#if NET8_0_OR_GREATER
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 16 * 1024 * 1024);
#endif
    }

    // A failing flatten in a field tree 90 levels deep whose every /T is the same 512 KiB string object: the message took
    // the field's full name (45 million characters) and cut it to 200 afterwards. It is now built only that far.
    [Fact]
    public void A_flatten_failure_does_not_build_a_huge_name()
    {
        const int levels = 90;
        int widget = levels + 4, name = levels + 5;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (/Missing 12 Tf 0 g) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [{widget} 0 R] >>",
        };
        for (int level = 0; level < levels; level++)
        {
            int number = level + 4;
            var parent = level == 0 ? "" : $" /Parent {number - 1} 0 R";
            objects.Add($"<< /T {name} 0 R{parent} /Kids [{number + 1} 0 R] >>");
        }
        objects.Add($"<< /Type /Annot /Subtype /Widget /FT /Tx /T (f) /V (value) /Parent {widget - 1} 0 R /Rect [10 10 100 30] /P 3 0 R >>");
        objects.Add($"({new string('n', 512 * 1024)})");
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Create(objects.ToArray())), PdfDocumentOpenMode.Modify);
#if NET8_0_OR_GREATER
        long before = GC.GetAllocatedBytesForCurrentThread();
#endif

        var refused = Assert.Throws<InvalidOperationException>(() => document.Flatten());

        Assert.Contains(new string('n', 200) + "…", refused.Message);
#if NET8_0_OR_GREATER
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 32 * 1024 * 1024);
#endif
    }

    // 32 MiB of XMP from about 32 KB of Flate, or from far less with Flate applied twice: the whole packet was inflated
    // while the file was saved (and opened in Append mode), so a few kilobytes could take gigabytes. It is now kept as it
    // is, unread.
    [Theory]
    [InlineData("/FlateDecode")]
    [InlineData("[/FlateDecode /FlateDecode]")]
    public void XMP_that_inflates_beyond_16_MiB_is_not_read(string filter)
    {
        var data = Zlib(Packet("Old title", 32 * 1024 * 1024));
        if (filter.StartsWith("[", StringComparison.Ordinal))
            data = Zlib(data);

        var metadata = SavedMetadata(WithMetadata(data, filter));

        Assert.Equal(data, metadata.Stream!.Value);
    }

    // An unsigned form with a field name of 5,000 characters opens and saves in Append mode. Opening in Append mode reads
    // the rules of the file's signatures, and that refused the long name although the file has no signature.
    [Fact]
    public void A_long_field_name_in_an_unsigned_file_does_not_stop_Append_mode()
    {
        var pdf = TestPdf.Create(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            $"<< /FT /Tx /T ({new string('n', 5_000)}) /V (x) >>",
        });

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Append);
        document.Info.Title = "Updated";
        document.Save(new MemoryStream());
    }

    // A form whose 200 text fields are kids of a field with a 20,000-character name, with values but no font to draw them:
    // the refusal named every field in full, so a few-megabyte file could build gigabytes of message text.
    [Fact]
    public void A_form_that_cannot_be_flattened_names_a_few_fields_briefly()
    {
        const int count = 200;
        var kids = string.Join(" ", Enumerable.Range(5, count).Select(n => $"{n} 0 R"));
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (/Missing 12 Tf 0 g) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [{kids}] >>",
            $"<< /T ({new string('n', 20_000)}) /Kids [{kids}] >>",
        };
        for (int index = 0; index < count; index++)
            objects.Add($"<< /Type /Annot /Subtype /Widget /FT /Tx /T (f{index}) /V (value) /Parent 4 0 R /Rect [10 10 100 30] /P 3 0 R >>");

        using var document = PdfReader.Open(new MemoryStream(TestPdf.Create(objects.ToArray())), PdfDocumentOpenMode.Modify);
        var refused = Assert.Throws<InvalidOperationException>(() => document.Flatten());

        Assert.Contains($"and {count - 10} more", refused.Message);
        Assert.True(refused.Message.Length < 5_000, $"{refused.Message.Length} characters");
    }
}
