// Upstream PDFsharp #295: PDF/A-1b files with text outside WinAnsi embed a CID font subset, which needs a CIDSet; veraPDF
// refused such files (rule 6.3.5-3). Original fixtures; MIT, see LICENSE.
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.PdfA;
using Xunit;

namespace FreePDF.RegressionTests;

public class PdfACidSetTests
{
    static PdfDictionary SaveAndGetCidFont(PdfAFormat? format)
    {
        TestFonts.Initialize();
        byte[] bytes;
        using (var document = new PdfDocument())
        {
            if (format is { } pdfa)
                document.SetPdfA(pdfa);
            var font = new XFont("FreePDF Synthetic", 12, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("šššččč ABC", font, XBrushes.Black, 50, 100);
            using var output = new MemoryStream();
            document.Save(output);
            bytes = output.ToArray();
        }
        var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        var fonts = reopened.Pages[0].Resources.Elements.GetDictionary("/Font")!;
        var type0 = fonts.Elements.Values.Select(item => (item as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfDictionary)
            .Single(font => font!.Elements.GetName("/Subtype") == "/Type0")!;
        return ((PdfArray)type0.Elements.GetValue("/DescendantFonts")!).Elements.GetDictionary(0)!;
    }

    [Fact]
    public void A_PDF_A_1_CID_font_lists_its_glyphs_in_a_CIDSet()
    {
        var cidFont = SaveAndGetCidFont(PdfAFormats.PdfA_1b);
        var descriptor = cidFont.Elements.GetDictionary("/FontDescriptor")!;
        var bits = descriptor.Elements.GetDictionary("/CIDSet")!.Stream!.UnfilteredValue;
        var listed = Enumerable.Range(0, bits.Length * 8).Where(cid => (bits[cid >> 3] & (0x80 >> (cid & 7))) != 0).ToHashSet();

        // Every glyph the text uses has a width in /W, written as "glyph [width]".
        var widths = cidFont.Elements["/W"]!.ToString()!;
        var used = Regex.Matches(widths, @"(\d+)\s*\[").Cast<Match>().Select(match => int.Parse(match.Groups[1].Value)).ToArray();
        Assert.NotEmpty(used);
        Assert.All(used, glyph => Assert.Contains(glyph, listed));
    }

    [Fact]
    public void Other_files_get_no_CIDSet()
    {
        Assert.Null(SaveAndGetCidFont(PdfAFormats.PdfA_2b).Elements.GetDictionary("/FontDescriptor")!.Elements["/CIDSet"]);
        Assert.Null(SaveAndGetCidFont(null).Elements.GetDictionary("/FontDescriptor")!.Elements["/CIDSet"]);
    }
}
