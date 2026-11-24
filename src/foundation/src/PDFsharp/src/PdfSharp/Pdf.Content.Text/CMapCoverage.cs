// Original FreePDF implementation. See LICENSE and PROVENANCE.md.

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// The character codes that a ToUnicode CMap maps, as sorted ranges that do not overlap, and the codes among them
    /// whose value contains U+0000, U+FEFF or U+FFFE (ISO 14289-1 7.21.7). Its size grows with the entries in the map,
    /// not with the code space, so one coverage can be kept for every map in a document.
    /// </summary>
    sealed class CMapCoverage
    {
        readonly Ranges _mapped;
        readonly Ranges _forbidden;

        CMapCoverage(Ranges mapped, Ranges forbidden)
        {
            _mapped = mapped;
            _forbidden = forbidden;
        }

        internal bool Contains(int code) => _mapped.Contains(code);

        internal bool MapsToForbiddenValue(int code) => _forbidden.Contains(code);

        // Null when two entries map the same code: the map then has no single meaning. Forbidden codes lie inside
        // their own entry's range, so they cannot overlap either.
        internal static CMapCoverage? Create(List<(int Low, int High)> mapped, List<(int Low, int High)> forbidden)
        {
            var ranges = Ranges.Create(mapped);
            return ranges == null ? null : new CMapCoverage(ranges, Ranges.Create(forbidden)!);
        }

        sealed class Ranges
        {
            readonly int[] _low;
            readonly int[] _high;

            Ranges(int[] low, int[] high)
            {
                _low = low;
                _high = high;
            }

            internal bool Contains(int code)
            {
                int index = Array.BinarySearch(_low, code);
                if (index >= 0) return true;
                // The complement is the first range that starts above the code. The range before it may contain it.
                index = ~index - 1;
                return index >= 0 && code <= _high[index];
            }

            // Null when two ranges overlap.
            internal static Ranges? Create(List<(int Low, int High)> ranges)
            {
                ranges.Sort((a, b) => a.Low.CompareTo(b.Low));
                var low = new int[ranges.Count];
                var high = new int[ranges.Count];
                for (int i = 0; i < ranges.Count; i++)
                {
                    if (i > 0 && ranges[i].Low <= high[i - 1]) return null;
                    low[i] = ranges[i].Low;
                    high[i] = ranges[i].High;
                }
                return new Ranges(low, high);
            }
        }
    }

    static partial class CMapParser
    {
        // Extraction tolerates incomplete CMaps. To prove a code is absent we must understand the
        // entire mapping program. Accept the declarative subset below and return null for the rest.
        internal static CMapCoverage? ReadCoverage(byte[] data, int width, Action tick)
        {
            if (data.Length > 256 * 1024) return null;
            var tokens = new Tokenizer(data);
            bool KeywordIs(string word) => tokens.Next() is Keyword k && k.Value == word;
            bool NameIs(string name) => tokens.Next() is Name n && n.Value == name;
            bool Code(out int value)
            {
                value = 0;
                if (tokens.Next() is not byte[] bytes || !tokens.LastStringWasHex || bytes.Length != width)
                    return false;
                value = (int)CodeMap.Value(bytes);
                return true;
            }
            var first = tokens.Next();
            bool wrapped = first is Name { Value: "/CIDInit" };
            if (wrapped)
            {
                if (!NameIs("/ProcSet") || !KeywordIs("findresource") || !KeywordIs("begin")
                    || tokens.Next() is not int || !KeywordIs("dict") || !KeywordIs("begin")) return null;
                first = tokens.Next();
            }
            if (first is not Keyword { Value: "begincmap" }) return null;
            var mapped = new List<(int Low, int High)>();
            var forbidden = new List<(int Low, int High)>();
            int spaceLow = -1, spaceHigh = -1;
            object? token;
            while ((token = tokens.Next()) is not Keyword { Value: "endcmap" })
            {
                tick();
                if (token is Name field)
                {
                    if (!ReadMetadata(tokens, field.Value) || !KeywordIs("def")) return null;
                    continue;
                }
                if (token is not int count || count < 0 || count > 100 || tokens.Next() is not Keyword section)
                    return null;
                if (section.Value == "begincodespacerange")
                {
                    if (spaceLow >= 0 || count != 1 || !Code(out spaceLow) || !Code(out spaceHigh)
                        || spaceLow > spaceHigh || !KeywordIs("endcodespacerange")) return null;
                    continue;
                }
                bool range = section.Value == "beginbfrange";
                if (spaceLow < 0 || (!range && section.Value != "beginbfchar")) return null;
                for (int i = 0; i < count; i++)
                {
                    tick();
                    if (!Code(out int low)) return null;
                    int high = low;
                    if (range && !Code(out high)) return null;
                    if (low < spaceLow || high > spaceHigh || low > high) return null;
                    var dest = tokens.Next();
                    if (range && dest is Keyword { Value: "[" })
                    {
                        for (int code = low; code <= high; code++)
                        {
                            tick();
                            if (!ReadDestination(tokens.Next(), code, 0, forbidden) || !tokens.LastStringWasHex) return null;
                        }
                        if (!KeywordIs("]")) return null;
                    }
                    else if (!ReadDestination(dest, low, high - low, forbidden) || !tokens.LastStringWasHex) return null;
                    mapped.Add((low, high));
                }
                if (!KeywordIs(range ? "endbfrange" : "endbfchar")) return null;
            }
            if (wrapped && !(KeywordIs("CMapName") && KeywordIs("currentdict") && NameIs("/CMap")
                && KeywordIs("defineresource") && KeywordIs("pop") && KeywordIs("end") && KeywordIs("end"))) return null;
            if (spaceLow < 0 || tokens.Next() != null || tokens.MalformedToken) return null;
            return CMapCoverage.Create(mapped, forbidden);
        }

        static bool ReadMetadata(Tokenizer tokens, string name)
        {
            if (name == "/CMapName") return tokens.Next() is Name;
            if (name == "/CMapType") return tokens.Next() is 2;
            if (name == "/WMode") return tokens.Next() is 0 or 1;
            if (name != "/CIDSystemInfo") return false;
            var start = tokens.Next();
            bool literal = start is Keyword { Value: "<<" };
            if (!literal && !(start is 3 && tokens.Next() is Keyword { Value: "dict" }
                && tokens.Next() is Keyword { Value: "dup" } && tokens.Next() is Keyword { Value: "begin" })) return false;
            for (int i = 0; i < 3; i++)
            {
                if (tokens.Next() is not Name entry) return false;
                var value = tokens.Next();
                if (!(entry.Value is "/Registry" or "/Ordering" && value is byte[]
                    || entry.Value == "/Supplement" && value is int)) return false;
                if (!literal && tokens.Next() is not Keyword { Value: "def" }) return false;
            }
            return tokens.Next() is Keyword end && end.Value == (literal ? ">>" : "end");
        }

        static readonly int[] ForbiddenUnits = { 0x0000, 0xFEFF, 0xFFFE };

        // A range increments only the last byte of its UTF-16BE destination. Surrogate
        // boundaries coincide with byte boundaries, so validity holds throughout that range.
        // The codes from low to low + increment whose value contains a forbidden unit go to forbidden.
        static bool ReadDestination(object? token, int low, int increment, List<(int Low, int High)> forbidden)
        {
            if (token is not byte[] bytes || bytes.Length == 0 || (bytes.Length & 1) != 0
                || increment > 255 - bytes[bytes.Length - 1]) return false;
            bool needLow = false;
            bool leadingForbidden = false;
            int last = 0;
            for (int i = 0; i < bytes.Length; i += 2)
            {
                char unit = (char)((bytes[i] << 8) | bytes[i + 1]);
                if (Char.IsLowSurrogate(unit) != needLow) return false;
                needLow = Char.IsHighSurrogate(unit);
                if (i + 2 < bytes.Length)
                    leadingForbidden |= unit is '\u0000' or '﻿' or '￾';
                else
                    last = unit;
            }
            if (needLow) return false;
            if (leadingForbidden)
            {
                forbidden.Add((low, low + increment));
                return true;
            }
            // Only the last unit changes across the range, and only in its low byte.
            foreach (int value in ForbiddenUnits)
            {
                int offset = value - last;
                if (offset >= 0 && offset <= increment)
                    forbidden.Add((low + offset, low + offset));
            }
            return true;
        }
    }
}
