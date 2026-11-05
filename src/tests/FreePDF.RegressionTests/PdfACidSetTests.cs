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

    static bool HasCidSet(PdfDocument document)
    {
        var fonts = document.Pages.Cast<PdfPage>().Select(page => page.Resources.Elements.GetDictionary("/Font"))
            .Where(fonts => fonts != null).SelectMany(fonts => fonts!.Elements.Values)
            .Select(item => (item as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfDictionary)
            .Where(font => font?.Elements.GetName("/Subtype") == "/Type0").ToArray();
        Assert.NotEmpty(fonts);
        return fonts.All(type0 => ((PdfArray)type0!.Elements.GetValue("/DescendantFonts")!).Elements.GetDictionary(0)!
            .Elements.GetDictionary("/FontDescriptor")!.Elements["/CIDSet"] != null);
    }

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // Found in review: with the default encoding, text in and outside WinAnsi makes a TrueType font and a CID font
    // that share one font descriptor; the shared font file was treated as not CID and got no CIDSet.
    [Fact]
    public void A_CID_font_sharing_its_descriptor_with_a_TrueType_font_gets_a_CIDSet()
    {
        TestFonts.Initialize();
        using var document = new PdfDocument();
        document.SetPdfA(PdfAFormats.PdfA_1b);
        var font = new XFont("FreePDF Synthetic", 12);
        using (var gfx = XGraphics.FromPdfPage(document.AddPage()))
        {
            gfx.DrawString("Hello", font, XBrushes.Black, 50, 100);
            gfx.DrawString("čč", font, XBrushes.Black, 50, 150);
        }

        using var reopened = PdfReader.Open(new MemoryStream(Save(document)), PdfDocumentOpenMode.Import);
        Assert.True(HasCidSet(reopened));
    }

    // Found in review: text added to an opened PDF/A-1 file got no CIDSet, because an opened file is not PDF/A to
    // FreePDF; its XMP's claim now counts.
    [Fact]
    public void Text_added_to_an_opened_PDF_A_1_file_gets_a_CIDSet()
    {
        TestFonts.Initialize();
        byte[] original;
        using (var document = new PdfDocument())
        {
            document.SetPdfA(PdfAFormats.PdfA_1b);
            document.AddPage();
            original = Save(document);
        }
        using var opened = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Modify);
        using (var gfx = XGraphics.FromPdfPage(opened.Pages[0]))
            gfx.DrawString("čč", new XFont("FreePDF Synthetic", 12, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode)), XBrushes.Black, 50, 100);

        using var reopened = PdfReader.Open(new MemoryStream(Save(opened)), PdfDocumentOpenMode.Import);
        Assert.True(HasCidSet(reopened));
    }
}
