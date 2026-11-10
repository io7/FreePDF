// Upstream #401: ToUnicode maps wrote code points beyond U+FFFF as raw hex (<1F600>) instead of a UTF-16BE surrogate
// pair, so emoji and mathematical letters were extracted as the wrong characters. The maps also put every mapping
// into one bfrange section, while a section may hold at most 100. The font is original (generate-supplementary-font.py).
// MIT, see LICENSE.
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class ToUnicodeMapTests
{
    [Fact]
    public void Code_points_beyond_the_bmp_map_to_surrogate_pairs()
    {
        var map = ToUnicodeMap("A" + Char.ConvertFromUtf32(0x1D400) + Char.ConvertFromUtf32(0x1F600));

        Assert.Contains("<0041>", map);
        Assert.Contains("<D835DC00>", map);   // U+1D400 MATHEMATICAL BOLD CAPITAL A
        Assert.Contains("<D83DDE00>", map);   // U+1F600 GRINNING FACE
        Assert.DoesNotContain("<1D400>", map);
        Assert.DoesNotContain("<1F600>", map);
    }

    [Fact]
    public void A_map_with_more_than_100_entries_is_split_into_sections()
    {
        var text = new string(Enumerable.Range(0x100, 150).Select(cp => (char)cp).ToArray());

        var map = ToUnicodeMap(text);

        var counts = Regex.Matches(map, @"(\d+) beginbfrange").Cast<Match>().Select(m => Int32.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(new[] { 100, 50 }, counts);
        Assert.Equal(2, Regex.Matches(map, "endbfrange").Count);
        Assert.Equal(150, Regex.Matches(map, @"<[0-9A-F]{4}><[0-9A-F]{4}><[0-9A-F]{4,8}>").Count);
    }

    static string ToUnicodeMap(string text)
    {
        TestFonts.Initialize();
        using var saved = new MemoryStream();
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            var font = new XFont("FreePDF Supplementary", 12, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString(text, font, XBrushes.Black, 20, 40);
            document.Save(saved);
        }

        var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        var fonts = reopened.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        var type0 = fonts.Elements.Values.Select(v => (v as PdfReference)?.Value as PdfDictionary)
            .Single(d => d?.Elements.GetName("/Subtype") == "/Type0")!;
        var toUnicode = (PdfDictionary)((PdfReference)type0.Elements["/ToUnicode"]!).Value;
        return System.Text.Encoding.ASCII.GetString(toUnicode.Stream!.UnfilteredValue);
    }
}
