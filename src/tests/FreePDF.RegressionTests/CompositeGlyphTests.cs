// Original nested-component font and independent expected glyph set. MIT.
using PdfSharp.Internal.OpenType;
using Xunit;

namespace FreePDF.RegressionTests;

public class CompositeGlyphTests
{
    [Fact]
    public void Subsetting_keeps_nested_components_that_have_no_character_mapping()
    {
        using var resource = typeof(CompositeGlyphTests).Assembly.GetManifestResourceStream("FreePDF.RegressionTests.Assets.composite.ttf")!;
        using var data = new MemoryStream(); resource.CopyTo(data);
        var face = OpenTypeFontFace.GetOrCreateFrom(OpenTypeFontSource.GetOrCreateFrom(data.ToArray()));
        var glyphs = new Dictionary<ushort, object?> { [5] = null };
        face.glyf.CompleteGlyphClosure(glyphs);
        Assert.Equal(new ushort[] {0,2,3,4,5,6}, glyphs.Keys.OrderBy(g => g));
        var subset = face.CreateFontSubset(glyphs, true);
        foreach (var index in glyphs.Keys)
            Assert.True(subset.glyf.GetGlyphSize(index) > 0, "Missing glyph " + index);
    }
}
