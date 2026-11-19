// Examples for docs/guide/fonts.md. The font file is the regression tests' generated stand-in (family
// "Liberation Sans", full name "FreePDF Stand-in for Liberation Sans", one glyph for all ASCII), copied to
// fonts/LiberationSans-Regular.ttf by the project file. Its glyphs say nothing, so the tests check which font the
// PDF embeds, not its text. MIT; see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.Samples;

public sealed class FontSamples : SampleTest
{
    const string StandIn = "FreePDF Stand-in for Liberation Sans";

    [Fact]
    public void FontDirectory()
    {
        // begin-snippet: font-directory
        // Once, at startup, before the first XFont:
        GlobalFontSettings.AddFontDirectory(Path.Combine(AppContext.BaseDirectory, "fonts"));

        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawString("From the fonts folder", new XFont("Liberation Sans", 12), XBrushes.Black, 50, 50);
        document.Save("font-directory.pdf");
        // end-snippet

        Assert.Equal(new[] { StandIn }, EmbeddedFonts("font-directory.pdf"));
    }

    [Fact]
    public void FontResolver()
    {
        // begin-snippet: font-resolver-use
        GlobalFontSettings.FontResolver = new AppFontResolver();   // once, before the first XFont

        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawString("Regular", new XFont("Liberation Sans", 12), XBrushes.Black, 50, 50);
            gfx.DrawString("Bold", new XFont("Liberation Sans", 12, XFontStyleEx.Bold), XBrushes.Black, 50, 70);
        }
        document.Save("font-resolver.pdf");
        // end-snippet

        var fonts = EmbeddedFonts("font-resolver.pdf");
        Assert.NotEmpty(fonts);
        Assert.All(fonts, name => Assert.StartsWith(StandIn, name));
    }
}

// begin-snippet: font-resolver-class
/// <summary>Serves the font files that ship with the application, so every machine makes the same PDF.</summary>
sealed class AppFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (!familyName.Equals("Liberation Sans", StringComparison.OrdinalIgnoreCase))
            return null;   // a family this application does not ship

        // Only the regular face ships here, so FreePDF simulates bold and italic.
        return new FontResolverInfo("LiberationSans-Regular", bold, italic);
    }

    public byte[]? GetFont(string faceName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", faceName + ".ttf"));
}
// end-snippet
