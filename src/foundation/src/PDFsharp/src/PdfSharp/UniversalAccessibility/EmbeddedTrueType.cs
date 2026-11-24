// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;

namespace PdfSharp.UniversalAccessibility
{
    /// <summary>
    /// The parts of an embedded TrueType program that decide which glyph a code selects: the glyph count from
    /// 'maxp', the 'cmap' subtables of formats 0, 4, 6 and 12, and the glyph names of a 'post' table of format 2
    /// (OpenType specification). Every read is bounds-checked against the table it belongs to. A program the reader
    /// cannot use gives null; a subtable it cannot read gives no answer for that subtable.
    /// </summary>
    sealed class EmbeddedTrueType
    {
        const int MaxTables = 512;

        readonly byte[]? _cmap;
        readonly byte[]? _post;
        readonly Action _tick;
        readonly Func<int, bool> _reserve;
        readonly Dictionary<(int Platform, int Encoding, int Code), int?> _lookups = new();
        readonly Dictionary<string, int> _customNames = new(System.StringComparer.Ordinal);
        bool _postRead, _hasStandardNames, _hasCustomNames;
        bool _postNamesComplete = true;

        EmbeddedTrueType(int glyphCount, byte[]? cmap, byte[]? post, Action tick, Func<int, bool> reserve)
        {
            GlyphCount = glyphCount;
            _cmap = cmap;
            _post = post;
            _tick = tick;
            _reserve = reserve;
            // A table shorter than its 32-byte header is present but unreadable (-1), unlike a missing one (0).
            if (post != null) PostFormat = post.Length >= 32 ? (int)U32(post, 0) : -1;
        }

        internal int GlyphCount { get; }

        /// <summary>
        /// The 'post' table version as a 16.16 number (0x00020000 for format 2), 0 without a table, -1 for a table too
        /// short to have one.
        /// </summary>
        internal int PostFormat { get; }

        /// <summary>
        /// The program, or null when it is not a single TrueType program with a 'maxp' table. The 'post' names it keeps
        /// are first reserved with <paramref name="reserve"/>; when that fails, the names count as unreadable.
        /// </summary>
        internal static EmbeddedTrueType? Read(byte[] data, Action tick, Func<int, bool> reserve)
        {
            if (data.Length < 12) return null;
            // 0x00010000 and 'true' mark TrueType outlines. 'OTTO' (CFF outlines) and 'ttcf' (collections) are not read.
            uint version = U32(data, 0);
            if (version != 0x00010000 && version != 0x74727565) return null;
            int tables = U16(data, 4);
            if (tables > MaxTables || 12L + 16L * tables > data.Length) return null;
            // The first record of each table counts, and each table is copied once.
            var found = new Dictionary<string, (long Offset, long Length)>(System.StringComparer.Ordinal);
            for (int i = 0; i < tables; i++)
            {
                tick();
                int record = 12 + 16 * i;
                long offset = U32(data, record + 8);
                long length = U32(data, record + 12);
                if (offset + length > data.Length) return null;
                var tag = Tag(data, record);
                if ((tag is "maxp" or "cmap" or "post") && !found.ContainsKey(tag)) found.Add(tag, (offset, length));
            }
            if (!found.TryGetValue("maxp", out var maxp) || maxp.Length < 6) return null;
            byte[]? cmap = null, post = null;
            if (found.TryGetValue("cmap", out var c)) cmap = Slice(data, c.Offset, c.Length);
            if (found.TryGetValue("post", out var p)) post = Slice(data, p.Offset, p.Length);
            return new EmbeddedTrueType(U16(data, (int)maxp.Offset + 4), cmap, post, tick, reserve);
        }

        /// <summary>True when the 'cmap' table has a subtable for this platform and encoding.</summary>
        internal bool HasSubtable(int platform, int encoding) => Subtable(platform, encoding) >= 0;

        /// <summary>
        /// The glyph that the subtable for this platform and encoding gives the code: 0 when it maps the code to no
        /// glyph, null when there is no such subtable or its format cannot be read.
        /// </summary>
        internal int? Lookup(int platform, int encoding, int code)
        {
            var key = (platform, encoding, code);
            if (_lookups.TryGetValue(key, out var known)) return known;
            int? glyph = null;
            int start = Subtable(platform, encoding);
            if (start >= 0)
            {
                // Each reader stays within the length the subtable declares (formats 8 and up keep it in 32 bits).
                int format = U16(_cmap!, start);
                long length = format >= 8 ? U32(_cmap!, start + 4) : U16(_cmap!, start + 2);
                if (start + length <= _cmap!.Length)
                {
                    int end = (int)(start + length);
                    switch (format)
                    {
                        case 0: glyph = Format0(start, end, code); break;
                        case 4: glyph = Format4(start, end, code); break;
                        case 6: glyph = Format6(start, end, code); break;
                        case 12: glyph = Format12(start, end, code); break;
                    }
                }
            }
            _lookups.Add(key, glyph);
            return glyph;
        }

        /// <summary>
        /// The glyph that a format 2 'post' table names with a name of its own (index 258 and above), or null.
        /// Names of the standard Macintosh set are not resolved.
        /// </summary>
        internal int? GlyphByCustomName(string name)
        {
            ReadPost();
            return _customNames.TryGetValue(name, out var glyph) ? glyph : null;
        }

        /// <summary>
        /// True when the 'post' table can name a glyph with a standard Macintosh name other than .notdef: format 1 or
        /// 2.5, or format 2 with an index from 1 to 257. Index 0 names .notdef, which unnamed glyphs commonly use.
        /// </summary>
        internal bool HasStandardNames
        {
            get { ReadPost(); return _hasStandardNames; }
        }

        /// <summary>True when a format 2 'post' table gives some glyph a name of its own.</summary>
        internal bool HasCustomNames
        {
            get { ReadPost(); return _hasCustomNames; }
        }

        /// <summary>
        /// False when the 'post' table has a format the reader does not know or a name it cannot read; a name it
        /// does not find may then still be in the table.
        /// </summary>
        internal bool PostNamesComplete
        {
            get { ReadPost(); return _postNamesComplete; }
        }

        void ReadPost()
        {
            if (_postRead) return;
            _postRead = true;
            switch (PostFormat)
            {
                case 0:
                case 0x00030000:
                    return;
                case 0x00010000:
                case 0x00025000:
                    _hasStandardNames = true;
                    return;
                case 0x00020000:
                    break;
                default:
                    _postNamesComplete = false;
                    return;
            }
            int count = U16(_post!, 32);
            long stringsStart = 34L + 2L * count;
            if (stringsStart > _post!.Length)
            {
                _postNamesComplete = false;
                return;
            }
            // Only the strings that a glyph index refers to are read: index 258 + i names string i.
            int needed = 0;
            for (int glyph = 0; glyph < count; glyph++)
            {
                _tick();
                int index = U16(_post, 34 + 2 * glyph);
                if (index > 0 && index < 258) _hasStandardNames = true;
                if (index >= 258) needed = Math.Max(needed, index - 257);
            }
            if (needed == 0) return;
            _hasCustomNames = true;
            if (!_reserve(needed))
            {
                _postNamesComplete = false;
                return;
            }
            var strings = new List<string>();
            for (long position = stringsStart; position < _post.Length && strings.Count < needed;)
            {
                _tick();
                int length = _post[position];
                if (position + 1 + length > _post.Length) break;
                strings.Add(System.Text.Encoding.ASCII.GetString(_post, (int)position + 1, length));
                position += 1 + length;
            }
            for (int glyph = 0; glyph < count; glyph++)
            {
                _tick();
                int index = U16(_post, 34 + 2 * glyph);
                if (index < 258) continue;
                if (index - 258 >= strings.Count) _postNamesComplete = false;
                else if (!_customNames.ContainsKey(strings[index - 258])) _customNames.Add(strings[index - 258], glyph);
            }
        }

        // The offset of the subtable for this platform and encoding within the 'cmap' table, or -1.
        int Subtable(int platform, int encoding)
        {
            if (_cmap == null || _cmap.Length < 4) return -1;
            int count = U16(_cmap, 2);
            for (int i = 0; i < count && 4 + 8 * i + 8 <= _cmap.Length; i++)
            {
                int record = 4 + 8 * i;
                if (U16(_cmap, record) != platform || U16(_cmap, record + 2) != encoding) continue;
                long offset = U32(_cmap, record + 4);
                return offset + 2 <= _cmap.Length ? (int)offset : -1;
            }
            return -1;
        }

        int? Format0(int start, int end, int code)
        {
            if (code < 0 || code > 255 || start + 6 + 256 > end) return null;
            return _cmap![start + 6 + code];
        }

        int? Format6(int start, int end, int code)
        {
            if (start + 10 > end) return null;
            int first = U16(_cmap!, start + 6);
            int count = U16(_cmap!, start + 8);
            if (start + 10L + 2L * count > end) return null;
            if (code < first || code >= first + count) return 0;
            return U16(_cmap!, start + 10 + 2 * (code - first));
        }

        // Segments are searched one by one: a font whose segments are out of order must not hide a mapping.
        int? Format4(int start, int end, int code)
        {
            if (start + 14 > end || code < 0 || code > 0xFFFF) return null;
            var cmap = _cmap!;
            int segments = U16(cmap, start + 6) / 2;
            int ends = start + 14;
            int starts = ends + 2 * segments + 2;
            int deltas = starts + 2 * segments;
            int rangeOffsets = deltas + 2 * segments;
            if (rangeOffsets + 2L * segments > end) return null;
            for (int i = 0; i < segments; i++)
            {
                _tick();
                if (code > U16(cmap, ends + 2 * i) || code < U16(cmap, starts + 2 * i)) continue;
                int delta = U16(cmap, deltas + 2 * i);
                int rangeOffset = U16(cmap, rangeOffsets + 2 * i);
                if (rangeOffset == 0) return (code + delta) & 0xFFFF;
                long address = rangeOffsets + 2L * i + rangeOffset + 2L * (code - U16(cmap, starts + 2 * i));
                if (address + 2 > end) return null;
                int glyph = U16(cmap, (int)address);
                return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
            }
            return 0;
        }

        int? Format12(int start, int end, int code)
        {
            if (start + 16 > end) return null;
            var cmap = _cmap!;
            long groups = U32(cmap, start + 12);
            if (start + 16L + 12L * groups > end) return null;
            for (long i = 0; i < groups; i++)
            {
                _tick();
                int group = start + 16 + (int)(12 * i);
                long first = U32(cmap, group);
                long last = U32(cmap, group + 4);
                if (code < first || code > last) continue;
                long glyph = U32(cmap, group + 8) + (code - first);
                return glyph > int.MaxValue ? null : (int)glyph;
            }
            return 0;
        }

        static byte[] Slice(byte[] data, long offset, long length)
        {
            var slice = new byte[length];
            System.Array.Copy(data, offset, slice, 0, length);
            return slice;
        }

        static string Tag(byte[] data, int offset)
            => new string(new[] { (char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3] });

        static int U16(byte[] data, int offset) => offset + 2 > data.Length ? 0 : (data[offset] << 8) | data[offset + 1];

        static uint U32(byte[] data, int offset)
            => offset + 4 > data.Length ? 0 : ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
