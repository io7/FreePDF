using FreePDF.Lab.Fixtures;
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.RegressionTests;

public class FixtureFontNamesTests
{
    [Fact]
    public void Random_tags_produce_identical_controlled_names()
    {
        using var first = new PdfDocument();
        using var second = new PdfDocument();
        Font(first, "/QRWPLT+Synthetic");
        Font(second, "/DFGHMU+Synthetic");
        Assert.Equal(FixtureFontNames.Normalize(first), FixtureFontNames.Normalize(second));
        Assert.Equal(new[] { "/AAAAAA+Synthetic" }, FixtureFontNames.Read(first));
    }

    [Fact]
    public void Distinct_subsets_and_shared_descriptors_keep_their_relationships()
    {
        using var document = new PdfDocument();
        var first = Font(document, "/QRWPLT+Synthetic");
        var second = Font(document, "/DFGHMU+Synthetic");
        var third = Font(document, "/QRWPLT+Synthetic");
        var descriptor = new PdfDictionary(document);
        descriptor.Elements.SetName("/Type", "/FontDescriptor");
        descriptor.Elements.SetName("/FontName", "/QRWPLT+Synthetic");
        document.Internals.AddObject(descriptor);
        first.Elements["/FontDescriptor"] = descriptor.Reference!;
        third.Elements["/FontDescriptor"] = descriptor.Reference!;
        var names = FixtureFontNames.Normalize(document);
        Assert.Equal(2, names.Length);
        Assert.NotEqual(first.Elements.GetName("/BaseFont"), second.Elements.GetName("/BaseFont"));
        Assert.Equal(first.Elements.GetName("/BaseFont"), third.Elements.GetName("/BaseFont"));
        Assert.Equal(first.Elements.GetName("/BaseFont"), descriptor.Elements.GetName("/FontName"));
        Assert.Equal(names, FixtureFontNames.Normalize(document));
    }

    [Fact]
    public void Unsubset_fonts_and_unrelated_dictionaries_are_untouched()
    {
        using var document = new PdfDocument();
        var font = Font(document, "/Helvetica");
        var other = new PdfDictionary(document);
        other.Elements.SetName("/BaseFont", "/ABCDEF+CustomValue");
        document.Internals.AddObject(other);
        Assert.Empty(FixtureFontNames.Normalize(document));
        Assert.Equal("/Helvetica", font.Elements.GetName("/BaseFont"));
        Assert.Equal("/ABCDEF+CustomValue", other.Elements.GetName("/BaseFont"));
    }

    static PdfDictionary Font(PdfDocument document, string name)
    {
        var font = new PdfDictionary(document);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/BaseFont", name);
        document.Internals.AddObject(font);
        return font;
    }
}
