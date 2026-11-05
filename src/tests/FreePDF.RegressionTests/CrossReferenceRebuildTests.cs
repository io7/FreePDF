// Files whose cross-reference information cannot be read: a wrong 'startxref', offsets that point elsewhere, a
// missing table. Viewers rebuild it from the objects; FreePDF refused such files (upstream PDFsharp PR #152 describes
// them). Original fixtures; MIT, see LICENSE.
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class CrossReferenceRebuildTests
{
    static readonly string[] Objects = {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>",
        "<< /Length 17 >>\nstream\n0 0 m 240 200 l S\nendstream",
    };

    static string Text(byte[] bytes) => TestPdf.Latin1.GetString(bytes);
    static byte[] Bytes(string text) => TestPdf.Latin1.GetBytes(text);

    static void AssertOpens(byte[] bytes, string? password = null)
    {
        using var document = password == null
            ? PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify)
            : PdfReader.Open(new MemoryStream(bytes), password, PdfDocumentOpenMode.Modify);
        Assert.Equal(1, document.PageCount);
        Assert.Equal(240, document.Pages[0].MediaBox.Width, 3);
        var content = document.Pages[0].Elements.GetDictionary("/Contents")!.Stream!.UnfilteredValue;
        Assert.Equal("0 0 m 240 200 l S", Encoding.ASCII.GetString(content));
        using var saved = new MemoryStream();
        document.Save(saved);
    }

    [Fact]
    public void A_wrong_startxref_is_rebuilt()
    {
        var text = Text(TestPdf.Create(Objects));
        text = Regex.Replace(text, @"startxref\s+\d+", "startxref\n9");
        AssertOpens(Bytes(text));
    }

    [Fact]
    public void A_missing_table_is_rebuilt_from_the_trailer_and_the_objects()
    {
        var text = Text(TestPdf.Create(Objects));
        var xref = text.IndexOf("xref\n", StringComparison.Ordinal);
        var trailer = text.IndexOf("trailer", StringComparison.Ordinal);
        text = text.Substring(0, xref) + text.Substring(trailer);
        AssertOpens(Bytes(text));
    }

    [Fact]
    public void Offsets_that_point_elsewhere_are_rebuilt()
    {
        // Every object moves seven bytes; the table still names the old offsets.
        var text = Text(TestPdf.Create(Objects));
        var first = text.IndexOf("1 0 obj", StringComparison.Ordinal);
        text = text.Substring(0, first) + "%moved\n" + text.Substring(first);
        AssertOpens(Bytes(text));
    }

    [Fact]
    public void The_newest_version_of_an_object_wins()
    {
        // An incremental update replaces the page with one 300 points wide; the table offsets are then broken.
        var original = Text(TestPdf.Create(Objects));
        var update = "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R >>\nendobj\n" +
                     "trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n99999\n%%EOF\n";
        using var document = PdfReader.Open(new MemoryStream(Bytes(original + update)), PdfDocumentOpenMode.Import);
        Assert.Equal(300, document.Pages[0].MediaBox.Width, 3);
    }

    [Fact]
    public void Objects_in_an_object_stream_are_rebuilt()
    {
        // Catalog 1 and page tree 2 are in object stream 5, page 3 and content 4 are file objects, and the
        // cross-reference stream 6 is never found, because 'startxref' is wrong.
        var pdf = new StringBuilder("%PDF-1.7\n");
        pdf.Append($"3 0 obj\n{Objects[2]}\nendobj\n");
        pdf.Append($"4 0 obj\n{Objects[3]}\nendobj\n");
        var members = $"{Objects[0]}\n{Objects[1]}\n";
        var header = $"1 0 2 {Objects[0].Length + 1} ";
        pdf.Append($"5 0 obj\n<< /Type /ObjStm /N 2 /First {header.Length} /Length {header.Length + members.Length} >>\nstream\n{header}{members}\nendstream\nendobj\n");
        pdf.Append("6 0 obj\n<< /Type /XRef /Size 7 /W [1 2 1] /Root 1 0 R /Length 0 >>\nstream\n\nendstream\nendobj\n");
        pdf.Append("startxref\n3\n%%EOF\n");

        AssertOpens(Encoding.ASCII.GetBytes(pdf.ToString()));
    }

    [Fact]
    public void An_encrypted_file_with_a_wrong_startxref_is_rebuilt()
    {
        byte[] encrypted;
        using (var document = PdfReader.Open(new MemoryStream(TestPdf.Create(Objects)), PdfDocumentOpenMode.Modify))
        {
            document.SecurityHandler.SetEncryptionToV4UsingAES();
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            document.Save(output);
            encrypted = output.ToArray();
        }
        var text = Regex.Replace(Text(encrypted), @"startxref\s+\d+", "startxref\n9");

        AssertOpens(Bytes(text), "owner");
    }

    [Fact]
    public void Append_mode_refuses_a_rebuilt_file()
    {
        var text = Regex.Replace(Text(TestPdf.Create(Objects)), @"startxref\s+\d+", "startxref\n9");

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(Bytes(text)), PdfDocumentOpenMode.Append));
    }

    [Fact]
    public void A_file_without_objects_is_still_an_error()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nnothing here\nstartxref\n9\n%%EOF\n");

        var error = Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import));
        Assert.Contains("Rebuilding", error.Message);
    }
}
