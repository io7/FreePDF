// QR codes, Code 128, EAN-13, EAN-8 and UPC-A, written for 7.9.0 from ISO/IEC 18004, 15417 and 15420. The reader
// below is independent of the encoder: it takes positions and tables from the standards, not from the library.
// Decoding by an external reader (zxing-cpp) is part of the PR's validation, not of these tests. MIT, see LICENSE.
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.BarCodes;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class BarcodeTests
{
    // ----- QR code ---------------------------------------------------------------------------------------------

    // The worked example of many QR tutorials: "HELLO WORLD" at version 1, level M (alphanumeric mode).
    [Fact]
    public void Hello_world_at_1_M_has_the_published_codewords()
    {
        var modules = new CodeQR("HELLO WORLD", new XSize(100, 100), QRErrorCorrectionLevel.M).GetModules();

        Assert.Equal(21, modules.GetLength(0));
        var symbol = QRReader.Read(modules);
        Assert.Equal(0b00, symbol.LevelBits); // M
        Assert.Equal(new byte[] { 32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17 }, symbol.Codewords.Take(16));
        Assert.Equal(new byte[] { 196, 35, 39, 119, 235, 215, 231, 226, 93, 23 }, symbol.Codewords.Skip(16));
    }

    // Version 5-Q has two blocks of 15 and two of 16 data codewords, 18 error correction codewords each; version 40-H
    // has 20 blocks of 15 and 61 of 16 with 30 each (ISO/IEC 18004 table 9). Every block must be a Reed-Solomon
    // code word: its syndromes are zero.
    [Theory]
    [InlineData(5, QRErrorCorrectionLevel.Q, new[] { 15, 15, 16, 16 }, 18)]
    [InlineData(40, QRErrorCorrectionLevel.H, null, 30)]
    public void Interleaved_blocks_have_valid_error_correction(int version, QRErrorCorrectionLevel level, int[]? dataLengths, int eccLength)
    {
        dataLengths ??= Enumerable.Repeat(15, 20).Concat(Enumerable.Repeat(16, 61)).ToArray();
        var text = new string('7', 3 * dataLengths.Sum()); // more than fits in smaller versions
        var code = new CodeQR(text.Substring(0, 50), new XSize(100, 100), level) { Version = version };

        var symbol = QRReader.Read(code.GetModules());

        var blocks = QRReader.Deinterleave(symbol.Codewords, dataLengths, eccLength);
        Assert.All(blocks, block => Assert.True(GaloisField.SyndromesAreZero(block, eccLength)));
    }

    // Version information of versions 7 and 8 (ISO/IEC 18004 annex D): 0x07C94 and 0x085BC, in both places.
    [Theory]
    [InlineData(7, 0x07C94)]
    [InlineData(8, 0x085BC)]
    public void Version_information_matches_the_standard(int version, int expected)
    {
        var modules = new CodeQR("FREEPDF", new XSize(100, 100)) { Version = version }.GetModules();

        Assert.Equal(17 + 4 * version, modules.GetLength(0));
        var (topRight, bottomLeft) = QRReader.VersionInformation(modules);
        Assert.Equal(expected, topRight);
        Assert.Equal(expected, bottomLeft);
    }

    // The 15-bit format information is written twice and carries a valid BCH(15,5) code.
    [Theory]
    [InlineData(QRErrorCorrectionLevel.L, 0b01)]
    [InlineData(QRErrorCorrectionLevel.M, 0b00)]
    [InlineData(QRErrorCorrectionLevel.Q, 0b11)]
    [InlineData(QRErrorCorrectionLevel.H, 0b10)]
    public void Format_information_names_the_level(QRErrorCorrectionLevel level, int levelBits)
    {
        var symbol = QRReader.Read(new CodeQR("https://github.com/io7/FreePDF", new XSize(100, 100), level).GetModules());

        Assert.Equal(levelBits, symbol.LevelBits);
        Assert.InRange(symbol.Mask, 0, 7);
    }

    // Capacities from ISO/IEC 18004 table 7: the smallest version is chosen, and one character more takes the next.
    [Theory]
    [InlineData("0", 41, QRErrorCorrectionLevel.L, 21)]       // numeric, 1-L
    [InlineData("A", 20, QRErrorCorrectionLevel.M, 21)]       // alphanumeric, 1-M
    [InlineData("a", 14, QRErrorCorrectionLevel.M, 21)]       // byte, 1-M
    [InlineData("a", 7, QRErrorCorrectionLevel.H, 21)]        // byte, 1-H
    [InlineData("A", 1852, QRErrorCorrectionLevel.H, 177)]    // alphanumeric, 40-H
    [InlineData("0", 7089, QRErrorCorrectionLevel.L, 177)]    // numeric, 40-L
    public void The_smallest_version_that_fits_is_chosen(string character, int count, QRErrorCorrectionLevel level, int size)
    {
        var text = String.Concat(Enumerable.Repeat(character, count));

        Assert.Equal(size, new CodeQR(text, new XSize(100, 100), level).GetModules().GetLength(0));
        if (size < 177)
            Assert.Equal(size + 4, new CodeQR(text + character, new XSize(100, 100), level).GetModules().GetLength(0));
        else
            Assert.Throws<ArgumentException>(() => new CodeQR(text + character, new XSize(100, 100), level).GetModules());
    }

    // Text that is not ASCII is UTF-8, marked with ECI 26 unless Utf8Eci is false.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Text_beyond_ascii_is_utf8(bool eci)
    {
        var code = new CodeQR("Blåbær", new XSize(100, 100)) { Utf8Eci = eci };

        var bits = QRReader.Bits(QRReader.Read(code.GetModules()).Codewords);

        var utf8 = System.Text.Encoding.UTF8.GetBytes("Blåbær");
        var expected = (eci ? "0111" + "00011010" : "") + "0100" + Convert.ToString(utf8.Length, 2).PadLeft(8, '0')
                       + String.Concat(utf8.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        Assert.StartsWith(expected, bits);
    }

    [Fact]
    public void Invalid_settings_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new CodeQR(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodeQR("x") { Version = 41 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodeQR("x") { QuietZone = -1 });
        Assert.Throws<ArgumentException>(() => new CodeQR(new string('x', 20), new XSize(10, 10), QRErrorCorrectionLevel.H) { Version = 1 }.GetModules());

        using var document = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(document.AddPage());
        Assert.Throws<InvalidOperationException>(() => gfx.DrawMatrixCode(new CodeQR("no size"), new XPoint(10, 10)));
    }

    // The symbol is one filled path: one rectangle per run of dark modules in a row, inside the quiet zone.
    [Fact]
    public void A_qr_code_is_drawn_as_one_path_of_module_runs()
    {
        var code = new CodeQR("https://github.com/io7/FreePDF", new XSize(116, 116)); // 29 + 8 modules, 116/37 pt each
        var modules = code.GetModules();
        int runs = 0;
        for (int row = 0; row < modules.GetLength(0); row++)
        {
            for (int column = 0; column < modules.GetLength(1); column++)
            {
                if (modules[row, column] && (column == 0 || !modules[row, column - 1]))
                    runs++;
            }
        }

        var operators = DrawAndReadOperators(gfx => gfx.DrawMatrixCode(code, new XPoint(40, 40)));

        var rectangles = PathRectangles(operators);
        Assert.Equal(runs, rectangles.Count);
        Assert.Equal(1, operators.Count(op => op.Name is "f" or "f*"));
        double module = 116.0 / (modules.GetLength(0) + 8);
        Assert.InRange(rectangles.Min(r => r.X), 40 + 4 * module - 0.01, 40 + 4 * module + 0.01);
        Assert.InRange(rectangles.Max(r => r.X + r.Width), 156 - 4 * module - 0.01, 156 - 4 * module + 0.01);
        Assert.All(rectangles, r => Assert.InRange(r.Height, module - 0.01, module + 0.01));
    }

    // ----- Code 128 --------------------------------------------------------------------------------------------

    [Theory]
    // Code set B; check = (104 + 48 + 2*42 + 3*42 + 4*17 + 5*18 + 6*19 + 7*35) mod 103 = 55.
    [InlineData("PJJ123C", new[] { 104, 48, 42, 42, 17, 18, 19, 35, 55 })]
    // Code set C, two digits per character; check 1115 mod 103 = 85.
    [InlineData("1234567890", new[] { 105, 12, 34, 56, 78, 90, 85 })]
    // A control character needs code set A: tab is 9 + 64.
    [InlineData("\tA", new[] { 103, 73, 33, 36 })]
    // A single control character between lower-case letters is cheapest with a shift.
    [InlineData("a\nb", new[] { 104, 65, 98, 74, 66, 27 })]
    // An odd number of digits: the first in code set B, then code C (99) for the pairs; as short as C first.
    // Check (104 + 17 + 2*99 + 3*23 + 4*45) mod 103 = 53.
    [InlineData("12345", new[] { 104, 17, 99, 23, 45, 53 })]
    // GS1-128: FNC1 (102) after the start character; check 895 mod 103 = 71.
    [InlineData("\uE0F10109501101530003", new[] { 105, 102, 1, 9, 50, 11, 1, 53, 0, 3, 71 })]
    public void Code_128_uses_the_fewest_symbol_characters(string text, int[] expected)
    {
        var code = new Code128(text);

        Assert.Equal(expected, code.GetSymbolValues());
        var modules = code.GetModules();
        Assert.Equal(11 * expected.Length + 13, modules.Length);
        Assert.True(modules[0] && modules[modules.Length - 1], "starts and ends with a bar");
    }

    [Fact]
    public void Code_128_refuses_text_it_cannot_encode()
    {
        Assert.Throws<ArgumentException>(() => new Code128(""));
        Assert.Throws<ArgumentException>(() => new Code128("café"));
    }

    // Code 39 accepted an apostrophe, which has no bar pattern, and then failed with IndexOutOfRangeException
    // while drawing.
    [Fact]
    public void Code_39_refuses_an_apostrophe()
    {
        Assert.Throws<ArgumentException>(() => new Code3of9Standard("A'B"));
    }

    // ----- EAN and UPC -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("590123412345", "5901234123457")]
    [InlineData("400638133393", "4006381333931")]
    [InlineData("4006381333931", "4006381333931")]
    public void Ean_13_adds_or_checks_the_check_digit(string code, string full)
    {
        var ean = new Ean13(code);

        Assert.Equal(full, ean.FullCode);
        var modules = ean.GetModules();
        Assert.Equal(95, modules.Length);
        Assert.Equal("101", Bits(modules, 0, 3));
        Assert.Equal("01010", Bits(modules, 45, 5));
        Assert.Equal("101", Bits(modules, 92, 3));
    }

    [Fact]
    public void Ean_8_and_upc_a_add_their_check_digits()
    {
        Assert.Equal("96385074", new Ean8("9638507").FullCode);
        Assert.Equal(67, new Ean8("9638507").GetModules().Length);
        Assert.Equal("036000291452", new UpcA("03600029145").FullCode);
        // UPC-A is an EAN-13 with a leading zero.
        Assert.Equal(new Ean13("0036000291452").GetModules(), new UpcA("036000291452").GetModules());
    }

    [Fact]
    public void Ean_and_upc_refuse_wrong_codes()
    {
        Assert.Throws<ArgumentException>(() => new Ean13("5901234123458"));
        Assert.Throws<ArgumentException>(() => new Ean13("59012341234"));
        Assert.Throws<ArgumentException>(() => new Ean8("1234567X"));
        Assert.Throws<ArgumentException>(() => new UpcA("036000291453"));
    }

    [Fact]
    public void Linear_codes_come_from_FromType()
    {
        Assert.IsType<Code128>(BarCode.FromType(CodeType.Code128, "ABC", new XSize(100, 30)));
        Assert.IsType<Ean13>(BarCode.FromType(CodeType.Ean13, "590123412345", new XSize(100, 30)));
        Assert.IsType<Ean8>(BarCode.FromType(CodeType.Ean8, "9638507", new XSize(100, 30)));
        Assert.IsType<UpcA>(BarCode.FromType(CodeType.UpcA, "03600029145", new XSize(100, 30)));
    }

    // An EAN-13 with digits: guard bars reach lower than the data bars, and the 13 digits are drawn.
    [Fact]
    public void Ean_13_draws_long_guard_bars_and_its_digits()
    {
        TestFonts.Initialize();
        var ean = new Ean13("590123412345", new XSize(113 * 2, 80));

        var operators = DrawAndReadOperators(gfx => gfx.DrawBarCode(ean, XBrushes.Black, new XFont("FreePDF Synthetic", 22), new XPoint(10, 10)));

        var heights = PathRectangles(operators).Select(r => Math.Round(r.Height, 2)).Distinct().OrderBy(h => h).ToList();
        Assert.Equal(2, heights.Count);
        Assert.InRange(heights[1] - heights[0], 4.99 * 2, 5.01 * 2); // guard bars reach 5 modules lower
        Assert.Equal(13, operators.Count(op => op.Name is "Tj" or "TJ"));
    }

    // ----- MigraDoc --------------------------------------------------------------------------------------------

    // Before 7.9.0 MigraDoc had a Barcode element but no renderer, so barcodes were silently left out of the PDF.
    [Theory]
    [InlineData(BarcodeType.Barcode25i, "1234567890", false)]
    [InlineData(BarcodeType.Barcode39, "FREEPDF", true)]
    [InlineData(BarcodeType.Barcode128, "Invoice 2026-118", true)]
    [InlineData(BarcodeType.Ean13, "590123412345", true)]
    [InlineData(BarcodeType.Ean8, "9638507", false)]
    [InlineData(BarcodeType.UpcA, "03600029145", true)]
    [InlineData(BarcodeType.QRCode, "https://github.com/io7/FreePDF", false)]
    public void MigraDoc_renders_barcodes(BarcodeType type, string code, bool text)
    {
        TestFonts.Initialize();
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = "FreePDF Synthetic";
        var section = document.AddSection();
        section.AddParagraph("Before");
        var barcode = section.AddBarcode();
        barcode.Type = type;
        barcode.Code = code;
        barcode.Text = text;
        barcode.Width = Unit.FromCentimeter(type == BarcodeType.QRCode ? 3 : 6);
        barcode.Height = Unit.FromCentimeter(3);
        section.AddParagraph("After");
        var vertical = section.AddBarcode();
        vertical.Type = type;
        vertical.Code = code;
        vertical.Orientation = TextOrientation.Upward;
        vertical.Width = Unit.FromCentimeter(3);
        vertical.Height = Unit.FromCentimeter(type == BarcodeType.QRCode ? 3 : 6);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output);

        var page = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import).Pages[0];
        var operators = Flatten(ContentReader.ReadContent(page)).ToList();
        Assert.True(operators.Count(op => op.Name is "re" or "m") > 20, "the bars or modules are drawn");
        Assert.True(operators.Count(op => op.Name == "cm") >= 1, "the upward code is rotated");
    }

    // ----- found by review -------------------------------------------------------------------------------------

    // The code fills the shape inside its border (half a line width in from the stroke's centre), and Code 128,
    // Code 39 and 2 of 5 keep a quiet zone inside the shape. The bars sat half a line width too far right and
    // touched the border, so bordered codes could not be read.
    [Theory]
    [InlineData(BarcodeType.Barcode128, "FREEPDF-118")]
    [InlineData(BarcodeType.Barcode39, "FREEPDF")]
    [InlineData(BarcodeType.Barcode25i, "123456")]
    public void MigraDoc_keeps_linear_codes_off_the_border(BarcodeType type, string code)
    {
        TestFonts.Initialize();
        var document = new Document();
        var barcode = document.AddSection().AddBarcode();
        barcode.Type = type;
        barcode.Code = code;
        barcode.Width = Unit.FromCentimeter(6);
        barcode.Height = Unit.FromCentimeter(2);
        barcode.LineFormat.Width = 8;
        barcode.LineFormat.Color = Colors.Black;

        var operators = RenderMigraDoc(document);

        var rectangles = operators.Where(op => op.Name == "re")
            .Select(op => new XRect(Number(op, 0), Number(op, 1), Number(op, 2), Number(op, 3))).ToList();
        var border = rectangles.OrderByDescending(r => r.Width).First();
        var bars = rectangles.Where(r => r != border).Concat(PathRectangles(operators)).ToList();
        double innerLeft = border.X + 4, innerRight = border.X + border.Width - 4;
        double left = bars.Min(r => r.X) - innerLeft, right = innerRight - bars.Max(r => r.X + r.Width);
        Assert.InRange(left - right, -0.05, 0.05);
        if (type == BarcodeType.Barcode128)
        {
            double width = Unit.FromCentimeter(6).Point - 8;
            Assert.InRange(left, 10 * width / (new Code128(code).GetModules().Length + 20) - 0.05, 10 * width / (new Code128(code).GetModules().Length + 20) + 0.05);
        }
        else
            Assert.True(left > 4, $"quiet zone {left}");
    }

    // An exception while drawing leaves the graphics state as it was; the state stayed rotated and saved.
    [Fact]
    public void A_failed_draw_restores_the_graphics_state()
    {
        TestFonts.Initialize();
        static int DepthAtRectangle(List<COperator> operators)
        {
            int depth = 0;
            foreach (var op in operators)
            {
                if (op.Name == "q")
                    depth++;
                else if (op.Name == "Q")
                    depth--;
                else if (op.Name == "re")
                    return depth;
            }
            throw new InvalidOperationException("No rectangle drawn.");
        }

        var baseline = DrawAndReadOperators(gfx => gfx.DrawRectangle(XBrushes.Black, 50, 60, 100, 100));
        var afterFailures = DrawAndReadOperators(gfx =>
        {
            var font = new XFont("FreePDF Synthetic", 20);
            var tooLow = new Ean13("590123412345", new XSize(113, 10)) { Direction = CodeDirection.TopToBottom };
            Assert.Throws<InvalidOperationException>(() => gfx.DrawBarCode(tooLow, XBrushes.Black, font, new XPoint(400, 50)));
            var embedded = new Code128("ABC", new XSize(100, 8), CodeDirection.BottomToTop) { TextLocation = TextLocation.BelowEmbedded };
            Assert.Throws<InvalidOperationException>(() => gfx.DrawBarCode(embedded, XBrushes.Black, font, new XPoint(400, 50)));
            gfx.DrawRectangle(XBrushes.Black, 50, 60, 100, 100);
        });

        Assert.Equal(DepthAtRectangle(baseline), DepthAtRectangle(afterFailures));
        Assert.Equal(baseline.Count(op => op.Name == "cm"), afterFailures.Count(op => op.Name == "cm"));
    }

    // FNC1 is a private-use character: a real ñ (U+00F1) in text was taken for FNC1.
    [Fact]
    public void Fnc1_is_not_a_letter()
    {
        Assert.Throws<ArgumentException>(() => new Code128("A" + (char)0xF1 + "o"));
        Assert.Equal(102, new Code128(Code128.Fnc1 + "01").GetSymbolValues()[1]);
    }

    // A lone surrogate is refused; it was written as U+FFFD.
    [Fact]
    public void A_qr_code_refuses_a_lone_surrogate()
        => Assert.ThrowsAny<ArgumentException>(() => new CodeQR("a" + (char)0xD800 + "b").GetModules());

    // The DDL writer did not escape the code: quotes cut it short, backslashes and line breaks failed to parse.
    [Theory]
    [InlineData("say \"hi\" there")]
    [InlineData("C:\\path\\file")]
    [InlineData("line1\nline2\r\nend\ttab")]
    public void A_barcode_survives_a_ddl_round_trip(string code)
    {
        var document = new Document();
        var barcode = document.AddSection().AddBarcode();
        barcode.Type = BarcodeType.QRCode;
        barcode.Code = code;

        var read = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document));

        var back = Assert.IsType<Barcode>(read.Sections[0]!.Elements[0]);
        Assert.Equal(code, back.Code);
        Assert.Equal(BarcodeType.QRCode, back.Type);
    }

    // The RTF renderer threw "Unsupported type in RendererFactory" for a barcode; it now leaves it out.
    [Fact]
    public void Rtf_output_leaves_barcodes_out()
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph("Before");
        var barcode = section.AddBarcode();
        barcode.Type = BarcodeType.Barcode128;
        barcode.Code = "ABC";
        section.AddParagraph("After");

        using var stream = new MemoryStream();
        new MigraDoc.RtfRendering.RtfDocumentRenderer().Render(document, stream, false, "");

        var rtf = System.Text.Encoding.ASCII.GetString(stream.ToArray());
        Assert.Contains("Before", rtf);
        Assert.Contains("After", rtf);
    }

    [Fact]
    public void An_unknown_barcode_type_is_refused()
    {
        var document = new Document();
        var barcode = document.AddSection().AddBarcode();
        barcode.Type = (BarcodeType)99;
        barcode.Code = "1234";
        barcode.Width = barcode.Height = Unit.FromCentimeter(3);

        var renderer = new PdfDocumentRenderer { Document = document };
        Assert.Throws<ArgumentException>(() => renderer.RenderDocument());
    }

    static List<COperator> RenderMigraDoc(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output);
        var page = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import).Pages[0];
        return Flatten(ContentReader.ReadContent(page)).ToList();
    }

    // ----- helpers ---------------------------------------------------------------------------------------------

    static string Bits(bool[] modules, int start, int count)
        => String.Concat(modules.Skip(start).Take(count).Select(m => m ? '1' : '0'));

    static List<COperator> DrawAndReadOperators(Action<XGraphics> draw)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            draw(gfx);
        using var output = new MemoryStream();
        document.Save(output);
        var reopened = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import);
        return Flatten(ContentReader.ReadContent(reopened.Pages[0])).ToList();
    }

    static IEnumerable<COperator> Flatten(CSequence sequence)
    {
        foreach (var item in sequence)
        {
            if (item is COperator op)
                yield return op;
            else if (item is CSequence inner)
            {
                foreach (var nested in Flatten(inner))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// The closed subpaths of the page (m, l..., h) as bounding rectangles.
    /// </summary>
    static List<XRect> PathRectangles(List<COperator> operators)
    {
        var result = new List<XRect>();
        var points = new List<XPoint>();
        foreach (var op in operators)
        {
            switch (op.Name)
            {
                case "m":
                    points.Clear();
                    points.Add(new XPoint(Number(op, 0), Number(op, 1)));
                    break;
                case "l":
                    points.Add(new XPoint(Number(op, 0), Number(op, 1)));
                    break;
                case "h" when points.Count > 0:
                    double left = points.Min(p => p.X), right = points.Max(p => p.X);
                    double bottom = points.Min(p => p.Y), top = points.Max(p => p.Y);
                    result.Add(new XRect(left, bottom, right - left, top - bottom));
                    points.Clear();
                    break;
            }
        }
        return result;
    }

    static double Number(COperator op, int index) => op.Operands[index] switch
    {
        CReal real => real.Value,
        CInteger integer => integer.Value,
        var other => throw new InvalidOperationException($"Operand {other} is not a number."),
    };

    /// <summary>
    /// Reads format information, version information and codewords from a QR module matrix. Positions follow
    /// ISO/IEC 18004:2015 sections 7.7 to 7.9; alignment pattern positions come from its annex E.
    /// </summary>
    static class QRReader
    {
        static readonly Dictionary<int, int[]> AlignmentPositions = new()
        {
            [1] = [], [2] = [6, 18], [3] = [6, 22], [4] = [6, 26], [5] = [6, 30], [6] = [6, 34],
            [7] = [6, 22, 38], [8] = [6, 24, 42],
            [40] = [6, 30, 58, 86, 114, 142, 170],
        };

        public sealed class Symbol(int levelBits, int mask, byte[] codewords)
        {
            public int LevelBits { get; } = levelBits;
            public int Mask { get; } = mask;
            public byte[] Codewords { get; } = codewords;
        }

        public static Symbol Read(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int version = (size - 17) / 4;

            int copyA = 0, copyB = 0;
            for (int i = 0; i <= 5; i++)
                copyA |= Bit(modules[i, 8], i);
            copyA |= Bit(modules[7, 8], 6) | Bit(modules[8, 8], 7) | Bit(modules[8, 7], 8);
            for (int i = 9; i < 15; i++)
                copyA |= Bit(modules[8, 14 - i], i);
            for (int i = 0; i < 8; i++)
                copyB |= Bit(modules[8, size - 1 - i], i);
            for (int i = 8; i < 15; i++)
                copyB |= Bit(modules[size - 15 + i, 8], i);
            Assert.Equal(copyA, copyB);
            Assert.True(modules[size - 8, 8], "the dark module");

            int format = copyA ^ 0x5412;
            int data = format >> 10;
            int remainder = data;
            for (int i = 0; i < 10; i++)
                remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            Assert.Equal(format, (data << 10) | (remainder & 0x3FF));
            int levelBits = data >> 3;
            int mask = data & 7;

            bool[,] function = FunctionModules(size, version);
            var bits = new List<bool>();
            bool upwards = true;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                    right = 5;
                for (int step = 0; step < size; step++)
                {
                    int row = upwards ? size - 1 - step : step;
                    for (int k = 0; k < 2; k++)
                    {
                        int column = right - k;
                        if (!function[row, column])
                            bits.Add(modules[row, column] ^ Masked(mask, row, column));
                    }
                }
                upwards = !upwards;
            }

            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < codewords.Length * 8; i++)
            {
                if (bits[i])
                    codewords[i / 8] |= (byte)(0x80 >> (i % 8));
            }
            return new Symbol(levelBits, mask, codewords);
        }

        public static (int TopRight, int BottomLeft) VersionInformation(bool[,] modules)
        {
            int size = modules.GetLength(0);
            int topRight = 0, bottomLeft = 0;
            for (int i = 0; i < 18; i++)
            {
                topRight |= Bit(modules[i / 3, size - 11 + i % 3], i);
                bottomLeft |= Bit(modules[size - 11 + i % 3, i / 3], i);
            }
            return (topRight, bottomLeft);
        }

        public static List<byte[]> Deinterleave(byte[] codewords, int[] dataLengths, int eccLength)
        {
            var blocks = dataLengths.Select(length => new List<byte>(length + eccLength)).ToList();
            int index = 0;
            for (int i = 0; i < dataLengths.Max(); i++)
            {
                for (int b = 0; b < blocks.Count; b++)
                {
                    if (i < dataLengths[b])
                        blocks[b].Add(codewords[index++]);
                }
            }
            for (int i = 0; i < eccLength; i++)
            {
                foreach (var block in blocks)
                    block.Add(codewords[index++]);
            }
            return blocks.Select(block => block.ToArray()).ToList();
        }

        public static string Bits(byte[] codewords)
            => String.Concat(codewords.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

        static bool[,] FunctionModules(int size, int version)
        {
            var function = new bool[size, size];
            void Mark(int row, int column, int height, int width)
            {
                for (int r = row; r < row + height; r++)
                {
                    for (int c = column; c < column + width; c++)
                        function[r, c] = true;
                }
            }

            Mark(0, 0, 9, 9);                 // finder, separator and format information
            Mark(0, size - 8, 9, 8);
            Mark(size - 8, 0, 8, 9);
            Mark(6, 0, 1, size);              // timing patterns
            Mark(0, 6, size, 1);
            var positions = AlignmentPositions[version];
            int last = positions.Length == 0 ? 0 : positions[positions.Length - 1];
            foreach (int row in positions)
            {
                foreach (int column in positions)
                {
                    bool atFinder = (row == 6 && column == 6) || (row == 6 && column == last) || (row == last && column == 6);
                    if (!atFinder)
                        Mark(row - 2, column - 2, 5, 5);
                }
            }
            if (version >= 7)
            {
                Mark(0, size - 11, 6, 3);     // version information
                Mark(size - 11, 0, 3, 6);
            }
            return function;
        }

        static bool Masked(int mask, int i, int j) => mask switch
        {
            0 => (i + j) % 2 == 0,
            1 => i % 2 == 0,
            2 => j % 3 == 0,
            3 => (i + j) % 3 == 0,
            4 => (i / 2 + j / 3) % 2 == 0,
            5 => i * j % 2 + i * j % 3 == 0,
            6 => (i * j % 2 + i * j % 3) % 2 == 0,
            _ => ((i + j) % 2 + i * j % 3) % 2 == 0,
        };

        static int Bit(bool dark, int index) => dark ? 1 << index : 0;
    }

    /// <summary>
    /// GF(256) with the QR field polynomial 0x11D, only for checking Reed-Solomon syndromes.
    /// </summary>
    static class GaloisField
    {
        static int Multiply(int a, int b)
        {
            int product = 0;
            while (b != 0)
            {
                if ((b & 1) != 0)
                    product ^= a;
                a <<= 1;
                if ((a & 0x100) != 0)
                    a ^= 0x11D;
                b >>= 1;
            }
            return product;
        }

        // A code word c(x) is valid when c(alpha^i) = 0 for i = 0 .. eccLength - 1 (alpha = 2).
        public static bool SyndromesAreZero(byte[] block, int eccLength)
        {
            int alpha = 1;
            for (int i = 0; i < eccLength; i++)
            {
                int value = 0;
                foreach (byte codeword in block)
                    value = Multiply(value, alpha) ^ codeword;
                if (value != 0)
                    return false;
                alpha = Multiply(alpha, 2);
            }
            return true;
        }
    }
}
