// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using PdfSharp.Pdf.Content.Text;

namespace PdfSharp.UniversalAccessibility
{
    /// <summary>
    /// The parts of an embedded Type 1 program (Adobe Type 1 Font Format) that decide which glyph a code selects: the
    /// built-in /Encoding of the clear-text part and the glyph names of the CharStrings dictionary in the eexec-encrypted
    /// part. Charstrings and subroutines are skipped by their byte counts, never read as text. A program the reader
    /// cannot use gives null.
    /// </summary>
    sealed class EmbeddedType1
    {
        const int MaxGlyphs = 65536;
        const int MaxNameLength = 255;
        // Longer names and numbers are not in real programs; the lexer ends at one, so no huge text is made.
        const int MaxTokenLength = 1024;

        readonly HashSet<string> _glyphs;

        EmbeddedType1(HashSet<string> glyphs, string?[]? encoding)
        {
            _glyphs = glyphs;
            BuiltInEncoding = encoding;
        }

        /// <summary>
        /// The glyph name of each code in the built-in encoding (null where it encodes .notdef), or null when the
        /// encoding is not StandardEncoding or an array the reader can follow.
        /// </summary>
        internal string?[]? BuiltInEncoding { get; }

        internal bool HasGlyph(string name) => _glyphs.Contains(name);

        /// <summary>
        /// The program, or null when the reader cannot use it, when it names more than 65,536 glyphs, or when
        /// <paramref name="reserve"/> refuses an entry for a glyph name it keeps.
        /// </summary>
        internal static EmbeddedType1? Read(byte[] data, Action tick, System.Func<int, bool> reserve)
        {
            // A PDF Type 1 program is the font file itself: "%!", not the segment headers of a PFB file.
            if (data.Length < 2 || data[0] != '%' || data[1] != '!') return null;
            var clear = new Lexer(data, 0, data.Length, tick);
            string?[]? encoding = null;
            bool encodingSeen = false;
            int encrypted = -1;
            for (var token = clear.Next(); token != null; token = clear.Next())
            {
                if (token == "eexec")
                {
                    encrypted = clear.Position;
                    break;
                }
                if (token == "/Encoding" && !encodingSeen)
                {
                    encodingSeen = true;
                    encoding = Encoding(clear, reserve);
                }
            }
            if (encrypted < 0) return null;
            // The ciphertext starts after the white space that ends "eexec"; its first byte is not a blank, tab, carriage
            // return or line feed (Type 1 Font Format, section 7.2).
            while (encrypted < data.Length && data[encrypted] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') encrypted++;
            var plain = Decrypt(data, encrypted, tick);
            if (plain == null) return null;
            var glyphs = CharStrings(plain, tick, reserve);
            return glyphs == null ? null : new EmbeddedType1(glyphs, encoding);
        }

        // Only the forms the reader follows completely: "/Encoding StandardEncoding def", or "/Encoding 256 array",
        // optionally filled by "0 1 255 {1 index exch /.notdef put} for", then "dup code /name put" entries, then "def"
        // (each def may follow readonly or noaccess). Null for any other form: another named encoding, a changed copy,
        // or an operation the reader does not know, and when reserve refuses an entry for a name it would keep.
        static string?[]? Encoding(Lexer lexer, System.Func<int, bool> reserve)
        {
            var first = lexer.Next();
            if (first == "StandardEncoding")
            {
                if (!EndsDefinition(lexer)) return null;
                var standard = new string?[256];
                for (int code = 0; code < 256; code++)
                {
                    if (GlyphTables.StandardEncoding[code].Length > 0) standard[code] = GlyphTables.StandardEncoding[code];
                }
                return standard;
            }
            if (Integer(first) != 256 || lexer.Next() != "array") return null;
            var table = new string?[256];
            if (lexer.Peek() == "0")
            {
                foreach (var expected in new[] { "0", "1", "255", "{", "1", "index", "exch", "/.notdef", "put", "}", "for" })
                {
                    if (lexer.Next() != expected) return null;
                }
            }
            while (lexer.Peek() == "dup")
            {
                lexer.Next();
                var code = Integer(lexer.Next());
                var name = lexer.Next();
                if (code is not (>= 0 and < 256) || name == null || !name.StartsWith("/", System.StringComparison.Ordinal)) return null;
                if (lexer.Next() != "put") return null;
                // Each name kept reserves its entries, as glyph names do.
                if (name.Length - 1 > MaxNameLength || !reserve(1 + (name.Length - 1) / 16)) return null;
                table[code.Value] = name == "/.notdef" ? null : name.Substring(1);
            }
            return EndsDefinition(lexer) ? table : null;
        }

        static bool EndsDefinition(Lexer lexer)
        {
            var token = lexer.Next();
            if (token is "readonly" or "noaccess") token = lexer.Next();
            return token == "def";
        }

        // eexec decryption (Type 1 Font Format, chapter 7): key 55665, the first four plaintext bytes dropped. The
        // ciphertext is hexadecimal when its first four bytes are hexadecimal digits, else binary. Hexadecimal text ends
        // at the first character that is neither a digit nor white space.
        static byte[]? Decrypt(byte[] data, int start, Action tick)
        {
            if (start + 4 > data.Length) return null;
            bool hex = true;
            for (int i = 0; i < 4; i++) hex &= HexValue(data[start + i]) >= 0;
            var cipher = data;
            int length = data.Length - start;
            if (hex)
            {
                cipher = new byte[length / 2];
                length = 0;
                int high = -1;
                for (int i = start; i < data.Length; i++)
                {
                    int value = HexValue(data[i]);
                    if (value < 0)
                    {
                        if (IsWhite(data[i])) continue;
                        break;
                    }
                    if (high < 0) high = value;
                    else
                    {
                        cipher[length++] = (byte)((high << 4) | value);
                        high = -1;
                    }
                }
                start = 0;
            }
            if (length <= 4) return null;
            var plain = new byte[length - 4];
            int r = 55665;
            for (int i = 0; i < length; i++)
            {
                if ((i & 0xFFF) == 0) tick();
                int c = cipher[start + i];
                if (i >= 4) plain[i - 4] = (byte)(c ^ (r >> 8));
                r = ((c + r) * 52845 + 22719) & 0xFFFF;
            }
            return plain;
        }

        // The keys of the CharStrings dictionary. Each Subrs entry is "dup index count RD <count bytes> NP" and each
        // CharStrings entry "/name count RD <count bytes> ND", with one blank between RD and the bytes; RD, ND and NP
        // may have other names (Type 1 Font Format, section 2.4). Null unless the dictionary's end is reached.
        static HashSet<string>? CharStrings(byte[] plain, Action tick, System.Func<int, bool> reserve)
        {
            var lexer = new Lexer(plain, 0, plain.Length, tick);
            for (var token = lexer.Next(); token != null; token = lexer.Next())
            {
                if (token == "/Subrs")
                {
                    if (Integer(lexer.Next()) == null || lexer.Next() != "array") return null;
                    while (lexer.Peek() is "dup")
                    {
                        lexer.Next();
                        if (Integer(lexer.Next()) == null || !SkipBinary(lexer)) return null;
                        // NP, or "noaccess put".
                        while (lexer.Peek() is string next && IsExecutable(next) && next != "dup") lexer.Next();
                    }
                }
                else if (token == "/CharStrings")
                {
                    if (Integer(lexer.Next()) == null || lexer.Next() != "dict") return null;
                    while (lexer.Peek() is "dup" or "begin") lexer.Next();
                    var glyphs = new HashSet<string>(System.StringComparer.Ordinal);
                    for (var entry = lexer.Next(); entry != null; entry = lexer.Next())
                    {
                        if (entry == "end") return glyphs;
                        if (entry.StartsWith("/", System.StringComparison.Ordinal))
                        {
                            if (!SkipBinary(lexer)) return null;
                            // A name kept reserves one more entry for every 16 characters; longer than 255 is damage.
                            var name = entry.Substring(1);
                            if (name.Length > MaxNameLength) return null;
                            if (!glyphs.Contains(name) && (glyphs.Count >= MaxGlyphs || !reserve(1 + name.Length / 16))) return null;
                            glyphs.Add(name);
                        }
                        // ND, or "noaccess def"; anything else means the entries are out of step.
                        else if (!IsExecutable(entry)) return null;
                    }
                    return null;
                }
            }
            return null;
        }

        // "count RD " and the count bytes after it.
        static bool SkipBinary(Lexer lexer)
        {
            var count = Integer(lexer.Next());
            var rd = lexer.Next();
            if (count is not >= 0 || rd == null || !IsExecutable(rd)) return false;
            return lexer.Skip(1 + count.Value);
        }

        static bool IsExecutable(string token)
            => token.Length > 0 && token[0] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}') && Integer(token) == null;

        static int? Integer(string? token)
        {
            if (token == null || token.Length == 0 || token.Length > 9) return null;
            int value = 0, i = token[0] is '-' or '+' ? 1 : 0;
            if (i == token.Length) return null;
            for (; i < token.Length; i++)
            {
                if (token[i] < '0' || token[i] > '9') return null;
                value = value * 10 + (token[i] - '0');
            }
            return token[0] == '-' ? -value : value;
        }

        static int HexValue(byte b)
        {
            if (b >= '0' && b <= '9') return b - '0';
            if (b >= 'a' && b <= 'f') return b - 'a' + 10;
            if (b >= 'A' && b <= 'F') return b - 'A' + 10;
            return -1;
        }

        static bool IsWhite(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0;

        /// <summary>
        /// PostScript tokens: names, numbers and operators as their text; literal names with their slash; strings,
        /// hexadecimal strings and procedures' brackets as one token each. Comments and white space are skipped.
        /// </summary>
        sealed class Lexer
        {
            readonly byte[] _data;
            readonly int _end;
            readonly Action _tick;
            string? _peeked;
            bool _hasPeeked;

            internal Lexer(byte[] data, int start, int end, Action tick)
            {
                _data = data;
                Position = start;
                _end = end;
                _tick = tick;
            }

            /// <summary>The offset just past the last token read.</summary>
            internal int Position { get; private set; }

            internal string? Peek()
            {
                if (!_hasPeeked)
                {
                    int position = Position;
                    _peeked = Read();
                    _hasPeeked = true;
                    _peekEnd = Position;
                    Position = position;
                }
                return _peeked;
            }

            int _peekEnd;

            internal string? Next()
            {
                if (_hasPeeked)
                {
                    _hasPeeked = false;
                    Position = _peekEnd;
                    return _peeked;
                }
                return Read();
            }

            /// <summary>Skips count bytes after the last token; false when they pass the end.</summary>
            internal bool Skip(int count)
            {
                _hasPeeked = false;
                if (count > _end - Position) return false;
                Position += count;
                return true;
            }

            string? Read()
            {
                _tick();
                while (Position < _end)
                {
                    byte b = _data[Position];
                    if (IsWhite(b)) Position++;
                    else if (b == '%')
                    {
                        while (Position < _end && _data[Position] != '\n' && _data[Position] != '\r') Position++;
                    }
                    else break;
                }
                if (Position >= _end) return null;
                int start = Position;
                byte first = _data[Position++];
                switch (first)
                {
                    case (byte)'(':
                        // A string, with nested parentheses and backslash escapes.
                        for (int depth = 1; depth > 0 && Position < _end; Position++)
                        {
                            byte c = _data[Position];
                            if (c == '\\') Position++;
                            else if (c == '(') depth++;
                            else if (c == ')') depth--;
                        }
                        return "(";
                    case (byte)'<':
                        if (Position < _end && _data[Position] == '<')
                        {
                            Position++;
                            return "<<";
                        }
                        while (Position < _end && _data[Position] != '>') Position++;
                        if (Position < _end) Position++;
                        return "<";
                    case (byte)'>':
                        if (Position < _end && _data[Position] == '>') Position++;
                        return ">>";
                    case (byte)'[':
                    case (byte)']':
                    case (byte)'{':
                    case (byte)'}':
                        return ((char)first).ToString();
                }
                // A name or number; a literal name keeps its slash.
                while (Position < _end && !IsWhite(_data[Position]) && !IsDelimiter(_data[Position])) Position++;
                if (Position - start > MaxTokenLength)
                {
                    Position = _end;
                    return null;
                }
                if (first == '/' && Position == start + 1) return "/";
                var chars = new char[Position - start];
                for (int i = 0; i < chars.Length; i++) chars[i] = (char)_data[start + i];
                return new string(chars);
            }

            static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
                or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
        }
    }
}
