// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Text;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Maps a glyph name to Unicode text with the algorithm of the Adobe Glyph List specification, section 2
    /// (https://github.com/adobe-type-tools/agl-specification).
    /// </summary>
    static class GlyphNames
    {
        static Dictionary<string, string>? _adobe, _dingbats;

        static Dictionary<string, string> Adobe => _adobe ??= Parse(GlyphTables.AdobeGlyphList);

        static Dictionary<string, string> Dingbats => _dingbats ??= Parse(GlyphTables.ZapfDingbatsGlyphList);

        /// <summary>
        /// The Unicode text of a glyph name, or null if it maps to nothing. <paramref name="zapfDingbats"/> selects the
        /// ITC Zapf Dingbats list first, as the specification does for the font named ZapfDingbats.
        /// </summary>
        internal static string? ToUnicode(string name, bool zapfDingbats)
        {
            if (name.StartsWith("/", StringComparison.Ordinal))
                name = name.Substring(1);
            int period = name.IndexOf('.');
            if (period >= 0)
                name = name.Substring(0, period);
            if (name.Length == 0)
                return null;
            var text = new StringBuilder();
            foreach (var component in name.Split('_'))
                text.Append(Component(component, zapfDingbats));
            if (text.Length == 0)
                return null;
            return text.ToString();
        }

        static string Component(string component, bool zapfDingbats)
        {
            if (zapfDingbats && Dingbats.TryGetValue(component, out var dingbat))
                return dingbat;
            if (Adobe.TryGetValue(component, out var listed))
                return listed;
            if (component.Length >= 7 && (component.Length - 3) % 4 == 0 && component.StartsWith("uni", StringComparison.Ordinal))
            {
                var text = new StringBuilder();
                for (int i = 3; i < component.Length; i += 4)
                {
                    if (!UpperHex(component, i, 4, out int value) || (value >= 0xD800 && value <= 0xDFFF))
                        return "";
                    text.Append((char)value);
                }
                return text.ToString();
            }
            if (component.Length >= 5 && component.Length <= 7 && component[0] == 'u'
                && UpperHex(component, 1, component.Length - 1, out int scalar)
                && scalar <= 0x10FFFF && (scalar < 0xD800 || scalar > 0xDFFF))
                return Char.ConvertFromUtf32(scalar);
            return "";
        }

        // The specification allows only the digits 0-9 and the uppercase letters A-F.
        static bool UpperHex(string text, int start, int length, out int value)
        {
            value = 0;
            for (int i = start; i < start + length; i++)
            {
                char c = text[i];
                int digit;
                if (c >= '0' && c <= '9')
                    digit = c - '0';
                else if (c >= 'A' && c <= 'F')
                    digit = c - 'A' + 10;
                else
                    return false;
                value = value * 16 + digit;
            }
            return true;
        }

        static Dictionary<string, string> Parse(string list)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in list.Split('\n'))
            {
                var line = raw.Trim();
                int semicolon = line.IndexOf(';');
                if (semicolon <= 0)
                    continue;
                var text = new StringBuilder();
                foreach (var hex in line.Substring(semicolon + 1).Split(' '))
                {
                    if (hex.Length > 0)
                        text.Append((char)Convert.ToInt32(hex, 16));
                }
                map[line.Substring(0, semicolon)] = text.ToString();
            }
            return map;
        }
    }
}
