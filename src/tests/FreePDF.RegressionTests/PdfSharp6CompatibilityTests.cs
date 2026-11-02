// PDFsharp 6.2.4 source-compatibility members found by the consumer survey
// (docs/research/consumer-survey). Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.PdfA;
using Xunit;

namespace FreePDF.RegressionTests;

public class PdfSharp6CompatibilityTests
{
    [Fact]
    public void GetDateTime_with_a_DateTime_default_returns_the_stored_date_as_UTC_like_6_2_4()
    {
        using var document = new PdfDocument();
        document.Info.Elements["/CreationDate"] = new PdfString("D:20240102030405+01'00'");

        DateTime value = document.Info.Elements.GetDateTime("/CreationDate", DateTime.MinValue);

        Assert.Equal(new DateTime(2024, 1, 2, 2, 4, 5), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Fact]
    public void GetDateTime_with_a_DateTime_default_returns_the_default_for_a_missing_entry()
    {
        using var document = new PdfDocument();
        var fallback = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        Assert.Equal(fallback, document.Info.Elements.GetDateTime("/NoSuchDate", fallback));
    }

    [Fact]
    public void GetDateTime_without_a_default_still_returns_the_v7_DateTimeOffset()
    {
        using var document = new PdfDocument();
        document.Info.Elements["/CreationDate"] = new PdfString("D:20240102030405Z");

        DateTimeOffset? value = document.Info.Elements.GetDateTime("/CreationDate");

        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), value);
    }

    // 6.2.4 code subclasses the content objects and fills them with object initializers.
    sealed class HexOperand : CString
    {
        public override string ToString() => "<" + Value + ">";
    }

    [Fact]
    public void Content_objects_have_their_6_2_4_parameterless_constructors()
    {
        var text = new HexOperand { Value = "414243", CStringType = CStringType.HexString };
        var plain = new CString();
        var integer = new CInteger { Value = 7 };
        var real = new CReal { Value = 1.5 };

        Assert.Equal("<414243>", text.ToString());
        Assert.Equal("", plain.Value);
        Assert.Equal(CStringType.String, plain.CStringType);
        Assert.Equal(7, integer.Value);
        Assert.Equal(1.5, real.Value);
    }

    [Fact]
    public void SetPdfA_without_arguments_declares_PDF_A_1b()
    {
        using var document = new PdfDocument();

        document.SetPdfA();
        document.AddPage();

        Assert.True(document.IsPdfA);
        Assert.Equal("1B", PdfAManager.ForDocument(document).Format.Name);
    }

    [Fact]
    public void SetPdfA_after_a_page_exists_is_refused_unlike_6_2_4()
    {
        using var document = new PdfDocument();
        document.AddPage();

        var refused = Assert.Throws<InvalidOperationException>(() => document.SetPdfA());
        Assert.Contains("before any PDF content is created", refused.Message);
    }

    [Fact]
    public void PdfA_2u_is_part_2_conformance_U()
    {
        Assert.Equal("2U", PdfAFormats.PdfA_2u.Name);
    }
}
