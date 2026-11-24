// Original synthetic TrueType programs for the glyph check. See LICENSE.
using System.Text;

namespace FreePDF.RegressionTests;

/// <summary>
/// Writes the smallest sfnt the glyph check reads: maxp, optional cmap subtables and an optional post table. The
/// tables have no outlines; the check only reads how codes select glyphs (OpenType specification: the table
/// directory, 'maxp', 'cmap' formats 0, 4, 6 and 12, 'post' formats 2 and 3).
/// </summary>
static class SyntheticTrueType
{
    internal sealed record Cmap(int Platform, int Encoding, int Format, IReadOnlyDictionary<int, int> Map);

    /// <param name="postNames">Format 2 names by glyph index; a name of the standard Macintosh set must be given as
    /// its standard index ("#3"), any other text becomes a custom name. Null writes a format 3 table, and
    /// <paramref name="post"/> false writes none.</param>
    internal static byte[] Build(int glyphs, IEnumerable<Cmap>? cmaps = null, string?[]? postNames = null, bool post = true,
        string tag = "\0\u0001\0\0")
    {
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["maxp"] = Bytes(w => { w.U32(0x00005000); w.U16(glyphs); })
        };
        if (cmaps != null) tables["cmap"] = CmapTable(cmaps.ToList());
        if (post) tables["post"] = PostTable(postNames, glyphs);
        return Bytes(w =>
        {
            foreach (char c in tag) w.U8(c);
            w.U16(tables.Count); w.U16(0); w.U16(0); w.U16(0);
            int offset = 12 + 16 * tables.Count;
            foreach (var table in tables)
            {
                foreach (char c in table.Key) w.U8(c);
                w.U32(0); w.U32(offset); w.U32(table.Value.Length);
                offset += (table.Value.Length + 3) & ~3;
            }
            foreach (var table in tables)
            {
                w.Raw(table.Value);
                for (int pad = table.Value.Length; (pad & 3) != 0; pad++) w.U8(0);
            }
        });
    }

    static byte[] CmapTable(List<Cmap> cmaps)
    {
        var subtables = cmaps.Select(Subtable).ToList();
        return Bytes(w =>
        {
            w.U16(0); w.U16(cmaps.Count);
            int offset = 4 + 8 * cmaps.Count;
            for (int i = 0; i < cmaps.Count; i++)
            {
                w.U16(cmaps[i].Platform); w.U16(cmaps[i].Encoding); w.U32(offset);
                offset += subtables[i].Length;
            }
            foreach (var subtable in subtables) w.Raw(subtable);
        });
    }

    static byte[] Subtable(Cmap cmap)
    {
        var map = cmap.Map.OrderBy(p => p.Key).ToList();
        switch (cmap.Format)
        {
            case 0:
                return Bytes(w =>
                {
                    w.U16(0); w.U16(262); w.U16(0);
                    for (int code = 0; code < 256; code++) w.U8(cmap.Map.TryGetValue(code, out var g) ? g : 0);
                });
            case 6:
            {
                int first = map.Count == 0 ? 0 : map[0].Key;
                int count = map.Count == 0 ? 0 : map[map.Count - 1].Key - first + 1;
                return Bytes(w =>
                {
                    w.U16(6); w.U16(10 + 2 * count); w.U16(0); w.U16(first); w.U16(count);
                    for (int code = first; code < first + count; code++) w.U16(cmap.Map.TryGetValue(code, out var g) ? g : 0);
                });
            }
            case 4:
            {
                // One segment per code, then the required final 0xFFFF segment. idDelta maps each code to its glyph.
                var segments = map.Select(p => (Start: p.Key, End: p.Key, Delta: (p.Value - p.Key) & 0xFFFF)).ToList();
                segments.Add((0xFFFF, 0xFFFF, 1));
                int n = segments.Count;
                return Bytes(w =>
                {
                    w.U16(4); w.U16(16 + 8 * n); w.U16(0); w.U16(2 * n); w.U16(0); w.U16(0); w.U16(0);
                    foreach (var s in segments) w.U16(s.End);
                    w.U16(0);
                    foreach (var s in segments) w.U16(s.Start);
                    foreach (var s in segments) w.U16(s.Delta);
                    foreach (var _ in segments) w.U16(0);
                });
            }
            case 41:
            {
                // Format 4 with one segment over the whole code range whose glyphs come from the glyph index array
                // (idRangeOffset), then the final segment.
                int first = map[0].Key;
                int last = map[map.Count - 1].Key;
                var glyphIds = Enumerable.Range(first, last - first + 1).Select(c => cmap.Map.TryGetValue(c, out var g) ? g : 0).ToList();
                return Bytes(w =>
                {
                    w.U16(4); w.U16(16 + 8 * 2 + 2 * glyphIds.Count); w.U16(0); w.U16(4); w.U16(0); w.U16(0); w.U16(0);
                    w.U16(last); w.U16(0xFFFF); w.U16(0);
                    w.U16(first); w.U16(0xFFFF);
                    w.U16(0); w.U16(1);
                    w.U16(4); w.U16(0);   // From the first idRangeOffset entry, 4 bytes on is the array.
                    foreach (var glyph in glyphIds) w.U16(glyph);
                });
            }
            case 12:
                return Bytes(w =>
                {
                    w.U16(12); w.U16(0); w.U32(16 + 12 * map.Count); w.U32(0); w.U32(map.Count);
                    foreach (var p in map) { w.U32(p.Key); w.U32(p.Key); w.U32(p.Value); }
                });
            default:
                // A header of a format the check does not read.
                return Bytes(w => { w.U16(cmap.Format); w.U16(6); w.U16(0); });
        }
    }

    static byte[] PostTable(string?[]? names, int glyphs) => Bytes(w =>
    {
        w.U32(names == null ? 0x00030000 : 0x00020000);
        for (int i = 0; i < 7; i++) w.U32(0);
        if (names == null) return;
        w.U16(glyphs);
        var custom = new List<string>();
        for (int glyph = 0; glyph < glyphs; glyph++)
        {
            var name = glyph < names.Length ? names[glyph] : null;
            if (name == null) w.U16(0);
            else if (name.StartsWith("#", StringComparison.Ordinal))
                w.U16(int.Parse(name.Substring(1), System.Globalization.CultureInfo.InvariantCulture));
            else { w.U16(258 + custom.Count); custom.Add(name); }
        }
        foreach (var name in custom) { w.U8(name.Length); w.Raw(Encoding.ASCII.GetBytes(name)); }
    });

    sealed class Writer
    {
        internal readonly MemoryStream Data = new();
        internal void U8(int v) => Data.WriteByte((byte)v);
        internal void U16(int v) { U8(v >> 8); U8(v); }
        internal void U32(long v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }
        internal void Raw(byte[] bytes) => Data.Write(bytes, 0, bytes.Length);
    }

    static byte[] Bytes(Action<Writer> write)
    {
        var writer = new Writer();
        write(writer);
        return writer.Data.ToArray();
    }
}
