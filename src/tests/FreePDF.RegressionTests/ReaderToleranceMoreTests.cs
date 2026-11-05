// Files that poppler and MuPDF open and FreePDF refused, found in the upstream PDFsharp triage of 2026-11-05. The
// fixtures are our own. MIT, see LICENSE.
using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using FreePDF.Lab.Fixtures;
using Xunit;

namespace FreePDF.RegressionTests;

public class ReaderToleranceMoreTests
{
    static PdfDocument Open(byte[] bytes, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Import)
        => PdfReader.Open(new MemoryStream(bytes), mode);

    static byte[] Append(byte[] bytes, string text) => bytes.Concat(TestPdf.Latin1.GetBytes(text)).ToArray();

    // Upstream #390: with 'startxref' outside the last 1,030 bytes, the whole file was read into one string.
    [Theory]
    [InlineData(5000)]
    [InlineData(-1)]
    public void Startxref_far_from_the_end_is_found(int padding)
    {
        var bytes = TestPdf.Create(ReaderFixtureData.SimplePage);
        var keyword = TestPdf.Latin1.GetString(bytes).LastIndexOf("startxref", StringComparison.Ordinal);
        // -1 places 'startxref' across the border of the first 64 KiB block searched from the end.
        if (padding < 0)
            padding = 64 * 1024 + 4 - (bytes.Length - keyword);
        bytes = Append(bytes, new string('\n', padding));

        using var document = Open(bytes);
        Assert.Equal(1, document.PageCount);
    }

    // Upstream #357: a reference with an object number beyond what PDF allows stopped the whole file opening.
    [Fact]
    public void A_reference_with_a_huge_object_number_reads_as_null()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Pg 910491793 0 R >>",
        });

        using var document = Open(bytes);
        Assert.Equal(1, document.PageCount);
        Assert.Equal(240, document.Pages[0].MediaBox.Width, 3);
    }

    // Upstream #176: a code for the table entry being defined right now lost a byte.
    [Fact]
    public void Lzw_codes_for_the_entry_being_defined_decode_completely()
    {
        // "AAAAAAA" encodes as clear, 65, 258, 259, 65, end of data; 258 and 259 are each defined by their own use.
        var codes = new[] { 256, 65, 258, 259, 65, 257 };
        var bits = new StringBuilder();
        foreach (var code in codes)
            bits.Append(Convert.ToString(code, 2).PadLeft(9, '0'));
        while (bits.Length % 8 != 0)
            bits.Append('0');
        var data = Enumerable.Range(0, bits.Length / 8).Select(index => Convert.ToByte(bits.ToString(index * 8, 8), 2)).ToArray();
        // The fixture is ASCII, so the codes are hex encoded on top.
        var hex = string.Concat(data.Select(value => value.ToString("X2"))) + ">";

        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Contents 4 0 R >>",
            $"<< /Filter [/ASCIIHexDecode /LZWDecode] /Length {hex.Length} >>\nstream\n{hex}\nendstream",
        });

        using var document = Open(bytes);
        var content = document.Pages[0].Elements.GetDictionary("/Contents")!.Stream!.UnfilteredValue;
        Assert.Equal("AAAAAAA", Encoding.ASCII.GetString(content));
    }

    // Upstream PR #152: a stray ')' after a string stopped the whole file opening.
    [Fact]
    public void A_stray_closing_parenthesis_is_ignored()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Label (Page one)) /MediaBox [0 0 240 200] >>",
        });

        using var document = Open(bytes);
        Assert.Equal(1, document.PageCount);
        Assert.Equal("Page one", document.Pages[0].Elements.GetString("/Label"));
        Assert.Equal(240, document.Pages[0].MediaBox.Width, 3);
    }

    // Upstream #211: an object without 'endobj' stopped the whole file opening.
    [Fact]
    public void An_object_without_endobj_is_read()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
        });
        // Remove the 'endobj' of object 2; its cross-reference entries keep their offsets.
        var text = TestPdf.Latin1.GetString(bytes);
        var at = text.IndexOf("endobj", text.IndexOf("2 0 obj", StringComparison.Ordinal), StringComparison.Ordinal);
        text = text[..at] + "      " + text[(at + 6)..];

        using var document = Open(TestPdf.Latin1.GetBytes(text));
        Assert.Equal(1, document.PageCount);
    }

    // Upstream PR #365: a content name in a legacy code page did not match the same name as a resource key.
    [Theory]
    [InlineData("ja-JP", "/#83#65", "/テ")]
    [InlineData("en-US", "/#83#65", "/ƒe")]
    [InlineData("ja-JP", "/caf#C3#A9", "/café")]
    public void Content_names_decode_like_resource_keys(string culture, string literal, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var content = $"{literal} gs";
            var bytes = TestPdf.Create(new[] {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Resources << /ExtGState << {literal} << /CA 1 >> >> >> /Contents 4 0 R >>",
                $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            });

            using var document = Open(bytes);
            var page = document.Pages[0];
            var key = page.Resources.Elements.GetDictionary("/ExtGState")!.Elements.Keys.Single();
            var operation = ContentReader.ReadContent(page).OfType<COperator>().Single();
            var name = ((CName)operation.Operands[0]).Name;
            Assert.Equal(expected, key);
            Assert.Equal(expected, name);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
