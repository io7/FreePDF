// Damaged-file cases that the first versions of the stream-end search and the cross-reference rebuild got wrong:
// objects the table does not list, linearized trailers, names containing 'stream', embedded PDF data, a stale /Root,
// huge object numbers, and large files. Original fixtures; MIT, see LICENSE.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class DamagedFileRecoveryTests
{
    const string Content = "0 0 m 240 200 l S";

    // Writes objects and cross-reference sections; each section lists the objects written since the previous one.
    sealed class Builder
    {
        readonly StringBuilder _text = new("%PDF-1.7\n");
        readonly Dictionary<int, int> _offsets = new();
        readonly List<int> _pending = new();
        bool _first = true;

        public void Obj(int number, string body)
        {
            _offsets[number] = _text.Length;
            _pending.Add(number);
            _text.Append($"{number} 0 obj\n{body}\nendobj\n");
        }

        public void Raw(string text) => _text.Append(text);

        // An object without 'endobj', which readers tolerate.
        public void ObjWithoutEndObj(int number, string body)
        {
            _offsets[number] = _text.Length;
            _pending.Add(number);
            _text.Append($"{number} 0 obj\n{body}\n");
        }

        public int Xref(string trailer, int? previous = null)
        {
            int at = _text.Length;
            _text.Append("xref\n");
            if (_first)
                _text.Append("0 1\n0000000000 65535 f \n");
            _first = false;
            foreach (var number in _pending.Distinct().OrderBy(number => number))
                _text.Append($"{number} 1\n{_offsets[number]:D10} 00000 n \n");
            _pending.Clear();
            var prev = previous is { } p ? $" /Prev {p}" : "";
            _text.Append($"trailer\n<< {trailer}{prev} >>\nstartxref\n{at}\n%%EOF\n");
            return at;
        }

        public string Text => _text.ToString();
        public byte[] Bytes => TestPdf.Latin1.GetBytes(Text);
    }

    static string PageContent(PdfDocument document)
        => Encoding.ASCII.GetString(document.Pages[0].Elements.GetDictionary("/Contents")!.Stream!.UnfilteredValue);

    static byte[] WrongStartXRef(string text) => TestPdf.Latin1.GetBytes(Regex.Replace(text, @"startxref\s+\d+\s+%%EOF\s*$", "startxref\n9\n%%EOF\n"));

    static void AssertSimplePage(byte[] bytes, double width = 240)
    {
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        Assert.Equal(1, document.PageCount);
        Assert.Equal(width, document.Pages[0].MediaBox.Width, 3);
        Assert.Equal(Content, PageContent(document));
        using var saved = new MemoryStream();
        document.Save(saved);
    }

    static void PageObjects(Builder builder, string extraPage = "")
    {
        builder.Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Obj(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R {extraPage}>>");
    }

    [Fact]
    public void A_short_length_does_not_swallow_an_object_an_update_replaced()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 15 >>\nstream\n{Content}\nendstream");
        builder.Obj(5, "<< /Length 5 >>\nstream\nhello\nendstream");
        var first = builder.Xref("/Size 6 /Root 1 0 R");
        builder.Obj(5, "(replaced)");
        builder.Xref("/Size 6 /Root 1 0 R", first);

        AssertSimplePage(builder.Bytes);
    }

    [Fact]
    public void A_padded_stream_keeps_its_length_before_an_object_an_update_replaced()
    {
        var builder = new Builder();
        builder.Obj(1, "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>");
        builder.Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>");
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\n\nendstream");
        builder.Obj(5, "<< /Type /Metadata /Subtype /XML /Length 10 >>\nstream\n<x:xmpold>\nendstream");
        var first = builder.Xref("/Size 6 /Root 1 0 R");
        builder.Obj(5, "<< /Type /Metadata /Subtype /XML /Length 10 >>\nstream\n<x:xmpnew>\nendstream");
        builder.Xref("/Size 6 /Root 1 0 R", first);

        using var document = PdfReader.Open(new MemoryStream(builder.Bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(Content, PageContent(document));
        var metadata = document.Catalog.Elements.GetDictionary("/Metadata")!.Stream!.UnfilteredValue;
        Assert.Equal("<x:xmpnew>", Encoding.ASCII.GetString(metadata));
    }

    [Fact]
    public void A_rebuild_takes_encryption_and_information_from_a_linearized_first_page_trailer()
    {
        byte[] encrypted;
        using (var source = new PdfDocument())
        {
            source.Info.Title = "First-page trailer";
            source.AddPage().Width = 240;
            source.SecurityHandler.SetEncryptionToV4UsingAES();
            source.SecuritySettings.UserPassword = "user";
            source.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            source.Save(output);
            encrypted = output.ToArray();
        }
        // Move the trailer's entries to a first-page trailer at the start, as linearized files have it; the last
        // trailer keeps only /Size, and 'startxref' is wrong, so the reader has to rebuild.
        var text = TestPdf.Latin1.GetString(encrypted);
        int trailer = text.LastIndexOf("trailer", StringComparison.Ordinal);
        int startXRef = text.IndexOf("startxref", trailer, StringComparison.Ordinal);
        var dictionary = text.Substring(trailer + 7, startXRef - trailer - 7).Trim();
        var size = Regex.Match(dictionary, @"/Size\s*(\d+)").Groups[1].Value;
        int header = text.IndexOf('\n') + 1;
        var damaged = text.Substring(0, header)
                      + $"xref\n0 1\n0000000000 65535 f \ntrailer\n{dictionary}\nstartxref\n0\n%%EOF\n"
                      + text.Substring(header, trailer - header)
                      + $"trailer\n<< /Size {size} >>\nstartxref\n99999\n%%EOF\n";
        var bytes = TestPdf.Latin1.GetBytes(damaged);

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import));
        using var document = PdfReader.Open(new MemoryStream(bytes), "owner", PdfDocumentOpenMode.Import);
        Assert.Equal("First-page trailer", document.Info.Title);
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void A_name_containing_stream_is_not_taken_for_stream_data()
    {
        var builder = new Builder();
        builder.Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Obj(5, "<< /Type /Font /Subtype /Type1 /BaseFont /BitstreamVeraSans-Roman >>");
        builder.Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Xref("/Size 6 /Root 1 0 R");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Fact]
    public void Objects_of_an_embedded_PDF_do_not_replace_the_file_s_own()
    {
        var inner = new Builder();
        inner.Obj(4, "<< /Length 5 >>\nstream\nINNER\nendstream");
        inner.Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        inner.Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        inner.Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 999 999] /Contents 4 0 R >>");
        inner.Xref("/Size 5 /Root 1 0 R");
        var embedded = inner.Text;

        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Obj(6, $"<< /Type /EmbeddedFile /Length {embedded.Length} >>\nstream\n{embedded}\nendstream");
        builder.Xref("/Size 7 /Root 1 0 R");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Fact]
    public void A_root_that_names_a_missing_object_falls_back_to_the_catalog()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Xref("/Size 5 /Root 9 0 R");

        AssertSimplePage(TestPdf.Latin1.GetBytes(Regex.Replace(builder.Text, @"startxref\s+\d+", "startxref\n99999")));
    }

    [Fact]
    public void An_object_number_beyond_the_PDF_limit_is_ignored_by_a_rebuild()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Raw("2147483647 0 obj\n<< /Huge true >>\nendobj\n");
        builder.Xref("/Size 5 /Root 1 0 R");

        // Registered, it made the rebuilt trailer's /Size negative and left no number for a new object. Import mode
        // keeps unreachable objects, so it would show there.
        var bytes = WrongStartXRef(builder.Text);
        using (var imported = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import))
            Assert.Null(imported.Internals.GetObject(new PdfObjectID(int.MaxValue)));
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        Assert.Equal(1, document.PageCount);
        document.AddPage();
        using var saved = new MemoryStream();
        document.Save(saved);
        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(2, reopened.PageCount);
    }

    [Fact]
    public void A_rebuild_of_many_objects_takes_linear_time()
    {
        // 40,000 objects took 36 seconds when every object was registered by sorting the whole table.
        var text = new StringBuilder("%PDF-1.7\n");
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        text.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        text.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>\nendobj\n");
        for (int index = 0; index < 40000; index++)
            text.Append(index % 2 == 0 ? $"{10 + index} 0 obj\n<< /A {index} >>\nendobj\n" : $"{10 + index} 0 obj\n<< /Length 1 >>\nstream\nx\nendstream\nendobj\n");
        text.Append("trailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");

        var watch = Stopwatch.StartNew();
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(text.ToString())), PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"Rebuild took {watch.Elapsed}.");
    }

    [Fact]
    public void Object_header_and_endobj_text_inside_strings_are_not_taken_for_structure()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Note (endobj) /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Obj(5, "<< /Type /Annot /Contents (See 2 0 obj in the spec, then endobj) >>");
        builder.Xref("/Size 6 /Root 1 0 R");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Fact]
    public void A_trailer_inside_a_string_does_not_make_a_rebuilt_file_encrypted()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Obj(5, "<< /Contents (trailer << /Encrypt 3 0 R >>) >>");
        builder.Xref("/Size 6 /Root 1 0 R");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Fact]
    public void A_rebuild_takes_all_trailer_entries_from_the_newest_file_revision()
    {
        // An encrypted file with a plain file appended: the plain one is newest, and none of the encrypted file's
        // entries may leak into it.
        byte[] encrypted;
        using (var source = new PdfDocument())
        {
            source.AddPage().Width = 100;
            source.SecurityHandler.SetEncryptionToV4UsingAES();
            source.SecuritySettings.UserPassword = "user";
            using var output = new MemoryStream();
            source.Save(output);
            encrypted = output.ToArray();
        }
        var plain = new Builder();
        PageObjects(plain);
        plain.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        plain.Xref("/Size 5 /Root 1 0 R");

        AssertSimplePage(encrypted.Concat(plain.Bytes).ToArray());
    }

    [Fact]
    public void Trailer_text_repeated_in_a_string_takes_linear_time()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Obj(5, $"<< /Note ({string.Concat(Enumerable.Repeat("trailer", 40000))}) >>");
        builder.Xref("/Size 6 /Root 1 0 R");

        var watch = Stopwatch.StartNew();
        AssertSimplePage(WrongStartXRef(builder.Text));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Rebuild took {watch.Elapsed}.");
    }

    [Theory]
    [InlineData("/Length 7 0 R", "stream\n")]
    [InlineData("/Length {0}", "stream \n")]
    public void An_embedded_PDF_with_an_indirect_length_or_a_space_after_stream_stays_data(string length, string keyword)
    {
        var inner = new Builder();
        inner.Obj(4, "<< /Length 5 >>\nstream\nINNER\nendstream");
        inner.Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        inner.Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        inner.Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 999 999] /Contents 4 0 R >>");
        inner.Xref("/Size 5 /Root 1 0 R");
        var embedded = inner.Text;

        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Obj(6, $"<< /Type /EmbeddedFile {string.Format(length, embedded.Length)} >>\n{keyword}{embedded}\nendstream");
        builder.Obj(7, embedded.Length.ToString());
        builder.Xref("/Size 8 /Root 1 0 R");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Fact]
    public void A_short_length_without_endobj_does_not_swallow_an_unlisted_object()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.ObjWithoutEndObj(4, $"<< /Length 15 >>\nstream\n{Content}\nendstream");
        builder.Raw("9 0 obj\n<< /Length 5 >>\nstream\nhello\nendstream\nendobj\n");
        builder.Xref("/Size 5 /Root 1 0 R");

        AssertSimplePage(builder.Bytes);
    }

    [Fact]
    public void A_generation_beyond_the_PDF_limit_is_ignored_by_a_rebuild()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Raw("3 99999 obj\nnull\nendobj\n");
        builder.Xref("/Size 5 /Root 1 0 R");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Theory]
    [InlineData("endstream")]
    [InlineData(">>")]
    [InlineData("]")]
    public void A_header_on_the_line_of_an_object_without_endobj_is_found_by_a_rebuild(string before)
    {
        // An object lacks 'endobj', and object 3 starts on the same line.
        var page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>";
        var text = $"%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" + before switch
        {
            "endstream" => $"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /Length 1 >>\nstream\nx\nendstream 3 0 obj\n{page}\nendobj\n",
            ">>" => $"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >> 3 0 obj\n{page}\nendobj\n",
            _ => $"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n5 0 obj\n[1 2] 3 0 obj\n{page}\nendobj\n",
        };
        text += $"4 0 obj\n<< /Length 17 >>\nstream\n{Content}\nendstream\nendobj\ntrailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n";

        AssertSimplePage(TestPdf.Latin1.GetBytes(text));
    }

    [Theory]
    [InlineData("xrefs")]
    [InlineData("trailers")]
    [InlineData("startxrefs")]
    public void Keywords_inside_stream_data_must_be_whole_to_close_the_stream(string word)
    {
        var data = $"BT ( endstream {word} ) Tj ET {Content}";
        var builder = new Builder();
        PageObjects(builder);
        // The declared length ends right before the fake 'endstream', so it is the nearer candidate.
        builder.Obj(4, $"<< /Length {data.IndexOf("endstream", StringComparison.Ordinal) - 2} >>\nstream\n{data}\nendstream");
        builder.Xref("/Size 5 /Root 1 0 R");

        using var document = PdfReader.Open(new MemoryStream(builder.Bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(data, PageContent(document));
    }

    [Fact]
    public void Without_a_trailer_naming_a_root_a_rebuild_keeps_the_encryption()
    {
        byte[] encrypted;
        using (var source = new PdfDocument())
        {
            source.Info.Title = "Renamed root";
            source.AddPage().Width = 240;
            source.SecurityHandler.SetEncryptionToV4UsingAES();
            source.SecuritySettings.UserPassword = "user";
            source.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            source.Save(output);
            encrypted = output.ToArray();
        }
        var text = TestPdf.Latin1.GetString(encrypted);
        int trailer = text.LastIndexOf("trailer", StringComparison.Ordinal);
        text = text.Substring(0, trailer) + text.Substring(trailer).Replace("/Root", "/Rxxx");
        var bytes = TestPdf.Latin1.GetBytes(Regex.Replace(text, @"startxref\s+\d+", "startxref\n9"));

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import));
        using var document = PdfReader.Open(new MemoryStream(bytes), "owner", PdfDocumentOpenMode.Import);
        Assert.Equal("Renamed root", document.Info.Title);
    }

    [Fact]
    public void Junk_trailer_keywords_after_the_trailer_take_linear_time()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Xref("/Size 5 /Root 1 0 R");
        var text = Regex.Replace(builder.Text, @"startxref\s+\d+\s+%%EOF\s*$", "")
                   + string.Concat(Enumerable.Repeat(" trailer << /K (", 20000)) + "\nstartxref\n9\n%%EOF\n";

        var watch = Stopwatch.StartNew();
        AssertSimplePage(TestPdf.Latin1.GetBytes(text));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Rebuild took {watch.Elapsed}.");
    }

    [Fact]
    public void An_indirect_length_is_tried_with_every_value_its_number_had()
    {
        // The length object 7 comes first; the embedded file has an integer object 7 too.
        var inner = new Builder();
        inner.Obj(7, "3");
        inner.Obj(4, "<< /Length 7 0 R >>\nstream\nINN\nendstream");
        inner.Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        inner.Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        inner.Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 999 999] /Contents 4 0 R >>");
        inner.Xref("/Size 8 /Root 1 0 R");
        var embedded = inner.Text;

        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(7, embedded.Length.ToString());
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Obj(6, $"<< /Type /EmbeddedFile /Length 7 0 R >>\nstream\n{embedded}\nendstream");
        builder.Xref("/Size 8 /Root 1 0 R");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Theory]
    [InlineData("/Note (see trailer << x >>)")]
    [InlineData("% old trailer << /Size 1 >>\n")]
    public void A_trailer_holding_trailer_text_in_a_string_or_comment_is_used(string extra)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            "<< /Title (real title) >>",
        }, _ => "/Info 4 0 R " + extra);
        var text = Regex.Replace(TestPdf.Latin1.GetString(bytes), @"startxref\s+\d+", "startxref\n9");

        using var document = PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(text)), PdfDocumentOpenMode.Import);
        Assert.Equal("real title", document.Info.Title);
    }

    [Fact]
    public void Objects_on_one_line_without_endobj_take_linear_time()
    {
        // 8,000 such objects took 29 seconds when the next header at a line start was searched for on every object.
        var text = new StringBuilder("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> 2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> ");
        text.Append("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>");
        for (int index = 0; index < 8000; index++)
            text.Append($" {10 + index} 0 obj << /A {index} >>");
        text.Append("\ntrailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n");

        var watch = Stopwatch.StartNew();
        using var document = PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(text.ToString())), PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Rebuild took {watch.Elapsed}.");
    }

    [Fact]
    public void A_header_behind_an_endstream_found_by_searching_is_not_an_object()
    {
        // /Length is wrong, so the search finds the 'endstream' inside the string first; '3 0 obj' behind it is data.
        var data = $"BT (endstream 3 0 obj) Tj ET {Content}";
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 5 >>\nstream\n{data}\nendstream");
        builder.Xref("/Size 5 /Root 1 0 R");

        using var document = PdfReader.Open(new MemoryStream(WrongStartXRef(builder.Text)), PdfDocumentOpenMode.Import);
        Assert.Equal(240, document.Pages[0].MediaBox.Width, 3);
    }

    [Fact]
    public void Without_a_root_the_encryption_comes_from_the_newest_trailer_that_has_it()
    {
        byte[] encrypted;
        using (var source = new PdfDocument())
        {
            source.Info.Title = "Size-only update";
            source.AddPage().Width = 240;
            source.SecurityHandler.SetEncryptionToV4UsingAES();
            source.SecuritySettings.UserPassword = "user";
            source.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            source.Save(output);
            encrypted = output.ToArray();
        }
        var text = TestPdf.Latin1.GetString(encrypted);
        int trailer = text.LastIndexOf("trailer", StringComparison.Ordinal);
        text = text.Substring(0, trailer) + text.Substring(trailer).Replace("/Root", "/Rxxx");
        text += "xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 9 >>\nstartxref\n9\n%%EOF\n";

        using var document = PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(text)), "owner", PdfDocumentOpenMode.Import);
        Assert.Equal("Size-only update", document.Info.Title);
    }

    static byte[] TrailerFile(string beforeTrailer, string afterEof, string extra = "")
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Obj(6, "<< /Title (real title) >>");
        builder.Raw(beforeTrailer);
        builder.Xref("/Size 7 /Root 1 0 R /Info 6 0 R" + extra);
        return TestPdf.Latin1.GetBytes(Regex.Replace(builder.Text, @"startxref\s+\d+", "startxref\n9") + afterEof);
    }

    [Theory]
    [InlineData("trailer << ) (\n")]
    [InlineData("% trailer <<\n")]
    public void Junk_trailer_strings_and_comments_take_linear_time(string unit)
    {
        // 16,000 such lines took 7 seconds when each string or comment could extend the bound again.
        var bytes = TrailerFile("", "trailer << (\n" + string.Concat(Enumerable.Repeat(unit, 24000)));

        var watch = Stopwatch.StartNew();
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal("real title", document.Info.Title);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Rebuild took {watch.Elapsed}.");
    }

    [Fact]
    public void A_broken_older_trailer_whose_string_runs_over_the_real_one_does_not_hide_it()
    {
        var bytes = TrailerFile("trailer\n<< /Size 3 /Info (abc\n", "\n) >>\n");

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal("real title", document.Info.Title);
    }

    [Fact]
    public void A_trailer_info_that_is_not_a_dictionary_is_dropped()
    {
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Xref("/Size 5 /Root 1 0 R /Info (not a dictionary)");

        using var document = PdfReader.Open(new MemoryStream(WrongStartXRef(builder.Text)), PdfDocumentOpenMode.Import);
        Assert.Equal("", document.Info.Title);
    }

    [Fact]
    public void A_dictionary_without_size_in_a_comment_is_no_fallback_trailer()
    {
        // No dictionary names a /Root; a comment's dictionary must not make the file encrypted.
        var builder = new Builder();
        PageObjects(builder);
        builder.Obj(4, $"<< /Length 17 >>\nstream\n{Content}\nendstream");
        builder.Raw("% trailer << /Encrypt 4 0 R >>\n");
        builder.Xref("/Size 5");

        AssertSimplePage(WrongStartXRef(builder.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Length 18 ")]
    public void A_header_behind_an_endstream_found_by_searching_counts_when_no_other_copy_exists(string length)
    {
        // The content stream's length is missing or wrong, and the page follows right behind its 'endstream'.
        var text = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
                   + $"4 0 obj\n<< {length}>>\nstream\n{Content}\nendstream 3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>\nendobj\n"
                   + "trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n9\n%%EOF\n";

        using var document = PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(text)), PdfDocumentOpenMode.Import);
        Assert.Equal(240, document.Pages[0].MediaBox.Width, 3);
        Assert.StartsWith(Content, PageContent(document));
    }

    [Fact]
    public void A_newer_copy_behind_an_endstream_found_by_searching_wins_over_an_older_broken_one()
    {
        // Object 6 first appears without a body; its real copy follows a stream whose /Length is wrong.
        var text = "%PDF-1.7\n3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>\n\n6 0 obj \n"
                   + "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
                   + $"4 0 obj\n<< /Length 15 >> stream\n{Content}\nendstream\nendobj\n"
                   + "6 0 obj << /Title (t) >> 1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
                   + "trailer\n<< /Size 10 /Root 1 0 R /Info 6 0 R >>\nstartxref\n9\n%%EOF\n";

        using var document = PdfReader.Open(new MemoryStream(TestPdf.Latin1.GetBytes(text)), PdfDocumentOpenMode.Import);
        Assert.Equal("t", document.Info.Title);
        Assert.Equal(1, document.PageCount);
    }
}
