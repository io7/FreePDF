// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Pdf.Forms;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// One font dictionary as text extraction reads it: how to split a string into codes, the Unicode text of each
    /// code and its advance width in text space (ISO 32000-1 9.10.2 for the Unicode order, 9.2.4 and 9.7.4.3 for
    /// widths).
    /// </summary>
    sealed class TextFont
    {
        const string Replacement = "�";
        // An advance for codes without any known width, so that runs still move along the line.
        const double FallbackWidth = 0.5;

        sealed class CidWidth
        {
            internal int First, Last;
            internal double Width;
        }

        internal string Name { get; private set; } = "";
        bool _composite, _identity;
        CodeMap? _encoding, _toUnicode;
        string?[] _texts = new string?[256];
        readonly double[] _widths = new double[256];
        double _defaultWidth = 1;
        readonly Dictionary<int, double> _cidWidths = new();
        readonly List<CidWidth> _cidRanges = new();

        internal static TextFont Load(PdfDictionary font, TextBudget budget)
        {
            var result = new TextFont { Name = PostScriptName(TextItems.NameOf(font, "/BaseFont")) };
            if (TextItems.Resolve(font.Elements["/ToUnicode"]) is PdfDictionary { Stream: not null } toUnicode)
                result._toUnicode = CMapParser.Parse(budget.Decode(toUnicode));
            if (TextItems.NameOf(font, "/Subtype") == "/Type0")
                result.LoadComposite(font, budget);
            else
                result.LoadSimple(font);
            return result;
        }

        // "/ABCDEF+Helvetica" -> "Helvetica" (ISO 32000-1 9.6.4: a subset tag is six uppercase letters and '+').
        static string PostScriptName(string name)
        {
            if (name.StartsWith("/", StringComparison.Ordinal))
                name = name.Substring(1);
            if (name.Length > 7 && name[6] == '+' && name.Substring(0, 6).All(c => c >= 'A' && c <= 'Z'))
                name = name.Substring(7);
            return name;
        }

        /// <summary>
        /// Reads one code at <paramref name="offset"/> and returns its length in bytes (at least 1). The width is in
        /// text space units per unit of font size; <paramref name="wordSpace"/> is true for the single-byte code 32, the
        /// one code word spacing applies to (ISO 32000-1 9.3.3).
        /// </summary>
        internal int Read(byte[] bytes, int offset, out string text, out double width, out bool wordSpace)
        {
            if (!_composite)
            {
                int code = bytes[offset];
                text = _toUnicode?.Text(1, (uint)code) ?? _texts[code] ?? Replacement;
                width = _widths[code];
                wordSpace = code == 32;
                return 1;
            }
            int length = _encoding!.ReadCode(bytes, offset, out uint value);
            text = _toUnicode?.Text(length, value) ?? Replacement;
            int cid = (int)value;
            if (!_identity)
                cid = _encoding.Cid(length, value) ?? 0;
            width = WidthOf(cid);
            wordSpace = length == 1 && value == 32;
            return length;
        }

        void LoadSimple(PdfDictionary font)
        {
            double scale = 0.001;
            if (TextItems.NameOf(font, "/Subtype") == "/Type3")
            {
                var matrix = TextItems.Matrix(font.Elements["/FontMatrix"]);
                if (matrix != null)
                    scale = matrix.Value.A;
            }
            _texts = BaseEncoding(font);
            ApplyDifferences(font);

            var widths = TextItems.Resolve(font.Elements["/Widths"]) as PdfArray;
            int first = TextItems.Integer(font.Elements["/FirstChar"]) ?? 0;
            double? missing = TextItems.Number(TextItems.Dict(font, "/FontDescriptor")?.Elements["/MissingWidth"]) * scale;
            short[]? standard = null;
            if (widths == null)
                standard = StandardFontWidths.Get(Name);
            for (int code = 0; code < 256; code++)
            {
                double? width = null;
                if (widths != null)
                {
                    int index = code - first;
                    if (index >= 0 && index < widths.Elements.Count)
                        width = TextItems.Number(widths.Elements[index]) * scale;
                }
                else if (standard != null && code >= 32)
                {
                    width = standard[code - 32] / 1000.0;
                }
                _widths[code] = width ?? missing ?? FallbackWidth;
            }
        }

        string?[] BaseEncoding(PdfDictionary font)
        {
            var encoding = TextItems.Resolve(font.Elements["/Encoding"]);
            string? name = TextItems.NameValue(encoding);
            if (encoding is PdfDictionary dictionary)
                name = TextItems.NameOf(dictionary, "/BaseEncoding");
            string?[]? table = null;
            if (name != null)
                table = SimpleEncodings.Named(name);
            if (table != null)
                return table;
            // The font's built-in encoding. Only the standard fonts' are known without reading the font program.
            switch (Name)
            {
                case "Symbol":
                    return SimpleEncodings.Symbol();
                case "ZapfDingbats":
                    return SimpleEncodings.ZapfDingbats();
                default:
                    return SimpleEncodings.Standard();
            }
        }

        void ApplyDifferences(PdfDictionary font)
        {
            if (TextItems.Resolve(font.Elements["/Encoding"]) is not PdfDictionary encoding)
                return;
            if (TextItems.Resolve(encoding.Elements["/Differences"]) is not PdfArray differences)
                return;
            bool zapfDingbats = Name == "ZapfDingbats";
            int code = -1;
            foreach (var item in differences.Elements)
            {
                var number = TextItems.Integer(item);
                if (number.HasValue)
                {
                    code = number.Value;
                    continue;
                }
                var glyph = TextItems.NameValue(item);
                if (glyph == null)
                    continue;
                if (code >= 0 && code < 256)
                    _texts[code] = GlyphNames.ToUnicode(glyph, zapfDingbats);
                code++;
            }
        }

        void LoadComposite(PdfDictionary font, TextBudget budget)
        {
            _composite = true;
            var encoding = TextItems.Resolve(font.Elements["/Encoding"]);
            if (encoding is PdfDictionary { Stream: not null } stream)
            {
                _encoding = CMapParser.Parse(budget.Decode(stream));
            }
            else
            {
                var name = TextItems.NameValue(encoding);
                _identity = name == "/Identity-H" || name == "/Identity-V";
                _encoding = new CodeMap();
            }
            if (!_encoding.HasSpaces)
                _encoding.AddSpace(new byte[] { 0, 0 }, new byte[] { 0xFF, 0xFF });

            var descendant = Descendant(font);
            _defaultWidth = (TextItems.Number(descendant?.Elements["/DW"]) ?? 1000) / 1000;
            if (descendant != null && TextItems.Resolve(descendant.Elements["/W"]) is PdfArray widths)
                ReadWidths(widths);
        }

        static PdfDictionary? Descendant(PdfDictionary font)
        {
            if (TextItems.Resolve(font.Elements["/DescendantFonts"]) is not PdfArray fonts || fonts.Elements.Count == 0)
                return null;
            return TextItems.Resolve(fonts.Elements[0]) as PdfDictionary;
        }

        // ISO 32000-1 9.7.4.3: "c [w1 w2 ...]" or "cfirst clast w".
        void ReadWidths(PdfArray array)
        {
            var items = array.Elements;
            int i = 0;
            while (i < items.Count)
            {
                var first = TextItems.Integer(items[i]);
                if (first == null)
                {
                    i++;
                    continue;
                }
                if (i + 1 < items.Count && TextItems.Resolve(items[i + 1]) is PdfArray list)
                {
                    for (int k = 0; k < list.Elements.Count; k++)
                    {
                        var width = TextItems.Number(list.Elements[k]);
                        if (width.HasValue)
                            _cidWidths[first.Value + k] = width.Value / 1000;
                    }
                    i += 2;
                }
                else if (i + 2 < items.Count)
                {
                    var last = TextItems.Integer(items[i + 1]);
                    var width = TextItems.Number(items[i + 2]);
                    if (last.HasValue && width.HasValue && last.Value >= first.Value)
                        _cidRanges.Add(new CidWidth { First = first.Value, Last = last.Value, Width = width.Value / 1000 });
                    i += 3;
                }
                else
                {
                    break;
                }
            }
            _cidRanges.Sort((a, b) => a.First.CompareTo(b.First));
        }

        double WidthOf(int cid)
        {
            if (_cidWidths.TryGetValue(cid, out var width))
                return width;
            int low = 0, high = _cidRanges.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                var range = _cidRanges[middle];
                if (cid < range.First)
                    high = middle - 1;
                else if (cid > range.Last)
                    low = middle + 1;
                else
                    return range.Width;
            }
            return _defaultWidth;
        }
    }
}
