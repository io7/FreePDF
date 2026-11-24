// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections;

namespace PdfSharp.Pdf.Content.Text
{
    static partial class CMapParser
    {
        // Extraction tolerates incomplete CMaps. To prove a code is absent we must understand the
        // entire mapping program. Accept the declarative subset below and return null for the rest.
        internal static BitArray? ReadCoverage(byte[] data, int width, Action tick)
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
            var mapped = new BitArray(1 << (width * 8));
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
                            if (!ReadDestination(tokens.Next(), 0) || !tokens.LastStringWasHex || mapped[code]) return null;
                            mapped[code] = true;
                        }
                        if (!KeywordIs("]")) return null;
                    }
                    else
                    {
                        if (!ReadDestination(dest, high - low) || !tokens.LastStringWasHex) return null;
                        for (int code = low; code <= high; code++)
                        {
                            if (mapped[code]) return null;
                            mapped[code] = true;
                        }
                    }
                }
                if (!KeywordIs(range ? "endbfrange" : "endbfchar")) return null;
            }
            if (wrapped && !(KeywordIs("CMapName") && KeywordIs("currentdict") && NameIs("/CMap")
                && KeywordIs("defineresource") && KeywordIs("pop") && KeywordIs("end") && KeywordIs("end"))) return null;
            if (spaceLow < 0 || tokens.Next() != null || tokens.MalformedToken) return null;
            return mapped;
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

        // A range increments only the last byte of its UTF-16BE destination. Surrogate
        // boundaries coincide with byte boundaries, so validity holds throughout that range.
        static bool ReadDestination(object? token, int increment)
        {
            if (token is not byte[] bytes || bytes.Length == 0 || (bytes.Length & 1) != 0
                || increment > 255 - bytes[bytes.Length - 1]) return false;
            bool needLow = false;
            for (int i = 0; i < bytes.Length; i += 2)
            {
                char unit = (char)((bytes[i] << 8) | bytes[i + 1]);
                if (Char.IsLowSurrogate(unit) != needLow) return false;
                needLow = Char.IsHighSurrogate(unit);
            }
            return !needLow;
        }
    }
}
