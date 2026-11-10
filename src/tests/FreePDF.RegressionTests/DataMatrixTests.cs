// Data Matrix ECC 200, rewritten for 7.9.0 from ISO/IEC 16022:2006. The inherited encoder drew nothing in the Core
// build and lost characters in C40, Text and X12. The reader below follows the standard's annex F placement on its
// own; external decoding (zxing-cpp) is part of the PR's validation. MIT, see LICENSE.
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

public class DataMatrixTests
{
    // The encodation example of ISO/IEC 16022: "123456" in a 10 x 10 symbol is the data codewords 142 164 186
    // (digit pairs plus 130) and the error correction codewords 114 25 5 88 102.
    [Fact]
    public void The_standard_example_has_its_published_codewords()
    {
        var symbol = new CodeDataMatrix("123456", 10).GetModules();

        Assert.Equal(10, symbol.GetLength(0));
        Assert.Equal(new byte[] { 142, 164, 186, 114, 25, 5, 88, 102 }, DataMatrixReader.Codewords(symbol, 1, 1));
    }

    // Finder (solid left and bottom edge) and clock (alternating top and right edge) of every data region.
    [Theory]
    [InlineData(10, 10, 1, 1)]
    [InlineData(32, 32, 2, 2)]
    [InlineData(144, 144, 6, 6)]
    [InlineData(16, 48, 1, 2)]
    public void Every_region_has_its_finder_and_clock(int rows, int columns, int regionsDown, int regionsAcross)
    {
        var symbol = new CodeDataMatrix("AB", "", rows, columns, 0, new XSize(100, 100)).GetModules();

        Assert.Equal(rows, symbol.GetLength(0));
        Assert.Equal(columns, symbol.GetLength(1));
        int height = rows / regionsDown, width = columns / regionsAcross;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                int y = r % height, x = c % width;
                if (x == 0 || y == height - 1)
                    Assert.True(symbol[r, c], $"finder at {r},{c}");
                else if (y == 0)
                    Assert.Equal(x % 2 == 0, symbol[r, c]);
                else if (x == width - 1)
                    Assert.Equal(y % 2 == 1, symbol[r, c]);
            }
        }
    }

    // Each interleaved block is a Reed-Solomon code word over GF(256) with polynomial 0x12D and roots a^1 .. a^n.
    // 144 x 144 has 10 blocks of 62 error correction codewords; the first eight hold 156 data codewords, the last
    // two 155.
    [Theory]
    [InlineData(52, 2, 2, 204, 84)]
    [InlineData(144, 6, 10, 1558, 620)]
    public void Interleaved_blocks_have_valid_error_correction(int size, int regions, int blocks, int data, int ecc)
    {
        var symbol = new CodeDataMatrix(new string('7', 40), "", size, size, 0, new XSize(100, 100)).GetModules();

        var codewords = DataMatrixReader.Codewords(symbol, regions, regions);

        Assert.Equal(data + ecc, codewords.Length);
        for (int block = 0; block < blocks; block++)
        {
            var word = new List<byte>();
            for (int i = block; i < data; i += blocks)
                word.Add(codewords[i]);
            for (int i = data + block; i < data + ecc; i += blocks)
                word.Add(codewords[i]);
            Assert.True(SyndromesAreZero(word, ecc / blocks), $"block {block}");
        }
    }

    [Theory]
    [InlineData("A", new byte[] { 66 })]                                   // ASCII value + 1
    [InlineData("1234", new byte[] { 142, 164 })]                          // digit pairs
    [InlineData("é", new byte[] { 235, 106 })]                            // Upper Shift, then 0xE9 - 127
    [InlineData("ñ0101", new byte[] { 232, 131, 131 })]               // FNC1, then "01" "01"
    public void Ascii_encodation_writes_the_standard_codewords(string text, byte[] expected)
    {
        var symbol = new CodeDataMatrix(text, "", 0, 0, 0, new XSize(100, 100)).GetModules();

        Assert.Equal(expected, DataMatrixReader.Codewords(symbol, 1, 1).Take(expected.Length));
    }

    // Text beyond Latin-1 is UTF-8 after ECI 26 (codewords 241, 27).
    [Fact]
    public void Text_beyond_latin1_is_utf8_after_eci_26()
    {
        var symbol = new CodeDataMatrix("€", "", 0, 0, 0, new XSize(100, 100)).GetModules();

        // € is E2 82 AC in UTF-8: each byte above 127 takes Upper Shift and byte - 127.
        Assert.Equal(new byte[] { 241, 27, 235, 0xE2 - 127, 235, 0x82 - 127, 235, 0xAC - 127 },
            DataMatrixReader.Codewords(symbol, 1, 1).Take(8));
    }

    // Capacity: 144 x 144 holds 1558 data codewords, so 3116 digits; the smallest fitting square is chosen.
    [Fact]
    public void The_smallest_square_that_fits_is_chosen()
    {
        Assert.Equal(10, new CodeDataMatrix("123456", 0).GetModules().GetLength(0));
        Assert.Equal(12, new CodeDataMatrix("1234567", 0).GetModules().GetLength(0));
        Assert.Equal(144, new CodeDataMatrix(new string('5', 3116), 0).GetModules().GetLength(0));
        Assert.Throws<ArgumentException>(() => new CodeDataMatrix(new string('5', 3117), 0));
    }

    [Fact]
    public void Invalid_sizes_and_too_long_texts_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new CodeDataMatrix("x", 11));
        Assert.Throws<ArgumentException>(() => new CodeDataMatrix("x", 30));
        Assert.Throws<ArgumentException>(() => new CodeDataMatrix("1234567", 10));
    }

    // The inherited encoder dropped the last characters in C40, Text and X12 when they did not fill a triplet.
    [Theory]
    [InlineData(DataMatrixEncoding.C40)]
    [InlineData(DataMatrixEncoding.Text)]
    [InlineData(DataMatrixEncoding.X12)]
    [InlineData(DataMatrixEncoding.EDIFACT)]
    public void Every_requested_encoding_keeps_all_characters(DataMatrixEncoding encoding)
    {
        var code = new CodeDataMatrix("HELLO WORLD 12345", encoding, 26, new XSize(100, 100));

        var codewords = DataMatrixReader.Codewords(code.GetModules(), 1, 1);

        // ASCII encodation: "HELLO WORLD " as value + 1, then the digit pairs 12 and 34, then '5'.
        var expected = "HELLO WORLD ".Select(ch => (byte)(ch + 1)).Concat(new byte[] { 142, 164, (byte)('5' + 1) }).ToArray();
        Assert.Equal(expected, codewords.Take(expected.Length));
    }

    // Base256 for runs marked 'b': latch 231, length, bytes, randomized by position (255-state algorithm).
    [Fact]
    public void Base256_runs_are_latched_and_randomized()
    {
        var codewords = DataMatrixReader.Codewords(new CodeDataMatrix("AB", "bb", 0, 0, 0, new XSize(10, 10)).GetModules(), 1, 1);

        static byte Randomized(int value, int position) => (byte)((value + 149 * position % 255 + 1) % 256);
        Assert.Equal(new byte[] { 231, Randomized(2, 2), Randomized('A', 3), Randomized('B', 4) }, codewords.Take(4));
    }

    // Drawn as one path of row runs, in every build; the Core build drew nothing before.
    [Fact]
    public void A_data_matrix_is_drawn_as_one_path()
    {
        var code = new CodeDataMatrix("https://github.com/io7/FreePDF", "", 0, 0, 1, new XSize(100, 100));
        var modules = code.GetModules();
        int runs = 0;
        for (int r = 0; r < modules.GetLength(0); r++)
        {
            for (int c = 0; c < modules.GetLength(1); c++)
            {
                if (modules[r, c] && (c == 0 || !modules[r, c - 1]))
                    runs++;
            }
        }

        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawMatrixCode(code, new XPoint(50, 50));
        using var output = new MemoryStream();
        document.Save(output);
        var reopened = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import);
        var operators = Flatten(ContentReader.ReadContent(reopened.Pages[0])).ToList();

        Assert.Equal(runs, operators.Count(op => op.Name == "m"));
        Assert.Equal(1, operators.Count(op => op.Name == "re")); // the white quiet zone
    }

    [Fact]
    public void MigraDoc_renders_a_data_matrix()
    {
        TestFonts.Initialize();
        var document = new Document();
        var section = document.AddSection();
        var barcode = section.AddBarcode();
        barcode.Type = BarcodeType.DataMatrix;
        barcode.Code = "https://github.com/io7/FreePDF";
        barcode.Width = Unit.FromCentimeter(4);
        barcode.Height = Unit.FromCentimeter(2);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output);

        var page = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import).Pages[0];
        Assert.True(Flatten(ContentReader.ReadContent(page)).Count(op => op.Name == "m") > 20);
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

    static bool SyndromesAreZero(List<byte> word, int eccLength)
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
                    a ^= 0x12D;
                b >>= 1;
            }
            return product;
        }

        int alpha = 2;
        for (int i = 1; i <= eccLength; i++)
        {
            int value = 0;
            foreach (byte codeword in word)
                value = Multiply(value, alpha) ^ codeword;
            if (value != 0)
                return false;
            alpha = Multiply(alpha, 2);
        }
        return true;
    }

    /// <summary>
    /// Reads the codewords of an ECC 200 symbol: strips finders and clocks, then follows the module placement of
    /// ISO/IEC 16022 annex F.
    /// </summary>
    static class DataMatrixReader
    {
        public static byte[] Codewords(bool[,] symbol, int regionsDown, int regionsAcross)
        {
            int rows = symbol.GetLength(0), columns = symbol.GetLength(1);
            int regionHeight = rows / regionsDown - 2, regionWidth = columns / regionsAcross - 2;
            int nrow = regionsDown * regionHeight, ncol = regionsAcross * regionWidth;
            var mapping = new bool[nrow, ncol];
            for (int r = 0; r < nrow; r++)
            {
                for (int c = 0; c < ncol; c++)
                    mapping[r, c] = symbol[r / regionHeight * (regionHeight + 2) + r % regionHeight + 1, c / regionWidth * (regionWidth + 2) + c % regionWidth + 1];
            }

            var codewords = new List<byte>();
            var visited = new bool[nrow, ncol];
            int value = 0;

            void Bit(int row, int column, int bit)
            {
                if (row < 0)
                {
                    row += nrow;
                    column += 4 - (nrow + 4) % 8;
                }
                if (column < 0)
                {
                    column += ncol;
                    row += 4 - (ncol + 4) % 8;
                }
                visited[row, column] = true;
                if (mapping[row, column])
                    value |= 0x80 >> (bit - 1);
            }

            void Finish()
            {
                codewords.Add((byte)value);
                value = 0;
            }

            void Shape(int row, int column)
            {
                (int, int)[] offsets = [(-2, -2), (-2, -1), (-1, -2), (-1, -1), (-1, 0), (0, -2), (0, -1), (0, 0)];
                for (int k = 0; k < 8; k++)
                    Bit(row + offsets[k].Item1, column + offsets[k].Item2, k + 1);
                Finish();
            }

            void Corner((int, int)[] cells)
            {
                for (int k = 0; k < 8; k++)
                    Bit(cells[k].Item1, cells[k].Item2, k + 1);
                Finish();
            }

            int row = 4, col = 0;
            do
            {
                if (row == nrow && col == 0)
                    Corner([(nrow - 1, 0), (nrow - 1, 1), (nrow - 1, 2), (0, ncol - 2), (0, ncol - 1), (1, ncol - 1), (2, ncol - 1), (3, ncol - 1)]);
                if (row == nrow - 2 && col == 0 && ncol % 4 != 0)
                    Corner([(nrow - 3, 0), (nrow - 2, 0), (nrow - 1, 0), (0, ncol - 4), (0, ncol - 3), (0, ncol - 2), (0, ncol - 1), (1, ncol - 1)]);
                if (row == nrow - 2 && col == 0 && ncol % 8 == 4)
                    Corner([(nrow - 3, 0), (nrow - 2, 0), (nrow - 1, 0), (0, ncol - 2), (0, ncol - 1), (1, ncol - 1), (2, ncol - 1), (3, ncol - 1)]);
                if (row == nrow + 4 && col == 2 && ncol % 8 == 0)
                    Corner([(nrow - 1, 0), (nrow - 1, ncol - 1), (0, ncol - 3), (0, ncol - 2), (0, ncol - 1), (1, ncol - 3), (1, ncol - 2), (1, ncol - 1)]);
                do
                {
                    if (row < nrow && col >= 0 && !visited[row, col])
                        Shape(row, col);
                    row -= 2;
                    col += 2;
                } while (row >= 0 && col < ncol);
                row += 1;
                col += 3;
                do
                {
                    if (row >= 0 && col < ncol && !visited[row, col])
                        Shape(row, col);
                    row += 2;
                    col -= 2;
                } while (row < nrow && col >= 0);
                row += 3;
                col += 1;
            } while (row < nrow || col < ncol);

            return codewords.ToArray();
        }
    }
}
