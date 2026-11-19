# Fonts

`new XFont("Arial", 12)` names a font family and a size in points. FreePDF needs the font file to draw with
it. It embeds the characters you use (a subset of the font) in the PDF, so readers see the same letters on
any machine.

## Fonts installed on the machine

The portable package `io7.FreePDF` finds the fonts installed on Windows, Linux and macOS. When a common
family is missing, it uses one with the same character widths, so lines break at the same places:

| Requested | Used when missing |
| --- | --- |
| Arial, Helvetica | Liberation Sans or Arimo |
| Times New Roman | Liberation Serif or Tinos |
| Courier New | Liberation Mono or Cousine |
| Calibri | Carlito |
| Cambria | Caladea |

It logs a warning when it substitutes. Without these fonts it falls back to DejaVu or Noto fonts of the same
kind, whose widths differ.

A Linux container often has no fonts at all. In a Debian or Ubuntu image, install the Liberation fonts:

```
RUN apt-get update && apt-get install -y fonts-liberation
```

FreePDF uses single-font `.ttf` and `.otf` files with TrueType outlines whose licence allows embedding. It
skips font collections (`.ttc`) and `.otf` fonts with CFF outlines.

## Fonts you ship with the application

Add their folder at startup:

<!-- snippet: font-directory -->
```csharp
// Once, at startup, before the first XFont:
GlobalFontSettings.AddFontDirectory(Path.Combine(AppContext.BaseDirectory, "fonts"));

using var document = new PdfDocument();
var page = document.AddPage();
using (var gfx = XGraphics.FromPdfPage(page))
    gfx.DrawString("From the fonts folder", new XFont("Liberation Sans", 12), XBrushes.Black, 50, 50);
document.Save("font-directory.pdf");
```

FreePDF searches the folders you add before the system's font folders.

## A font resolver

The fonts on a machine can change, and the PDF changes with them. For the same PDF on every machine, serve
the font files yourself with an `IFontResolver`:

<!-- snippet: font-resolver-class -->
```csharp
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
```

<!-- snippet: font-resolver-use -->
```csharp
GlobalFontSettings.FontResolver = new AppFontResolver();   // once, before the first XFont

using var document = new PdfDocument();
var page = document.AddPage();
using (var gfx = XGraphics.FromPdfPage(page))
{
    gfx.DrawString("Regular", new XFont("Liberation Sans", 12), XBrushes.Black, 50, 50);
    gfx.DrawString("Bold", new XFont("Liberation Sans", 12, XFontStyleEx.Bold), XBrushes.Black, 50, 70);
}
document.Save("font-resolver.pdf");
```

`ResolveTypeface` turns a family name and style into a face name of your choice; `GetFont` returns the font
file for that face name. Set `GlobalFontSettings.FontResolver` once, before the first font is used; it cannot
be removed later.

## Characters

FreePDF draws every Unicode character that the font has a glyph for. It does not shape text, so scripts
that need shaping, such as Arabic or Devanagari, do not come out right.

## GDI and WPF packages

`io7.FreePDF.Gdi` and `io7.FreePDF.Wpf` use the fonts installed on Windows. `AddFontDirectory` throws
`NotSupportedException` there; use a font resolver for the fonts you ship.
