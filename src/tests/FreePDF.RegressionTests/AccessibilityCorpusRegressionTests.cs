// Minimized original reproducer for a Chromium corpus rendering difference.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using Xunit;

namespace FreePDF.RegressionTests;

public class AccessibilityCorpusRegressionTests
{
    [Fact]
    public void Round_trip_preserves_page_box_precision_used_by_content_transforms()
    {
        using var bytes = new MemoryStream();
        using (var pdf = new PdfDocument())
        {
            var page = pdf.AddPage();
            page.Elements.SetRectangle("/MediaBox", new PdfRectangle(new XPoint(0, 0), new XPoint(841.91998, 594.95996)));
            pdf.Save(bytes);
        }
        using var imported = PdfReader.Open(new MemoryStream(bytes.ToArray()), PdfDocumentOpenMode.Modify);
        using var resaved = new MemoryStream(); imported.Save(resaved);
        using var reopened = PdfReader.Open(new MemoryStream(resaved.ToArray()), PdfDocumentOpenMode.Import);
        var box = reopened.Pages[0].MediaBox;
        Assert.Equal(841.91998, box.X2, 7);
        Assert.Equal(594.95996, box.Y2, 7);
    }

    [Fact]
    public void Saved_real_numbers_keep_their_precision_within_the_single_precision_range()
    {
        using var bytes = new MemoryStream();
        using (var pdf = new PdfDocument())
        {
            pdf.AddPage();
            var probe = new PdfDictionary(pdf);
            probe.Elements["/Precise"] = new PdfReal(594.95996);
            probe.Elements["/Largest"] = new PdfReal(Single.MaxValue);
            probe.Elements["/Smallest"] = new PdfReal(Single.MinValue);
            pdf.Catalog.Elements["/SyntheticProbe"] = probe;
            pdf.Save(bytes);
        }
        var text = System.Text.Encoding.Latin1.GetString(bytes.ToArray());
        string Literal(string key) => System.Text.RegularExpressions.Regex.Match(text, key + @"\s*([^/>\s]+)").Groups[1].Value;
        Assert.Equal("594.95996", Literal("/Precise"));
        // ISO 32000-1 Annex C: readers may hold reals in single precision (about ±3.403e38).
        foreach (var key in new[] { "/Largest", "/Smallest" })
        {
            var literal = Literal(key);
            Assert.Matches(@"^-?[0-9]+(\.[0-9]+)?$", literal);
            var value = Double.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(value) <= Single.MaxValue, $"{key} was written as {literal}.");
        }
    }
}
