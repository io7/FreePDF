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

    // zlib of count zero bytes, written in pieces so that the zeros are never held at once.
    static byte[] ZlibOfZeros(long count)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0xDA);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
        {
            var zeros = new byte[1024 * 1024];
            for (long left = count; left > 0; left -= zeros.Length)
                deflate.Write(zeros, 0, (int)Math.Min(zeros.Length, left));
        }
        output.Write(new byte[4], 0, 4);   // the Adler-32 checksum, which no reader here checks
        return output.ToArray();
    }

    // A file whose cross-reference stream is Flate applied twice to 65 MiB of zeros, a few hundred bytes in all: the reader
    // inflated it whole while opening, before it could check anything, so a few such bytes could take gigabytes. Each
    // cross-reference or object stream now decodes to at most 64 MiB.
    [Fact]
    public void A_cross_reference_stream_that_inflates_too_far_is_refused()
    {
        var data = Zlib(ZlibOfZeros(65L * 1024 * 1024));
        var head = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
                   + "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n";
        int xref = head.Length;
        var text = head + $"4 0 obj\n<< /Type /XRef /Size 5 /W [1 4 2] /Root 1 0 R /Filter [/FlateDecode /FlateDecode] /Length {data.Length} >>\nstream\n"
                   + new string('@', data.Length) + $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n";
        var pdf = TestPdf.Latin1.GetBytes(text);
        Array.Copy(data, 0, pdf, text.IndexOf("stream\n@", StringComparison.Ordinal) + "stream\n".Length, data.Length);

        var refused = Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import));

        Assert.Contains("64", refused.Message + refused.InnerException?.Message);
    }

    public enum Layout { Separate, Shared, DictionaryThenString }

    // A file whose objects 5 to 68 are in one object stream. Shared: its header points all 64 at one 1 MiB string (a few
    // kilobytes of Flate), so reading them built 64 MiB of strings from a stream that holds 1 MiB; with more objects and a
    // longer string, gigabytes. Separate: each object has its own short string, as in any file. DictionaryThenString:
    // dictionaries and 256-byte strings in turn, as in any file.
    static byte[] ObjectStreamFile(Layout layout)
    {
        const int count = 64;
        string header, objects;
        if (layout == Layout.Shared)
        {
            header = string.Join(" ", Enumerable.Range(5, count).Select(number => $"{number} 0")) + " ";
            objects = "(" + new string('a', 1024 * 1024) + ")";
        }
        else
        {
            var bodies = Enumerable.Range(0, count)
                .Select(index => layout == Layout.Separate || index % 2 == 1 ? "(" + new string('a', layout == Layout.Separate ? 1 : 256) + ")" : "<< /A 1 >>")
                .ToList();
            var starts = new List<int>();
            int at = 0;
            foreach (var body in bodies)
            {
                starts.Add(at);
                at += body.Length + 1;
            }
            header = string.Join(" ", starts.Select((offset, index) => $"{index + 5} {offset}")) + " ";
            objects = string.Join(" ", bodies) + " ";
        }
        var stream = Zlib(Encoding.ASCII.GetBytes(header + objects));
        int xrefNumber = 5 + count;
        using var file = new MemoryStream();
        var offsets = new long[5];
        void Text(string text)
        {
            var bytes = TestPdf.Latin1.GetBytes(text);
            file.Write(bytes, 0, bytes.Length);
        }
        Text("%PDF-1.7\n");
        var plain = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        };
        for (int number = 1; number <= 3; number++)
        {
            offsets[number] = file.Position;
            Text($"{number} 0 obj\n{plain[number - 1]}\nendobj\n");
        }
        offsets[4] = file.Position;
        Text($"4 0 obj\n<< /Type /ObjStm /N {count} /First {header.Length} /Filter /FlateDecode /Length {stream.Length} >>\nstream\n");
        file.Write(stream, 0, stream.Length);
        Text("\nendstream\nendobj\n");
        long xref = file.Position;
        // Cross-reference stream rows with /W [1 4 2]: type, offset or object stream, generation or index.
        var rows = new MemoryStream();
        void Row(int type, long second, int third)
        {
            rows.WriteByte((byte)type);
            for (int shift = 24; shift >= 0; shift -= 8)
                rows.WriteByte((byte)(second >> shift));
            rows.WriteByte((byte)(third >> 8));
            rows.WriteByte((byte)third);
        }
        Row(0, 0, 65535);
        for (int number = 1; number <= 4; number++)
            Row(1, offsets[number], 0);
        for (int index = 0; index < count; index++)
            Row(2, 4, index);
        Row(1, xref, 0);
        var rowBytes = rows.ToArray();
        Text($"{xrefNumber} 0 obj\n<< /Type /XRef /Size {xrefNumber + 1} /W [1 4 2] /Root 1 0 R /Length {rowBytes.Length} >>\nstream\n");
        file.Write(rowBytes, 0, rowBytes.Length);
        Text($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return file.ToArray();
    }

    [Fact]
    public void Objects_that_share_their_bytes_in_an_object_stream_are_refused()
    {
        using (var ordinary = PdfReader.Open(new MemoryStream(ObjectStreamFile(Layout.Separate)), PdfDocumentOpenMode.Import))
            Assert.Equal("a", (ordinary.Internals.GetObject(new PdfObjectID(68)) as PdfStringObject)?.Value);

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(ObjectStreamFile(Layout.Shared)), PdfDocumentOpenMode.Import));
    }

    // Dictionaries followed by long strings: after a dictionary the parser reads the next token whole (to see whether a
    // stream follows), and that token was counted as part of the dictionary, so an ordinary object stream was refused.
    [Fact]
    public void A_dictionary_before_a_long_string_in_an_object_stream_opens()
    {
        using var document = PdfReader.Open(new MemoryStream(ObjectStreamFile(Layout.DictionaryThenString)), PdfDocumentOpenMode.Import);

        Assert.Equal(new string('a', 256), (document.Internals.GetObject(new PdfObjectID(68)) as PdfStringObject)?.Value);
    }

    // A file whose objects 5 to 68 are all the 256 KiB string of object 4, by their cross-reference entries: classic,
    // with a different generation each, or a stream. In a classic table the repair kept each entry's generation, so the
    // entries stayed apart and each read and kept the string: 64 copies from a file that holds one. (A stream's entries
    // were already read once.)
    static byte[] AliasedObjectsFile(bool xrefStream)
    {
        const int aliases = 64;
        using var file = new MemoryStream();
        void Text(string text)
        {
            var bytes = TestPdf.Latin1.GetBytes(text);
            file.Write(bytes, 0, bytes.Length);
        }
        var bodies = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            "(" + new string('a', 256 * 1024) + ")",
        };
        var offsets = new long[5];
        Text("%PDF-1.7\n");
        for (int number = 1; number <= 4; number++)
        {
            offsets[number] = file.Position;
            Text($"{number} 0 obj\n{bodies[number - 1]}\nendobj\n");
        }
        long xref = file.Position;
        if (!xrefStream)
        {
            Text($"xref\n0 5\n0000000000 65535 f \n");
            for (int number = 1; number <= 4; number++)
                Text($"{offsets[number]:D10} 00000 n \n");
            Text($"5 {aliases}\n");
            for (int index = 0; index < aliases; index++)
                Text($"{offsets[4]:D10} {index + 1:D5} n \n");
            Text($"trailer\n<< /Size {5 + aliases} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return file.ToArray();
        }
        var rows = new MemoryStream();
        void Row(int type, long second, int third)
        {
            rows.WriteByte((byte)type);
            for (int shift = 24; shift >= 0; shift -= 8)
                rows.WriteByte((byte)(second >> shift));
            rows.WriteByte((byte)(third >> 8));
            rows.WriteByte((byte)third);
        }
        Row(0, 0, 65535);
        for (int number = 1; number <= 4; number++)
            Row(1, offsets[number], 0);
        for (int index = 0; index < aliases; index++)
            Row(1, offsets[4], 0);
        Row(1, xref, 0);
        var rowBytes = rows.ToArray();
        int xrefNumber = 5 + aliases;
        Text($"{xrefNumber} 0 obj\n<< /Type /XRef /Size {xrefNumber + 1} /W [1 4 2] /Root 1 0 R /Length {rowBytes.Length} >>\nstream\n");
        file.Write(rowBytes, 0, rowBytes.Length);
        Text($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return file.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entries_that_point_at_one_object_read_it_once(bool xrefStream)
    {
        using var document = PdfReader.Open(new MemoryStream(AliasedObjectsFile(xrefStream)), PdfDocumentOpenMode.Import);

        Assert.Single(document.Internals.GetAllObjects().OfType<PdfStringObject>());
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
