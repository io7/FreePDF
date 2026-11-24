// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// The Unicode text of each code of a simple font's base or built-in encoding (ISO 32000-1 9.6.6 and Annex D).
    /// Every call returns a new table, which the caller may change with /Differences.
    /// </summary>
    static class SimpleEncodings
    {
        /// <summary>The table of a named base encoding, or null if the name is not one this reader knows.</summary>
        internal static string?[]? Named(string name)
        {
            switch (name)
            {
                case "/StandardEncoding":
                    return Standard();
                case "/WinAnsiEncoding":
                    return FromChars(GlyphTables.WinAnsiEncoding);
                case "/MacRomanEncoding":
                    return FromChars(GlyphTables.MacRomanEncoding);
                default:
                    return null;
            }
        }

        /// <summary>
        /// The glyph names of a named base encoding by code ("" where it names no glyph), or null if the name is not one
        /// this reader knows. The table is shared and must not be changed.
        /// </summary>
        internal static string[]? Names(string name)
        {
            switch (name)
            {
                case "/StandardEncoding":
                    return GlyphTables.StandardEncoding;
                case "/WinAnsiEncoding":
                    return GlyphTables.WinAnsiNames;
                case "/MacRomanEncoding":
                    return GlyphTables.MacRomanNames;
                default:
                    return null;
            }
        }

        internal static string?[] Standard() => FromNames(GlyphTables.StandardEncoding, false);

        internal static string?[] Symbol() => FromNames(GlyphTables.SymbolEncoding, false);

        internal static string?[] ZapfDingbats() => FromNames(GlyphTables.ZapfDingbatsEncoding, true);

        static string?[] FromNames(string[] names, bool zapfDingbats)
        {
            var table = new string?[256];
            for (int code = 0; code < 256; code++)
            {
                if (names[code].Length > 0)
                    table[code] = GlyphNames.ToUnicode(names[code], zapfDingbats);
            }
            return table;
        }

        static string?[] FromChars(string chars)
        {
            var table = new string?[256];
            for (int code = 0; code < 256; code++)
            {
                if (chars[code] != '�')
                    table[code] = chars[code].ToString();
            }
            return table;
        }
    }
}
