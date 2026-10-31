// Original synthetic font and concurrency tests. MIT; see LICENSE.
using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FreePDF.RegressionTests;

static class TestFonts
{
    static TestFonts() => GlobalFontSettings.FontResolver = new SyntheticResolver();
    public static void Initialize() { }

    sealed class SyntheticResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("synthetic", bold, italic);
        public byte[] GetFont(string faceName)
        {
            using var input = typeof(TestFonts).Assembly.GetManifestResourceStream("FreePDF.RegressionTests.Assets.synthetic.ttf")!;
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}

public class FontCacheTests
{
    static readonly Func<Font, XFont> ConvertFont = (Func<Font, XFont>)Delegate.CreateDelegate(typeof(Func<Font, XFont>),
        typeof(DocumentRenderer).Assembly.GetType("MigraDoc.Rendering.FontHandler")!
            .GetMethod("FontToXFont", BindingFlags.Static | BindingFlags.NonPublic)!);

    public FontCacheTests() => TestFonts.Initialize();

    [Fact]
    public void Mutating_a_font_invalidates_the_cached_conversion()
    {
        var font = new Font("FreePDF Synthetic", 12);
        Assert.Equal(12, ConvertFont(font).Size);
        font.Size = 24;
        font.Bold = true;
        var converted = ConvertFont(font);
        Assert.Equal(24, converted.Size);
        Assert.True(converted.Bold);
    }

    [Fact]
    public void Parallel_conversions_never_return_another_documents_font()
    {
        Parallel.For(0, 8, worker =>
        {
            var size = 9 + worker * 3;
            var font = new Font("FreePDF Synthetic", size);
            for (var i = 0; i < 5000; i++)
                Assert.Equal(size, ConvertFont(font).Size);
        });
    }
}
