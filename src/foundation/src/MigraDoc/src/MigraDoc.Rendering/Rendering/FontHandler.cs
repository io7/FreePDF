// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

#define CACHE_FONTS_

using PdfSharp.Drawing;
using MigraDoc.DocumentObjectModel;

// v7.0.0 REVIEW PSG

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Helps to measure and handle fonts.
    /// </summary>
    static class FontHandler
    {
#if DEBUG_
        internal static int CreateFontCounter;
#endif
        /// <summary>
        /// Converts a DOM Font to an XFont.
        /// </summary>
        internal static XFont FontToXFont(Font font)
        {
            var name = font.Name;
            var size = font.Size.Point;
            XFontStyleEx style = GetXStyle(font);
            // Read one immutable pair: separate static references can belong to different writers.
            var cached = System.Threading.Volatile.Read(ref _lastConversion);
            if (cached != null && cached.Name == name && cached.Size == size && cached.Style == style &&
                cached.Font.TryGetTarget(out var lastFont) && ReferenceEquals(font, lastFont) &&
                cached.XFont.TryGetTarget(out var lastXFont))
                return lastXFont;

#if DEBUG_
            if (StringComparer.OrdinalIgnoreCase.Compare(font.Name, "Segoe UI Semilight") == 0
                && (style & XFontStyleEx.BoldItalic) == XFontStyleEx.Italic)
                _ = typeof(int);
#endif
            var xFont = new XFont(name, size, style);
#if DEBUG_
            CreateFontCounter++;
#endif
            System.Threading.Volatile.Write(ref _lastConversion, new FontConversion(font, xFont, name, size, style));
#if FORCE_MEMORYLEAK
            _lastFont2 = font;
#endif
            return xFont;
        }

        static FontConversion? _lastConversion;

        sealed class FontConversion(Font font, XFont xFont, string name, double size, XFontStyleEx style)
        {
            internal readonly WeakReference<Font> Font = new(font);
            internal readonly WeakReference<XFont> XFont = new(xFont);
            internal readonly string Name = name;
            internal readonly double Size = size;
            internal readonly XFontStyleEx Style = style;
        }
#if FORCE_MEMORYLEAK
        static Font? _lastFont2;
#endif

        internal static XFontStyleEx GetXStyle(Font font)
        {
            XFontStyleEx style = XFontStyleEx.Regular;
            if (font.Bold)
                style = font.Italic ? XFontStyleEx.BoldItalic : XFontStyleEx.Bold;
            else if (font.Italic)
                style = XFontStyleEx.Italic;

            return style;
        }

        internal static XUnitPt GetDescent(XFont font)
        {
#if PSGFX
            var descent = (font.FontFace.Height - font.FontFace.Baseline) * font.Size;
            return new(descent);
#else
            XUnitPt descent = font.Metrics.Descent;
            descent *= font.Size;
            descent /= font.FontFamily.GetEmHeight(font.Style);
            return descent;
#endif
        }

        internal static XUnitPt GetAscent(XFont font)
        {
#if PSGFX
            var descent = font.FontFace.Baseline * font.Size;
            return new(descent);
#else
            XUnitPt ascent = font.Metrics.Ascent;
            ascent *= font.Size;
            ascent /= font.FontFamily.GetEmHeight(font.Style);
            return ascent;
#endif
        }

        internal static double GetSubSuperScaling(XFont font)
        {
#if PSGFX
            return 0.8 * font.Size;  // TODO #PSG
#else
            return 0.8 * GetAscent(font) / font.GetHeight();
#endif
        }

        internal static XFont ToSubSuperFont(XFont font)
        {
#if PSGFX
            double size = font.Size * GetSubSuperScaling(font);
            return new XFont(font.FontFace.FamilyName, size, font.Style/*, font.PdfOptions*/);  // TODO #PSG
#else
            double size = font.Size * GetSubSuperScaling(font);
            return new XFont(font.Name, size, font.Style, font.PdfOptions);
#endif
        }

        internal static XBrush FontColorToXBrush(Font font)
        {
#if PSGFX
            Debug.Assert(font.Document != null, "font.Document != null");
            return new XSolidBrush(ColorHelper.ToXColor(font.Color, font.Document.UseCmykColor));
#else
#if noCMYK
            return new XSolidBrush(XColor.FromArgb((int)font.Color.A, (int)font.Color.R, (int)font.Color.G, (int)font.Color.B));
#else
            Debug.Assert(font.Document != null, "font.Document != null");
            return new XSolidBrush(ColorHelper.ToXColor(font.Color, font.Document.UseCmykColor));
#endif
#endif
        }

#if CACHE_FONTS || true_
        static XFont XFontFromCache(Font font, bool unicode, PdfFontEmbedding fontEmbedding)
        {
            XFont xFont = null;

            XPdfFontOptions options = null;
            options = new XPdfFontOptions(fontEmbedding, unicode);
            XFontStyleEx style = GetXStyle(font);
            xFont = new XFont(font.Name, font.Size, style, options);

            return xFont;
        }

        static string BuildSignature(Font font, bool unicode, PdfFontEmbedding fontEmbedding)
        {
            StringBuilder signature = new StringBuilder(128);
            signature.Append(font.Name.ToLower());
            signature.Append(font.Size.Point.ToString("##0.0"));
            return signature.ToString();
        }

        static Hash_table fontCache = new Hash_table();
#endif
    }
}
