// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using PdfSharp.Pdf.Content.Text;

namespace PdfSharp.UniversalAccessibility
{
    /// <summary>
    /// The parts of an embedded CFF program (Adobe Technical Note #5176) that decide which glyph a code or CID selects:
    /// the glyph count from the CharStrings INDEX, the charset, and for fonts without CIDFont operators the glyph names
    /// and the built-in encoding. Every read is bounds-checked. A program the reader cannot use gives null.
    /// </summary>
    sealed class EmbeddedCff
    {
        const int StandardStringCount = 391;
        const int MaxNameLength = 255;

        readonly HashSet<string>? _names;
        readonly Dictionary<int, int>? _cids;

        EmbeddedCff(int glyphCount, HashSet<string>? names, string?[]? encoding, Dictionary<int, int>? cids)
        {
            GlyphCount = glyphCount;
            _names = names;
            BuiltInEncoding = encoding;
            _cids = cids;
        }

        internal int GlyphCount { get; }

        /// <summary>True when the Top DICT has CIDFont operators: the charset then holds CIDs, not names.</summary>
        internal bool CidKeyed => _cids != null;

        /// <summary>
        /// The glyph name of each code in the program's built-in encoding (null where it encodes no glyph), or null when
        /// the encoding is one the reader does not know (Expert) or the font is CID-keyed.
        /// </summary>
        internal string?[]? BuiltInEncoding { get; }

        /// <summary>True when a font without CIDFont operators has a glyph of this name.</summary>
        internal bool HasGlyph(string name) => _names != null && _names.Contains(name);

        /// <summary>The glyph that a CID-keyed font's charset gives the CID, or -1 when it has none.</summary>
        internal int GlyphOfCid(int cid) => _cids != null && _cids.TryGetValue(cid, out var glyph) ? glyph : -1;

        /// <summary>
        /// The program, or null when it is not a single readable CFF font, or when <paramref name="reserve"/> refuses an
        /// entry for each glyph, which the names or the CID table keep.
        /// </summary>
        internal static EmbeddedCff? Read(byte[] data, Action tick, System.Func<int, bool> reserve)
        {
            // Header: major version 1 (CFF2 is not read), header size, offset size.
            if (data.Length < 4 || data[0] != 1 || data[2] < 4) return null;
            var names = Index.Read(data, data[2]);
            if (names == null || names.Count != 1) return null;
            var tops = Index.Read(data, names.End);
            if (tops == null || tops.Count != 1) return null;
            var strings = Index.Read(data, tops.End);
            if (strings == null) return null;
            var top = TopDict.Read(data, tops.ItemStart(0), tops.ItemEnd(0), tick);
            if (top == null || top.SyntheticBase || top.CharStrings <= 0) return null;
            var charStrings = Index.Read(data, top.CharStrings);
            if (charStrings == null || charStrings.Count == 0 || !charStrings.InOrder(tick)) return null;
            int glyphs = charStrings.Count;
            if (!reserve(glyphs)) return null;
            var charset = Charset(data, top.Charset, glyphs, top.Cid, tick);
            if (charset == null) return null;
            if (top.Cid)
            {
                // GID 0 is CID 0; a CID listed twice keeps its first glyph.
                var cids = new Dictionary<int, int> { [0] = 0 };
                for (int glyph = 1; glyph < glyphs; glyph++)
                {
                    if (!cids.ContainsKey(charset[glyph])) cids.Add(charset[glyph], glyph);
                }
                return new EmbeddedCff(glyphs, null, null, cids);
            }
            // Each custom string is made once, however many glyphs share its SID. A name longer than 255 characters is
            // damage, and each name kept reserves one more entry for every 16 characters.
            var custom = new Dictionary<int, string?>();
            string? Name(int sid)
            {
                if (sid < StandardStringCount) return CffStandardStrings.Names[sid];
                if (custom.TryGetValue(sid, out var known)) return known;
                var text = strings.Text(sid - StandardStringCount, MaxNameLength);
                custom.Add(sid, text);
                return text;
            }
            var glyphNames = new string[glyphs];
            var set = new HashSet<string>(System.StringComparer.Ordinal);
            for (int glyph = 0; glyph < glyphs; glyph++)
            {
                tick();
                var name = Name(charset[glyph]);
                if (name == null) return null;
                glyphNames[glyph] = name;
                if (set.Add(name) && !reserve(name.Length / 16)) return null;
            }
            string?[]? encoding;
            if (!Encoding(data, top.Encoding, glyphNames, Name, tick, out encoding)) return null;
            return new EmbeddedCff(glyphs, set, encoding, null);
        }

        // The SID (or CID) of each glyph. Predefined charsets: ISOAdobe names glyph i with SID i; Expert and
        // ExpertSubset are not at hand, and CIDFonts must not use one.
        static int[]? Charset(byte[] data, int offset, int glyphs, bool cid, Action tick)
        {
            var values = new int[glyphs];
            if (offset <= 2)
            {
                if (cid || offset != 0 || glyphs > 229) return null;
                for (int glyph = 0; glyph < glyphs; glyph++) values[glyph] = glyph;
                return values;
            }
            if (offset >= data.Length) return null;
            int format = data[offset];
            int position = offset + 1;
            if (format == 0)
            {
                if (position + 2L * (glyphs - 1) > data.Length) return null;
                for (int glyph = 1; glyph < glyphs; glyph++)
                {
                    tick();
                    values[glyph] = U16(data, position + 2 * (glyph - 1));
                }
                return values;
            }
            if (format != 1 && format != 2) return null;
            int size = format == 1 ? 3 : 4;
            for (int glyph = 1; glyph < glyphs;)
            {
                tick();
                if (position + size > data.Length) return null;
                int first = U16(data, position);
                int left = format == 1 ? data[position + 2] : U16(data, position + 2);
                position += size;
                for (int i = 0; i <= left && glyph < glyphs; i++, glyph++) values[glyph] = first + i;
            }
            return values;
        }

        // The built-in encoding: predefined Standard Encoding (code to name), a custom one (code to glyph), or none known
        // (Expert). False for a damaged custom encoding.
        static bool Encoding(byte[] data, int offset, string[] glyphNames, System.Func<int, string?> name, Action tick,
            out string?[]? encoding)
        {
            encoding = null;
            if (offset == 1) return true;
            var table = new string?[256];
            if (offset == 0)
            {
                for (int code = 0; code < 256; code++)
                {
                    if (GlyphTables.StandardEncoding[code].Length > 0) table[code] = GlyphTables.StandardEncoding[code];
                }
                encoding = table;
                return true;
            }
            if (offset < 0 || offset >= data.Length) return false;
            int format = data[offset] & 0x7F;
            int position = offset + 1;
            if (position >= data.Length) return false;
            int count = data[position++];
            int glyph = 1;
            void Encode(int code)
            {
                if (glyph < glyphNames.Length && table[code] == null) table[code] = glyphNames[glyph];
                glyph++;
            }
            if (format == 0)
            {
                if (position + count > data.Length) return false;
                for (int i = 0; i < count; i++) Encode(data[position + i]);
                position += count;
            }
            else if (format == 1)
            {
                if (position + 2L * count > data.Length) return false;
                for (int i = 0; i < count; i++)
                {
                    tick();
                    int first = data[position + 2 * i];
                    int left = data[position + 2 * i + 1];
                    if (first + left > 255) return false;
                    for (int code = first; code <= first + left; code++) Encode(code);
                }
                position += 2 * count;
            }
            else return false;
            // A code for a glyph past the CharStrings INDEX is damage.
            if (glyph > glyphNames.Length) return false;
            if ((data[offset] & 0x80) != 0)
            {
                if (position >= data.Length) return false;
                int supplements = data[position++];
                if (position + 3L * supplements > data.Length) return false;
                for (int i = 0; i < supplements; i++)
                {
                    int code = data[position + 3 * i];
                    var supplement = name(U16(data, position + 3 * i + 1));
                    if (supplement == null) return false;
                    if (table[code] == null) table[code] = supplement;
                }
            }
            encoding = table;
            return true;
        }

        static int U16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

        /// <summary>A bounds-checked INDEX: its item count and where each item starts and ends.</summary>
        sealed class Index
        {
            readonly byte[] _data;
            readonly int _offsets;
            readonly int _offSize;
            readonly int _dataStart;

            Index(byte[] data, int count, int offsets, int offSize, int end)
            {
                _data = data;
                Count = count;
                _offsets = offsets;
                _offSize = offSize;
                _dataStart = offsets + (count + 1) * offSize - 1;
                End = end;
            }

            internal int Count { get; }

            /// <summary>The offset just past the INDEX.</summary>
            internal int End { get; }

            internal static Index? Read(byte[] data, int at)
            {
                if (at < 0 || at + 2L > data.Length) return null;
                int count = U16(data, at);
                if (count == 0) return new Index(data, 0, at + 2, 1, at + 2);
                if (at + 3L > data.Length) return null;
                int offSize = data[at + 2];
                if (offSize < 1 || offSize > 4) return null;
                long offsets = at + 3L;
                long dataStart = offsets + (count + 1L) * offSize - 1;
                if (dataStart >= data.Length) return null;
                var index = new Index(data, count, (int)offsets, offSize, 0);
                long last = index.Offset(count);
                if (last < 1 || dataStart + last > data.Length) return null;
                return new Index(data, count, (int)offsets, offSize, (int)(dataStart + last));
            }

            long Offset(int i)
            {
                long value = 0;
                for (int b = 0; b < _offSize; b++) value = (value << 8) | _data[_offsets + i * _offSize + b];
                return value;
            }

            /// <summary>The start of item i, or -1 when its offsets are out of order or past the INDEX.</summary>
            internal int ItemStart(int i)
            {
                long start = Offset(i), end = Offset(i + 1);
                return start < 1 || start > end || _dataStart + end > End ? -1 : (int)(_dataStart + start);
            }

            internal int ItemEnd(int i) => ItemStart(i) < 0 ? -1 : (int)(_dataStart + Offset(i + 1));

            /// <summary>True when every offset is at least 1 and none is smaller than the one before.</summary>
            internal bool InOrder(Action tick)
            {
                long previous = 1;
                for (int i = 0; i <= Count; i++)
                {
                    if ((i & 0xFFF) == 0) tick();
                    long offset = Offset(i);
                    if (offset < previous) return false;
                    previous = offset;
                }
                return true;
            }

            /// <summary>
            /// Item i as text, one character per byte, or null when it is out of range, damaged or longer than
            /// <paramref name="maxLength"/>.
            /// </summary>
            internal string? Text(int i, int maxLength)
            {
                if (i < 0 || i >= Count || ItemStart(i) < 0 || ItemEnd(i) - ItemStart(i) > maxLength) return null;
                int start = ItemStart(i);
                var chars = new char[ItemEnd(i) - start];
                for (int c = 0; c < chars.Length; c++) chars[c] = (char)_data[start + c];
                return new string(chars);
            }
        }

        /// <summary>The Top DICT operators the reader needs (TN 5176 Tables 9 and 10).</summary>
        sealed class TopDict
        {
            internal int Charset;
            internal int Encoding;
            internal int CharStrings;
            internal bool Cid;
            internal bool SyntheticBase;

            internal static TopDict? Read(byte[] data, int start, int end, Action tick)
            {
                if (start < 0 || end < start) return null;
                var top = new TopDict();
                var operands = new List<long>();
                for (int p = start; p < end;)
                {
                    tick();
                    int b0 = data[p];
                    if (b0 <= 21)
                    {
                        int op = b0;
                        p++;
                        if (b0 == 12)
                        {
                            if (p >= end) return null;
                            op = 1200 + data[p++];
                        }
                        long last = operands.Count > 0 ? operands[operands.Count - 1] : -1;
                        switch (op)
                        {
                            case 15: top.Charset = Offset(last); break;
                            case 16: top.Encoding = Offset(last); break;
                            case 17: top.CharStrings = Offset(last); break;
                            case 1230: top.Cid = true; break;
                            case 1220: top.SyntheticBase = true; break;
                        }
                        operands.Clear();
                        continue;
                    }
                    if (operands.Count >= 48) return null;
                    if (b0 == 28)
                    {
                        if (p + 3 > end) return null;
                        operands.Add((short)((data[p + 1] << 8) | data[p + 2]));
                        p += 3;
                    }
                    else if (b0 == 29)
                    {
                        if (p + 5 > end) return null;
                        operands.Add((int)(((uint)data[p + 1] << 24) | ((uint)data[p + 2] << 16) | ((uint)data[p + 3] << 8) | data[p + 4]));
                        p += 5;
                    }
                    else if (b0 == 30)
                    {
                        // A real number: nibbles up to the end nibble 0xf. Its value is not needed.
                        p++;
                        while (true)
                        {
                            if (p >= end) return null;
                            int b = data[p++];
                            if ((b & 0x0F) == 0x0F || (b >> 4) == 0x0F) break;
                        }
                        operands.Add(-1);
                    }
                    else if (b0 >= 32 && b0 <= 246)
                    {
                        operands.Add(b0 - 139);
                        p++;
                    }
                    else if (b0 >= 247 && b0 <= 254)
                    {
                        if (p + 2 > end) return null;
                        int b1 = data[p + 1];
                        operands.Add(b0 <= 250 ? (b0 - 247) * 256 + b1 + 108 : -(b0 - 251) * 256 - b1 - 108);
                        p += 2;
                    }
                    else return null;
                }
                return top;
            }

            // An offset operand: -1 for a missing or negative one, which no structure can be at.
            static int Offset(long value) => value < 0 || value > int.MaxValue ? -1 : (int)value;
        }
    }
}
