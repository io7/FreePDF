// An incremental update may give a new object the number of an older revision's cross-reference stream (ISO 32000-1
// 7.5.6). FreePDF let the older cross-reference stream replace the newer object when it read the /Prev chain, so a
// form saved that way vanished (upstream PDFsharp issue #213). Original fixture, built byte by byte; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class ReusedXRefStreamNumberTests
{
    // Revision 1: catalog 1, pages 2, page 3, and its cross-reference stream as object 4.
    // Revision 2: a new catalog 1 with /AcroForm 4 0 R, the form as object 4, a text field 5, and the revision's own
    // cross-reference stream 6 with /Prev pointing at revision 1's.
    static byte[] File()
    {
        var pdf = new MemoryStream();
        var offsets = new Dictionary<int, long>();
        void Write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));
        void Object(int number, string body)
        {
            offsets[number] = pdf.Position;
            Write($"{number} 0 obj\n{body}\nendobj\n");
        }
        // A cross-reference stream with /W [1 4 2] and no filter: type 1 entries for the listed objects.
        long XRefStream(int number, int size, (int First, int[] Numbers)[] sections, string extra)
        {
            var data = new MemoryStream();
            foreach (var (_, numbers) in sections)
            {
                foreach (var entry in numbers)
                {
                    long offset = entry == number ? pdf.Position : offsets[entry];
                    data.WriteByte(1);
                    data.Write(new[] { (byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset });
                    data.Write(new byte[] { 0, 0 });
                }
            }
            var index = string.Join(" ", sections.Select(section => $"{section.First} {section.Numbers.Length}"));
            var start = pdf.Position;
            offsets[number] = start;
            Write($"{number} 0 obj\n<< /Type /XRef /Size {size} /W [1 4 2] /Index [{index}] /Root 1 0 R {extra} /Length {data.Length} >>\nstream\n");
            pdf.Write(data.ToArray());
            Write("\nendstream\nendobj\n");
            return start;
        }

        Write("%PDF-1.7\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>");
        var first = XRefStream(4, 5, new[] { (1, new[] { 1, 2, 3, 4 }) }, "");
        Write($"startxref\n{first}\n%%EOF\n");

        Object(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>");
        Object(4, "<< /Fields [5 0 R] /DA (/Helv 0 Tf 0 g) >>");
        Object(5, "<< /FT /Tx /T (name) /V (Ada) /Type /Annot /Subtype /Widget /Rect [20 150 220 170] >>");
        var second = XRefStream(6, 7, new[] { (1, new[] { 1 }), (4, new[] { 4, 5, 6 }) }, $"/Prev {first}");
        Write($"startxref\n{second}\n%%EOF\n");
        return pdf.ToArray();
    }

    [Theory]
    [InlineData(PdfDocumentOpenMode.Import)]
    [InlineData(PdfDocumentOpenMode.Modify)]
    [InlineData(PdfDocumentOpenMode.Append)]
    public void The_newer_object_keeps_the_number(PdfDocumentOpenMode mode)
    {
        using var document = PdfReader.Open(new MemoryStream(File()), mode);

        Assert.True(document.Catalog.HasAcroForm);
        var field = document.Catalog.GetAcroForm()!.Fields["name"];
        Assert.NotNull(field);
        Assert.Equal("Ada", field!.Elements.GetString("/V"));
    }

    [Fact]
    public void The_form_survives_a_save()
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(File()), PdfDocumentOpenMode.Modify))
            document.Save(saved);

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.True(reopened.Catalog.HasAcroForm);
        Assert.NotNull(reopened.Catalog.GetAcroForm()!.Fields["name"]);
    }
}
