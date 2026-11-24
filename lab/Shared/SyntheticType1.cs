// Original synthetic Type 1 programs for the glyph check. See LICENSE.
using System.Text;

namespace FreePDF.Lab.Fixtures;

/// <summary>
/// Writes the smallest Type 1 programs the glyph check reads (Adobe Type 1 Font Format): a clear-text part with the
/// built-in /Encoding, then an eexec-encrypted part with a Private dictionary, a Subrs array and a CharStrings
/// dictionary. The charstrings are opaque bytes; some spell PostScript tokens, which a reader must skip by length.
/// </summary>
static class SyntheticType1
{
    /// <param name="glyphs">The glyph names besides .notdef.</param>
    /// <param name="encoding">The built-in encoding: "StandardEncoding", another name, or "code name" pairs
    /// ("97 A 98 B") for a custom array.</param>
    /// <param name="hex">Writes the encrypted part as hexadecimal text instead of binary.</param>
    /// <param name="aliases">The names of the RD, ND and NP procedures.</param>
    /// <param name="cutTail">Characters cut from the end of the private text, to damage the program.</param>
    /// <param name="encrypt">False writes the private part in clear text, without eexec.</param>
    internal static byte[] Build(string[] glyphs, string encoding = "StandardEncoding", bool hex = false,
        (string Rd, string Nd, string Np)? aliases = null, int cutTail = 0, bool encrypt = true, int charStringLength = -1)
    {
        var (rd, nd, np) = aliases ?? ("RD", "ND", "NP");
        var clear = new StringBuilder();
        clear.Append("%!PS-AdobeFont-1.0: Synthetic 001.000\n");
        clear.Append("% A comment that mentions eexec and /Encoding must not count.\n");
        clear.Append("12 dict begin\n/FontName /Synthetic def\n/PaintType 0 def\n/FontType 1 def\n");
        clear.Append("/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n/FontBBox {0 0 1000 1000} readonly def\n");
        if (char.IsDigit(encoding[0]))
        {
            clear.Append("/Encoding 256 array\n0 1 255 {1 index exch /.notdef put} for\n");
            var parts = encoding.Split(' ');
            for (int i = 0; i < parts.Length; i += 2) clear.Append($"dup {parts[i]} /{parts[i + 1]} put\n");
            clear.Append("readonly def\n");
        }
        else clear.Append($"/Encoding {encoding} def\n");
        clear.Append("currentfile eexec\n");

        var priv = new MemoryStream();
        void Text(string s) { var b = Encoding.ASCII.GetBytes(s); priv.Write(b, 0, b.Length); }
        void Binary(byte[] b) => priv.Write(b, 0, b.Length);
        Text($"dup /Private 8 dict dup begin\n/{rd}{{string currentfile exch readstring pop}}executeonly def\n");
        Text($"/{nd}{{noaccess def}}executeonly def\n/{np}{{noaccess put}}executeonly def\n");
        Text("/BlueValues [-20 0 700 720] def\n/MinFeature {16 16} def\n/password 5839 def\n");
        Text("/OtherSubrs [ {} {} {(nested (text) here) pop} ] def\n");
        // Binary that spells tokens: a reader that tokenizes it would see /CharStrings, end and an open string.
        var trap = Encoding.ASCII.GetBytes("/CharStrings 9 dict dup begin /Z 1 RD x end (");
        Text("/Subrs 2 array\n");
        Text($"dup 0 {trap.Length} {rd} "); Binary(trap); Text($" {np}\n");
        Text($"dup 1 3 {rd} "); Binary(new byte[] { 0x0B, 0x20, 0x0D }); Text(" noaccess put\n");
        Text($"{nd}\n");
        Text($"2 index /CharStrings {glyphs.Length + 1} dict dup begin\n");
        foreach (var glyph in new[] { ".notdef" }.Concat(glyphs))
        {
            var body = Encoding.ASCII.GetBytes("end /x 5 RD (");
            int length = charStringLength < 0 ? body.Length : charStringLength;
            Text($"/{glyph} {length} {rd} "); Binary(body); Text($" {nd}\n");
        }
        Text("end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\nmark currentfile closefile\n");
        var plain = priv.ToArray();
        plain = plain.Take(plain.Length - cutTail).ToArray();

        var result = new MemoryStream();
        var head = Encoding.ASCII.GetBytes(encrypt ? clear.ToString() : clear.ToString().Replace("currentfile eexec\n", ""));
        result.Write(head, 0, head.Length);
        if (!encrypt)
        {
            result.Write(plain, 0, plain.Length);
            return result.ToArray();
        }
        var cipher = Encrypt(plain);
        if (hex)
        {
            var text = new StringBuilder();
            for (int i = 0; i < cipher.Length; i++)
            {
                text.Append(cipher[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                if (i % 32 == 31) text.Append('\n');
            }
            var bytes = Encoding.ASCII.GetBytes(text.ToString());
            result.Write(bytes, 0, bytes.Length);
        }
        else result.Write(cipher, 0, cipher.Length);
        var trailer = Encoding.ASCII.GetBytes("\n" + string.Concat(Enumerable.Repeat(new string('0', 64) + "\n", 8)) + "cleartomark\n");
        result.Write(trailer, 0, trailer.Length);
        return result.ToArray();
    }

    // eexec encryption (Type 1 Font Format, chapter 7): key 55665, four leading bytes. They are chosen so that the first
    // ciphertext byte is not white space and not all four are hexadecimal digits, as the format requires.
    static byte[] Encrypt(byte[] plain)
    {
        for (int seed = 0; ; seed++)
        {
            var input = new byte[4 + plain.Length];
            for (int i = 0; i < 4; i++) input[i] = (byte)(seed * 37 + i * 101);
            Array.Copy(plain, 0, input, 4, plain.Length);
            var cipher = new byte[input.Length];
            int r = 55665;
            for (int i = 0; i < input.Length; i++)
            {
                int c = input[i] ^ (r >> 8);
                cipher[i] = (byte)c;
                r = ((c + r) * 52845 + 22719) & 0xFFFF;
            }
            bool whiteFirst = cipher[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
            bool allHex = cipher.Take(4).All(b => Uri.IsHexDigit((char)b));
            if (!whiteFirst && !allHex) return cipher;
        }
    }
}
