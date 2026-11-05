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
}
