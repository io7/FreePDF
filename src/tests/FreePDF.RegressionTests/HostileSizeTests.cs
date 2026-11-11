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

    public enum Layout { Separate, Shared, DictionaryThenString, SharedArrayBeforeString }

    // A file whose objects 5 to 68 are in one object stream. Shared: its header points all 64 at one 1 MiB string (a few
    // kilobytes of Flate), so reading them built 64 MiB of strings from a stream that holds 1 MiB; with more objects and a
    // longer string, gigabytes. Separate: each object has its own short string, as in any file. DictionaryThenString:
    // dictionaries and 256-byte strings in turn, as in any file.
    static byte[] ObjectStreamFile(Layout layout, int count = 64)
    {
        string header, objects;
        if (layout is Layout.Shared or Layout.SharedArrayBeforeString)
        {
            header = string.Join(" ", Enumerable.Range(5, count).Select(number => $"{number} 0")) + " ";
            objects = (layout == Layout.SharedArrayBeforeString ? "[] " : "") + "(" + new string('a', 1024 * 1024) + ")";
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

    // 4,096 objects that all point at one empty array, followed by a 1 MiB string: after an array the parser read the next
    // token whole (to see whether a stream follows, which cannot in an object stream), once for every object: 4 GiB of
    // scanning, and with more objects and a longer string hours. Nothing after an object in an object stream is read now.
    [Fact]
    public void Objects_that_point_at_one_array_do_not_read_what_follows_it()
    {
        const int count = 4096;
        var pdf = ObjectStreamFile(Layout.SharedArrayBeforeString, count);
        var started = System.Diagnostics.Stopwatch.StartNew();

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

        Assert.IsType<PdfArray>(document.Internals.GetObject(new PdfObjectID(4 + count)));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"took {started.Elapsed}");
    }

    // Objects 4 to 67 written inside one another's strings, each with its own right cross-reference entry, around one
    // 256 KiB text: each object's string holds all those inside it and the text, so reading them kept 64 copies. The
    // objects read from a file may together span at most twice the file.
    [Fact]
    public void Objects_nested_in_one_another_s_strings_are_refused()
    {
        const int count = 64;
        using var file = new MemoryStream();
        void Text(string text)
        {
            var bytes = TestPdf.Latin1.GetBytes(text);
            file.Write(bytes, 0, bytes.Length);
        }
        var offsets = new List<long>();
        Text("%PDF-1.7\n");
        foreach (var body in new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                     "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>" })
        {
            offsets.Add(file.Position);
            Text($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }
        for (int index = 0; index < count; index++)
        {
            offsets.Add(file.Position);
            Text($"{offsets.Count} 0 obj\n(");
        }
        Text(new string('a', 256 * 1024));
        for (int index = 0; index < count; index++)
            Text(")\nendobj\n");
        long xref = file.Position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Text($"{offset:D10} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(file.ToArray()), PdfDocumentOpenMode.Import));
    }

    // 64 cross-reference sections and trailers written inside one another's strings around one 256 KiB text, each
    // trailer's /Prev naming the section in its own string: each trailer read kept the text again. Trailers count against
    // the file's span budget too.
    [Fact]
    public void Trailers_nested_in_one_another_s_strings_are_refused()
    {
        const int levels = 64;
        const string placeholder = "@@@@@@@@@@";
        var nested = "(" + new string('a', 256 * 1024) + ")";
        for (int level = levels - 1; level >= 0; level--)
        {
            var prev = level == levels - 1 ? "" : $" /Prev {placeholder}";
            nested = $"(xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 4 /Root 1 0 R{prev} /Junk {nested} >>\n)";
        }
        var head = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
                   + "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n";
        var offsets = new List<int>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(head, @"(\d) 0 obj"))
            offsets.Add(match.Index);
        int xref = head.Length;
        var text = head + "xref\n0 4\n0000000000 65535 f \n" + string.Concat(offsets.Select(offset => $"{offset:D10} 00000 n \n"))
                   + $"trailer\n<< /Size 4 /Root 1 0 R /Prev {placeholder} /Junk {nested} >>\nstartxref\n{xref}\n%%EOF\n";
        // Each /Prev names the next nested section, whose 'xref' follows the placeholder's string.
        var sections = System.Text.RegularExpressions.Regex.Matches(text, @"\(xref\n").Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Index + 1).ToList();
        var builder = new StringBuilder(text);
        int at = 0;
        foreach (var section in sections)
        {
            at = builder.ToString(at, builder.Length - at).IndexOf(placeholder, StringComparison.Ordinal) + at;
            builder.Remove(at, placeholder.Length).Insert(at, section.ToString("D10"));
        }

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(builder.ToString())), PdfDocumentOpenMode.Import));
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
    static byte[] AliasedObjectsFile(bool xrefStream, int aliases = 64, int padding = 0, int length = 256 * 1024, bool successive = false)
    {
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
            "(" + new string('a', length) + ")",
        };
        var offsets = new long[5];
        Text("%PDF-1.7\n");
        for (int number = 1; number <= 4; number++)
        {
            offsets[number] = file.Position;
            // Object 4's number may be padded with zeros, which a number may have.
            Text($"{(number == 4 ? new string('0', padding) : "")}{number} 0 obj\n{bodies[number - 1]}\nendobj\n");
        }
        long xref = file.Position;
        if (!xrefStream)
        {
            Text($"xref\n0 5\n0000000000 65535 f \n");
            for (int number = 1; number <= 4; number++)
                Text($"{offsets[number]:D10} 00000 n \n");
            Text($"5 {aliases}\n");
            for (int index = 0; index < aliases; index++)
                Text($"{offsets[4] + (successive ? index : 0):D10} {index + 1:D5} n \n");
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
            Row(1, offsets[4] + (successive ? index : 0), 0);
        Row(1, xref, 0);
        var rowBytes = rows.ToArray();
        int xrefNumber = 5 + aliases;
        Text($"{xrefNumber} 0 obj\n<< /Type /XRef /Size {xrefNumber + 1} /W [1 4 2] /Root 1 0 R /Length {rowBytes.Length} >>\nstream\n");
        file.Write(rowBytes, 0, rowBytes.Length);
        Text($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return file.ToArray();
    }

    // 2,048 cross-reference entries that point at one object whose number is padded with 1 MiB of zeros: each entry read
    // the number at its position again before duplicates were dropped, 2 GiB of scanning (and, with a compressed
    // cross-reference stream, terabytes from a small file). The number at a position is now read once.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entries_that_point_at_one_padded_object_read_its_number_once(bool xrefStream)
    {
        var pdf = AliasedObjectsFile(xrefStream, aliases: 2048, padding: 1024 * 1024, length: 16);
        var started = System.Diagnostics.Stopwatch.StartNew();

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);

        Assert.Single(document.Internals.GetAllObjects().OfType<PdfStringObject>());
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"took {started.Elapsed}");
    }

    // The same 2,048 entries, each pointing one byte further into the zeros: no two read the same place, and each scanned
    // the rest of the megabyte. What the entries' headers span is now limited to twice the file.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entries_that_point_into_one_run_of_zeros_are_refused(bool xrefStream)
    {
        var pdf = AliasedObjectsFile(xrefStream, aliases: 2048, padding: 1024 * 1024, length: 16, successive: true);
        var started = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"took {started.Elapsed}");
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

    // One text field with a 256 KiB value and 512 widgets without appearances: flattening built an appearance with the
    // whole value for each widget, 128 MiB from a file of 256 KiB, and with more widgets gigabytes. It now refuses more
    // than 64 Mi characters of appearances, and leaves the document as it was.
    [Fact]
    public void A_flatten_that_would_multiply_one_value_is_refused()
    {
        const int widgets = 512;
        var kids = string.Join(" ", Enumerable.Range(5, widgets).Select(n => $"{n} 0 R"));
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (/Helv 12 Tf 0 g) /DR << /Font << /Helv {5 + widgets} 0 R >> >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [{kids}] >>",
            $"<< /FT /Tx /T (name) /V ({new string('a', 256 * 1024)}) /Kids [{kids}] >>",
        };
        for (int index = 0; index < widgets; index++)
            objects.Add("<< /Type /Annot /Subtype /Widget /Parent 4 0 R /Rect [10 10 100 30] /P 3 0 R >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Create(objects.ToArray())), PdfDocumentOpenMode.Modify);

        var refused = Assert.Throws<InvalidOperationException>(() => document.Flatten());

        Assert.Contains("characters of appearances", refused.Message);
        Assert.NotNull(document.Catalog.Elements["/AcroForm"]);
    }

    // A one-page form with one field of the given kind and widgets, the font /Helv, and the given default appearance.
    static byte[] FormFile(string field, int widgets, string da = "/Helv 12 Tf 0 g", string rect = "[10 10 100 30]", params string[] extra)
    {
        var kids = string.Join(" ", Enumerable.Range(5, widgets).Select(n => $"{n} 0 R"));
        int font = 5 + widgets;
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA ({da}) /DR << /Font << /Helv {font} 0 R >> >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [{kids}] >>",
            field.Replace("KIDS", kids),
        };
        for (int index = 0; index < widgets; index++)
            objects.Add($"<< /Type /Annot /Subtype /Widget /Parent 4 0 R /Rect {rect} /P 3 0 R >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.AddRange(extra);
        return TestPdf.Create(objects.ToArray());
    }

    // A list box whose /Opt names one 64 Ki-character option 1,100 times (one object): flattening joined all the options
    // into one string (and encoded it) before it counted them, about 70 Mi characters several times over. They are counted
    // first now, and the form is refused.
    [Fact]
    public void A_list_box_whose_options_repeat_one_long_text_is_counted_before_it_is_joined()
    {
        const int options = 1100;
        int option = 5 + 1 + 1;   // after the widget and the font
        var pdf = FormFile($"<< /FT /Ch /T (list) /Opt [{string.Join(" ", Enumerable.Repeat($"{option} 0 R", options))}] /V (x) /Kids [KIDS] >>",
            1, extra: [$"[(x) ({new string('a', 64 * 1024)})]"]);
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
#if NET8_0_OR_GREATER
        long before = GC.GetAllocatedBytesForCurrentThread();
#endif

        var refused = Assert.Throws<InvalidOperationException>(() => document.Flatten());

        Assert.Contains("characters of appearances", refused.Message);
#if NET8_0_OR_GREATER
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 32 * 1024 * 1024, "the options were joined");
#endif
    }

    // A default appearance whose colour operand is padded to 256 Ki characters: each appearance copied the operand, for
    // every widget, and a list box for every option it shows (512 widgets, or 2,048 options in one, made 128 MiB). The
    // operands are written again as plain numbers now, so the appearances stay small.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_long_default_appearance_operand_is_not_copied_into_appearances(bool listBox)
    {
        var da = $"/Helv 0.001 Tf {new string('0', 256 * 1024)} g";
        var pdf = listBox
            ? FormFile($"<< /FT /Ch /T (list) /Opt [{string.Concat(Enumerable.Repeat("(o) ", 2048))}] /V (o) /Kids [KIDS] >>", 1, da)
            : FormFile("<< /FT /Tx /T (name) /V (x) /Kids [KIDS] >>", 512, da);
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);

        document.Flatten();

        var xObjects = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        long total = xObjects.Elements.Values.Sum(item => ((item as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfDictionary)?.Stream?.Value.Length ?? 0);
        Assert.True(total < 8 * 1024 * 1024, $"{total} bytes of appearances");
    }

    // A multiline field whose 64 KiB value is short words, in a wide widget: wrapping measured the whole line again for
    // every word, time that grows with the square of the value. Each word is measured once now.
    [Fact]
    public void Wrapping_a_long_value_takes_time_in_proportion_to_it()
    {
        var value = string.Concat(Enumerable.Repeat("a ", 32 * 1024));
        var pdf = FormFile($"<< /FT /Tx /Ff 4096 /T (name) /V ({value}) /Kids [KIDS] >>", 1, rect: "[0 0 10000000 30]");
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        var started = System.Diagnostics.Stopwatch.StartNew();

        document.Flatten();

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3), $"took {started.Elapsed}");
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
