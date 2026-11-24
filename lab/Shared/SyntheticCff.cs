// Original synthetic CFF programs for the glyph check. See LICENSE.
namespace FreePDF.Lab.Fixtures;

/// <summary>
/// Writes the smallest CFF programs the glyph check reads (Adobe Technical Note #5176): the header, the Name, Top DICT,
/// String and Global Subr INDEXes, a charset, an optional encoding, a CharStrings INDEX of empty glyphs, a Private
/// DICT, and for CIDFonts an FDArray and an FDSelect. Glyph names are custom strings unless given as a standard string
/// number ("#34" is A).
/// </summary>
static class SyntheticCff
{
    /// <param name="names">The names of glyphs 1 and up; glyph 0 is .notdef.</param>
    /// <param name="codes">The custom encoding: the code of glyphs 1 and up, in order. Null uses
    /// <paramref name="encodingId"/>.</param>
    /// <param name="supplements">Extra codes for named glyphs (the format's high bit).</param>
    /// <param name="charsetId">A predefined charset (0 ISOAdobe, 1 Expert, 2 ExpertSubset) instead of a written one;
    /// <paramref name="glyphCount"/> then gives the glyph count.</param>
    /// <param name="fonts">The number of fonts in the Name and Top DICT INDEXes (the same font repeated).</param>
    internal static byte[] Named(string[] names, int[]? codes = null, (int Code, string Name)[]? supplements = null,
        int charsetFormat = 0, int encodingFormat = 0, int encodingId = 0, int? charsetId = null, int glyphCount = 0,
        int fonts = 1, int major = 1, bool syntheticBase = false)
    {
        var strings = new List<string>();
        int glyphs = charsetId == null ? names.Length + 1 : glyphCount;
        byte[]? charset = charsetId == null ? Charset(names.Select(n => Sid(n, strings)).ToList(), charsetFormat) : null;
        byte[]? encoding = null;
        if (codes != null)
        {
            var sups = supplements?.Select(s => (s.Code, Sid(s.Name, strings))).ToList();
            encoding = Encoding(codes, encodingFormat, sups);
        }
        return Assemble(major, fonts, strings, glyphs, charset, charsetId ?? 0, encoding, encodingId, syntheticBase, null);
    }

    /// <summary>A CIDFont: glyph 0 is CID 0 and glyphs 1 and up are <paramref name="cids"/>.</summary>
    internal static byte[] Cid(int[] cids, int charsetFormat = 0, int? charsetId = null)
    {
        var strings = new List<string>();
        byte[]? charset = charsetId == null ? Charset(cids.ToList(), charsetFormat) : null;
        return Assemble(1, 1, strings, cids.Length + 1, charset, charsetId ?? 0, null, 0, false, cids.ToList());
    }

    static int Sid(string name, List<string> strings)
    {
        if (name.StartsWith("#", StringComparison.Ordinal))
            return int.Parse(name.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
        int index = strings.IndexOf(name);
        if (index < 0)
        {
            index = strings.Count;
            strings.Add(name);
        }
        return 391 + index;
    }

    static byte[] Charset(List<int> values, int format) => Bytes(w =>
    {
        w.U8(format);
        if (format == 0)
        {
            foreach (var value in values) w.U16(value);
            return;
        }
        // Ranges of consecutive values; format 1 counts the rest of a range in one byte, format 2 in two.
        for (int i = 0; i < values.Count;)
        {
            int run = 0;
            while (i + run + 1 < values.Count && values[i + run + 1] == values[i] + run + 1 && (format == 2 || run < 255)) run++;
            w.U16(values[i]);
            if (format == 1) w.U8(run); else w.U16(run);
            i += run + 1;
        }
    });

    static byte[] Encoding(int[] codes, int format, List<(int Code, int Sid)>? supplements) => Bytes(w =>
    {
        w.U8(format | (supplements != null ? 0x80 : 0));
        if (format == 0)
        {
            w.U8(codes.Length);
            foreach (var code in codes) w.U8(code);
        }
        else
        {
            var ranges = new List<(int First, int Left)>();
            for (int i = 0; i < codes.Length;)
            {
                int run = 0;
                while (i + run + 1 < codes.Length && codes[i + run + 1] == codes[i] + run + 1) run++;
                ranges.Add((codes[i], run));
                i += run + 1;
            }
            w.U8(ranges.Count);
            foreach (var range in ranges) { w.U8(range.First); w.U8(range.Left); }
        }
        if (supplements == null) return;
        w.U8(supplements.Count);
        foreach (var supplement in supplements) { w.U8(supplement.Code); w.U16(supplement.Sid); }
    });

    static byte[] Assemble(int major, int fonts, List<string> strings, int glyphs, byte[]? charset, int charsetId,
        byte[]? encoding, int encodingId, bool syntheticBase, List<int>? cids)
    {
        if (cids != null)
        {
            strings.Add("Adobe");
            strings.Add("Identity");
        }
        var name = Index(Enumerable.Repeat(System.Text.Encoding.ASCII.GetBytes("Synthetic"), fonts));
        var stringIndex = Index(strings.Select(s => System.Text.Encoding.ASCII.GetBytes(s)));
        var globalSubrs = Index(Array.Empty<byte[]>());
        var charStrings = Index(Enumerable.Repeat(new byte[] { 14 }, glyphs));   // endchar
        var priv = Bytes(w => { Number(w, 0); w.U8(20); });                    // defaultWidthX 0
        var fdSelect = Bytes(w => { w.U8(0); for (int i = 0; i < glyphs; i++) w.U8(0); });
        byte[] TopDict(int charsetAt, int encodingAt, int charStringsAt, int privateAt, int fdArrayAt, int fdSelectAt) => Bytes(w =>
        {
            if (syntheticBase) { Number(w, 0); w.U8(12); w.U8(20); }
            if (cids != null)
            {
                Number(w, 391 + strings.Count - 2); Number(w, 391 + strings.Count - 1); Number(w, 0); w.U8(12); w.U8(30);
                Number(w, cids.Max() + 1); w.U8(12); w.U8(34);
            }
            Number(w, charsetAt); w.U8(15);
            if (cids == null) { Number(w, encodingAt); w.U8(16); }
            Number(w, charStringsAt); w.U8(17);
            if (cids == null) { Number(w, priv.Length); Number(w, privateAt); w.U8(18); }
            else { Number(w, fdArrayAt); w.U8(12); w.U8(36); Number(w, fdSelectAt); w.U8(12); w.U8(37); }
        });
        byte[] FontDict(int privateAt) => Bytes(w => { Number(w, priv.Length); Number(w, privateAt); w.U8(18); });
        bool fdArray = cids != null;
        // Every DICT number is written in five bytes, so the sizes are known before the offsets.
        int topLength = Index(Enumerable.Repeat(TopDict(0, 0, 0, 0, 0, 0), fonts)).Length;
        int at = 4 + name.Length + topLength + stringIndex.Length + globalSubrs.Length;
        int charsetAt = charset != null ? at : charsetId;
        at += charset?.Length ?? 0;
        int encodingAt = encoding != null ? at : encodingId;
        at += encoding?.Length ?? 0;
        int charStringsAt = at;
        at += charStrings.Length;
        int fdSelectAt = at;
        if (fdArray) at += fdSelect.Length;
        int fdArrayAt = at;
        if (fdArray) at += Index(new[] { FontDict(0) }).Length;
        int privateAt = at;
        var top = TopDict(charsetAt, encodingAt, charStringsAt, privateAt, fdArrayAt, fdSelectAt);
        return Bytes(w =>
        {
            w.U8(major); w.U8(0); w.U8(4); w.U8(4);
            w.Raw(name);
            w.Raw(Index(Enumerable.Repeat(top, fonts)));
            w.Raw(stringIndex);
            w.Raw(globalSubrs);
            if (charset != null) w.Raw(charset);
            if (encoding != null) w.Raw(encoding);
            w.Raw(charStrings);
            if (fdArray)
            {
                w.Raw(fdSelect);
                w.Raw(Index(new[] { FontDict(privateAt) }));
            }
            w.Raw(priv);
        });
    }

    static void Number(Writer w, int value) { w.U8(29); w.U32(value); }

    internal static byte[] Index(IEnumerable<byte[]> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return new byte[] { 0, 0 };
        return Bytes(w =>
        {
            w.U16(list.Count); w.U8(4);
            int offset = 1;
            w.U32(offset);
            foreach (var item in list) { offset += item.Length; w.U32(offset); }
            foreach (var item in list) w.Raw(item);
        });
    }

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
