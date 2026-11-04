// Pages that inherit /Resources, /MediaBox, /CropBox or /Rotate from the page tree. Opening a document copies these
// values onto its pages, and an Append-mode update wrote them into every page it updated (adding a signature or a
// comment), so validators saw page keys change and reported earlier signatures as broken (pyHanko: "Dict keys
// differ"). Original fixtures; MIT, see LICENSE.
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class AppendInheritedPageValuesTests
{
    const string Inherited = "/MediaBox [0 0 400 300] /CropBox [10 10 390 290] /Rotate 90";

    // Page 3 inherits everything from the root; object 4 is its content, 5 a font, 6 shared resources.
    static byte[] Flat(string resources = "/Resources 6 0 R") => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        $"<< /Type /Pages /Kids [3 0 R] /Count 1 {Inherited} {resources} >>",
        "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>",
        "<< /Length 39 >>\nstream\nBT /F1 12 Tf 20 20 Td (Inherited) Tj ET\nendstream",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        "<< /Font << /F1 5 0 R >> >>",
    });

    // Page 4 inherits from the intermediate node 3, which inherits nothing from the root.
    static byte[] Nested() => TestPdf.Create(new[] {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Pages /Parent 2 0 R /Kids [4 0 R] /Count 1 {Inherited} /Resources 7 0 R >>",
        "<< /Type /Page /Parent 3 0 R /Contents 5 0 R >>",
        "<< /Length 39 >>\nstream\nBT /F1 12 Tf 20 20 Td (Inherited) Tj ET\nendstream",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        "<< /Font << /F1 6 0 R >> >>",
    });

    static byte[] Save(byte[] original, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // The text of an object as the update writes it, or null if the update does not write it.
    static string? WrittenObject(byte[] original, byte[] result, int number)
    {
        Assert.Equal(original, result.Take(original.Length).ToArray());
        var update = TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
        var match = Regex.Match(update, $@"(?ms)^{number} 0 obj\b(.*?)\bendobj");
        return match.Success ? match.Groups[1].Value : null;
    }

    static void AssertInheritedValues(PdfPage page)
    {
        Assert.Equal(400, page.MediaBox.Width, 3);
        Assert.Equal(380, page.CropBox.Width, 3);
        Assert.Equal(90, page.Rotate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_updated_page_is_written_without_the_values_it_inherits(bool nested)
    {
        var original = nested ? Nested() : Flat();
        var pageNumber = nested ? 4 : 3;

        var result = Save(original, document => document.Pages[0].Elements.SetName("/Marker", "/Changed"));

        var page = WrittenObject(original, result, pageNumber);
        Assert.NotNull(page);
        Assert.Contains("/Marker", page);
        foreach (var key in new[] { "/MediaBox", "/CropBox", "/Rotate", "/Resources" })
            Assert.DoesNotContain(key, page);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        AssertInheritedValues(reopened.Pages[0]);
        Assert.NotNull(reopened.Pages[0].Resources.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/F1"));
    }

    [Fact]
    public void A_page_size_set_by_the_application_is_written()
    {
        var original = Flat();

        var result = Save(original, document => document.Pages[0].MediaBox = new PdfRectangle(new XRect(0, 0, 500, 300)));

        var page = WrittenObject(original, result, 3);
        Assert.NotNull(page);
        Assert.Contains("/MediaBox", page);
        Assert.DoesNotContain("/CropBox", page);
        Assert.DoesNotContain("/Resources", page);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(500, reopened.Pages[0].MediaBox.Width, 3);
        Assert.Equal(90, reopened.Pages[0].Rotate);
    }

    [Fact]
    public void Drawing_with_shared_inherited_resources_changes_the_shared_resources()
    {
        var original = Flat();

        var result = Save(original, document => {
            using var gfx = XGraphics.FromPdfPage(document.Pages[0]);
            gfx.DrawRectangle(XBrushes.Red, 50, 50, 40, 40);
        });

        var page = WrittenObject(original, result, 3);
        Assert.NotNull(page);
        Assert.Contains("/Contents", page);
        Assert.DoesNotContain("/Resources", page);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        AssertInheritedValues(reopened.Pages[0]);
        var resources = reopened.Pages[0].Resources;
        Assert.NotNull(resources.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/F1"));
        Assert.NotNull(resources.Elements.GetDictionary("/ExtGState"));
    }

    [Fact]
    public void Drawing_with_direct_inherited_resources_works_in_Modify_mode()
    {
        // The copied resources had no document, so adding a graphics state threw NullReferenceException.
        using var document = PdfReader.Open(new MemoryStream(Flat("/Resources << /Font << /F1 5 0 R >> >>")), PdfDocumentOpenMode.Modify);
        using (var gfx = XGraphics.FromPdfPage(document.Pages[0]))
            gfx.DrawRectangle(XBrushes.Red, 50, 50, 40, 40);

        Assert.NotNull(document.Pages[0].Resources.Elements.GetDictionary("/ExtGState"));
        Assert.NotNull(document.Pages[0].Resources.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/F1"));
    }

    [Fact]
    public void Drawing_with_direct_inherited_resources_gives_the_page_its_own()
    {
        var original = Flat("/Resources << /Font << /F1 5 0 R >> >>");

        var result = Save(original, document => {
            using var gfx = XGraphics.FromPdfPage(document.Pages[0]);
            gfx.DrawRectangle(XBrushes.Red, 50, 50, 40, 40);
        });

        var page = WrittenObject(original, result, 3);
        Assert.NotNull(page);
        Assert.Contains("/Resources", page);
        Assert.DoesNotContain("/MediaBox", page);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        AssertInheritedValues(reopened.Pages[0]);
        var resources = reopened.Pages[0].Resources;
        Assert.NotNull(resources.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/F1"));
        Assert.NotNull(resources.Elements.GetDictionary("/ExtGState"));
    }

    [Fact]
    public void A_page_with_its_own_values_keeps_them()
    {
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [3 0 R] /Count 1 {Inherited} >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] >>",
        });

        var result = Save(original, document => document.Pages[0].Elements.SetName("/Marker", "/Changed"));

        var page = WrittenObject(original, result, 3);
        Assert.NotNull(page);
        Assert.Contains("/MediaBox[0 0 200 100]", page);
        Assert.DoesNotContain("/CropBox", page);
        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(200, reopened.Pages[0].MediaBox.Width, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saving_without_changes_still_keeps_the_bytes(bool nested)
    {
        var original = nested ? Nested() : Flat();
        Assert.Equal(original, Save(original, _ => { }));
    }
}
