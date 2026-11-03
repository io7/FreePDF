// The Core build's lookup of installed fonts when no custom font resolver is set. The fonts in Assets/SystemFonts are
// original (generate-system-fonts.py); they are added with AddFontDirectory, which is searched before the machine's own
// font folders, so the results do not depend on the fonts installed on the test machine. MIT; see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public sealed class SystemFontTests : IDisposable
{
    static readonly string FontFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "SystemFonts");

    public SystemFontTests() => GlobalFontSettings.ResetFontManagement();

    public void Dispose()
    {
        GlobalFontSettings.ResetFontManagement();
        TestFonts.Reinstall();
    }

    /// <summary>
    /// Draws text in the family and returns the base font names in the saved PDF: the face's full name, with ",Bold" or ",Italic" for a
    /// bold or italic face, as PDFsharp writes them.
    /// </summary>
    static List<string> EmbeddedFonts(string family, XFontStyleEx style)
    {
        var output = new MemoryStream();
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("FreePDF", new XFont(family, 12, style), XBrushes.Black, 50, 50);
            document.Save(output, false);
        }

        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var fonts = reopened.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        var names = new List<string>();
        foreach (var key in fonts.Elements.Keys)
        {
            var baseFont = fonts.Elements.GetDictionary(key)!.Elements.GetName("/BaseFont");
            // Strip the subset prefix (ABCDEF+).
            names.Add(baseFont.Substring(baseFont.IndexOf('+') + 1));
        }
        return names;
    }

    [Fact]
    public void An_installed_family_is_found_without_a_font_resolver()
    {
        GlobalFontSettings.AddFontDirectory(FontFolder);

        Assert.Equal(new[] { "FreePDF Probe Regular" }, EmbeddedFonts("FreePDF Probe", XFontStyleEx.Regular));
    }

    [Fact]
    public void The_bold_face_is_found_in_a_subfolder_and_used_for_bold_italic()
    {
        GlobalFontSettings.AddFontDirectory(FontFolder);

        Assert.Equal(new[] { "FreePDF Probe,Bold" }, EmbeddedFonts("FreePDF Probe", XFontStyleEx.Bold));
        Assert.Equal(new[] { "FreePDF Probe,Bold" }, EmbeddedFonts("freepdf probe", XFontStyleEx.BoldItalic));
    }

    [Fact]
    public void A_missing_common_family_uses_a_substitute_with_the_same_widths()
    {
        GlobalFontSettings.AddFontDirectory(FontFolder);

        Assert.Equal(new[] { "FreePDF Stand-in for Liberation Sans" }, EmbeddedFonts("Helvetica", XFontStyleEx.Regular));
    }

    [Fact]
    public void A_font_whose_licence_forbids_embedding_is_not_used_and_the_error_says_why()
    {
        GlobalFontSettings.AddFontDirectory(FontFolder);

        var refused = Assert.Throws<InvalidOperationException>(() => EmbeddedFonts("FreePDF Restricted", XFontStyleEx.Regular));
        Assert.Contains("licence does not allow embedding", refused.Message);
    }

    [Fact]
    public void Fonts_with_CFF_outlines_are_skipped()
    {
        GlobalFontSettings.AddFontDirectory(FontFolder);

        var refused = Assert.Throws<InvalidOperationException>(() => EmbeddedFonts("FreePDF Outline CFF", XFontStyleEx.Regular));
        Assert.Contains("No font named 'FreePDF Outline CFF' is installed", refused.Message);
        Assert.Contains("AddFontDirectory", refused.Message);
    }

    [Fact]
    public void Turning_system_fonts_off_requires_a_font_resolver_as_in_6_2_4()
    {
        GlobalFontSettings.UseSystemFonts = false;
        GlobalFontSettings.AddFontDirectory(FontFolder);

        var refused = Assert.Throws<InvalidOperationException>(() => EmbeddedFonts("FreePDF Probe", XFontStyleEx.Regular));
        Assert.Contains("Implement IFontResolver", refused.Message);
    }

    [Fact]
    public void A_custom_font_resolver_takes_precedence()
    {
        GlobalFontSettings.AddFontDirectory(FontFolder);
        TestFonts.Reinstall();

        Assert.Equal(new[] { "FreePDF Synthetic Regular" }, EmbeddedFonts("FreePDF Probe", XFontStyleEx.Regular));
    }

    [Fact]
    public void A_fallback_font_resolver_is_asked_before_the_installed_fonts()
    {
        GlobalFontSettings.AddFontDirectory(FontFolder);
        GlobalFontSettings.FallbackFontResolver = new ProbeToSyntheticResolver();

        Assert.Equal(new[] { "FreePDF Synthetic Regular" }, EmbeddedFonts("FreePDF Probe", XFontStyleEx.Regular));
    }

    sealed class ProbeToSyntheticResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            if (familyName == "FreePDF Probe")
                return new FontResolverInfo("synthetic-fallback");
            return null;
        }

        public byte[] GetFont(string faceName)
        {
            using var input = typeof(TestFonts).Assembly.GetManifestResourceStream("FreePDF.RegressionTests.Assets.synthetic.ttf")!;
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
