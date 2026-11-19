// Original text extraction tests: hostile fonts in a child process with a 256 MiB heap and a 10 s budget. MIT; see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

/// <summary>
/// Runs one text extraction scenario in the child process that ReaderProbe starts with --text-probe.
/// </summary>
static class TextProbe
{
    internal static int Run(string scenario)
    {
        try
        {
            var pdf = scenario switch
            {
                "shared-w" => SharedWidths(),
                "code-spaces" => ManyCodeSpaces(),
                "expansion" => Expansion(),
                "shared-cidfont" => SharedCidFont(),
                _ => throw new ArgumentException("Unknown scenario " + scenario),
            };
            using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
            var text = PdfSharp.Pdf.Content.Text.PdfTextExtractor.GetText(document.Pages[0]);
            Console.WriteLine("EXTRACTED: " + text.Length);
            return 0;
        }
        catch (PdfReaderException ex) { Console.WriteLine("REJECTED: " + ex.Message); return 11; }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); return 12; }
    }

    // One array of 10,000 widths, referenced 20,000 times from /W: expanding each reference is 200 million entries.
    static byte[] SharedWidths() => TextPdf.Page("BT /F1 10 Tf 0 0 Td <00010002> Tj ET",
        TextPdf.Composite("1 beginbfrange <0001> <0002> <0041> endbfrange", descendant: (d, cidFont) =>
        {
            var widths = TextPdf.Numbers(d, Enumerable.Repeat(500.0, 10_000).ToArray());
            d.Internals.AddObject(widths);
            var w = new PdfArray(d);
            for (int i = 0; i < 20_000; i++)
            {
                w.Elements.Add(new PdfInteger(i * 10_000));
                w.Elements.Add(widths.Reference!);
            }
            cidFont.Elements["/W"] = w;
        }));

    // 50,000 two-byte code space ranges that do not hold the code shown, and a string of one million codes.
    static byte[] ManyCodeSpaces()
    {
        var spaces = new StringBuilder("50000 begincodespacerange\n");
        for (int i = 0; i < 50_000; i++)
            spaces.Append('<').Append((0x1000 + i).ToString("X4")).Append("> <").Append((0x1000 + i).ToString("X4")).Append(">\n");
        spaces.Append("endcodespacerange");
        var encoding = TextPdf.CMap("1 begincidrange <0000> <FFFF> 0 endcidrange", spaces.ToString());
        var codes = new StringBuilder("BT /F1 10 Tf 0 0 Td <");
        for (int i = 0; i < 1_000_000; i++)
            codes.Append("0041");
        codes.Append("> Tj ET");
        return TextPdf.Page(codes.ToString(), TextPdf.Composite("", d => TextPdf.Stream(d, encoding)));
    }

    // 1,000 Type0 fonts share one CIDFont whose /W has 50,000 entries: 50 million widths if each font copies them.
    static byte[] SharedCidFont()
    {
        PdfDictionary? cidFont = null;
        PdfDictionary Make(PdfDocument d)
        {
            if (cidFont == null)
            {
                cidFont = TextPdf.Font(d, "/CIDFontType2", "/ABCDEF+Synthetic");
                var list = TextPdf.Numbers(d, 500);
                d.Internals.AddObject(list);
                var w = new PdfArray(d);
                for (int i = 0; i < 50_000; i++)
                {
                    w.Elements.Add(new PdfInteger(i));
                    w.Elements.Add(list.Reference!);
                }
                cidFont.Elements["/W"] = w;
                TextPdf.Indirect(d, cidFont);
            }
            var font = TextPdf.Font(d, "/Type0", "/ABCDEF+Synthetic", new PdfName("/Identity-H"));
            font.Elements["/DescendantFonts"] = TextPdf.Items(d, cidFont.Reference!);
            return font;
        }
        var content = new StringBuilder("BT ");
        for (int i = 0; i < 1000; i++)
            content.Append("/F").Append(i).Append(" 10 Tf ");
        content.Append("0 0 Td <0001> Tj ET");
        return TextPdf.Page(content.ToString(), Enumerable.Range(0, 1000).Select(i => ("/F" + i, (Func<PdfDocument, PdfDictionary>)Make)).ToArray());
    }

    // A ToUnicode CMap that maps one byte to 8,192 characters, shown 100,000 times: 1.6 GB of text.
    static byte[] Expansion()
    {
        var toUnicode = TextPdf.CMap("1 beginbfchar <41> <" + String.Concat(Enumerable.Repeat("0042", 8192)) + "> endbfchar",
            "1 begincodespacerange <00> <FF> endcodespacerange");
        return TextPdf.Page("BT /F1 10 Tf 0 0 Td (" + new string('A', 100_000) + ") Tj ET", ("/F1", d =>
        {
            var font = TextPdf.Font(d, "/Type1", "/Helvetica");
            font.Elements["/ToUnicode"] = TextPdf.Stream(d, toUnicode);
            return font;
        }));
    }
}

public partial class TextExtractionTests
{
    [Theory]
    [InlineData("shared-w", 0, "EXTRACTED")]
    [InlineData("code-spaces", 0, "EXTRACTED")]
    [InlineData("expansion", 11, "MaxDecodedBytes")]
    [InlineData("shared-cidfont", 11, "MaxDecodedBytes")]
    public async Task Hostile_fonts_stay_within_the_process_budget(string scenario, int exitCode, string message)
    {
        var result = await ReaderRobustnessTests.Probe(scenario, "--text-probe");
        Assert.True(result.ExitCode == exitCode, $"Expected exit {exitCode}, actual {result.ExitCode}: {result.Output}");
        Assert.Contains(message, result.Output);
    }
}
