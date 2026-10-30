// Independent tests from published behavior requirements. MIT; see LICENSE.
using System.Globalization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class ReaderToleranceTests
{
    [Theory]
    [InlineData("(Free Key)", "/Free Key")]
    [InlineData("<467265654B6579>", "/FreeKey")]
    [InlineData("42", "/42")]
    [InlineData("-12", "/-12")]
    [InlineData("2147483648", "/2147483648")]
    public void String_and_integer_dictionary_keys_are_repaired_and_round_trip(string input, string key)
    {
        using var document = TestPdf.OpenCatalog(input + " (retained)");
        Assert.Equal("retained", document.Catalog.Elements.GetString(key));
        using var stream = new MemoryStream();
        document.Save(stream);
        stream.Position = 0;
        using var reopened = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        Assert.Equal("retained", reopened.Catalog.Elements.GetString(key));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[1]")]
    [InlineData("<< /A 1 >>")]
    public void Unsupported_dictionary_key_types_still_fail(string input)
    {
        Assert.Throws<PdfReaderException>(() => TestPdf.OpenCatalog(input + " 1"));
    }

    [Theory]
    [InlineData("en-US", "/caf#E9", "/café")]
    [InlineData("en-US", "/#80", "/€")]
    [InlineData("ja-JP", "/#82#A0", "/あ")]
    [InlineData("ja-JP", "/caf#C3#A9", "/café")]
    [InlineData("en-US", "/#EF#BF#BD", "/�")]
    public void Names_prefer_valid_utf8_then_fall_back_to_the_current_ansi_code_page(string culture, string literal, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var name = Name.FromLiteralName(literal);
            Assert.Equal(expected, name.Value);
            Assert.Equal(literal, name.LiteralValue);
            using var document = TestPdf.OpenCatalog(literal + " 123");
            Assert.Equal(123, document.Catalog.Elements.GetInteger(expected));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
