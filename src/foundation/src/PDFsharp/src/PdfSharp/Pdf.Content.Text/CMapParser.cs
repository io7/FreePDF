// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Reads the parts of a CMap stream that text extraction needs (Adobe Technical Note #5099): codespacerange,
    /// bfchar, bfrange (also with an array of destinations), cidchar, cidrange and /WMode. Everything else is skipped.
    /// The parser has no recursion and keeps at most one flat array, so a hostile CMap costs time and memory in
    /// proportion to its length.
    /// </summary>
    static class CMapParser
    {
        sealed class Keyword
        {
            internal readonly string Value;
            internal Keyword(string value) { Value = value; }
        }

        sealed class Name
        {
            internal readonly string Value;
            internal Name(string value) { Value = value; }
        }

        internal static CodeMap Parse(byte[] data)
        {
            var map = new CodeMap();
            var tokens = new Tokenizer(data);
            var operands = new List<object>();
            List<object>? array = null;
            int nesting = 0;
            object? token;
            while ((token = tokens.Next()) != null)
            {
                if (token is Keyword { Value: "[" })
                {
                    if (nesting++ == 0)
                        array = new List<object>();
                    continue;
                }
                if (token is Keyword { Value: "]" })
                {
                    if (nesting > 0 && --nesting == 0)
                    {
                        operands.Add(array!);
                        array = null;
                    }
                    continue;
                }
                if (nesting > 0)
                {
                    // Only a flat array of destinations is meaningful; nested arrays are dropped.
                    if (nesting == 1 && token is not Keyword)
                        array!.Add(token);
                    continue;
                }
                if (token is not Keyword keyword)
                {
                    operands.Add(token);
                    continue;
                }
                switch (keyword.Value)
                {
                    case "endcodespacerange":
                        CodeSpaces(map, operands);
                        break;
                    case "endbfchar":
                        Chars(map, operands);
                        break;
                    case "endbfrange":
                        Ranges(map, operands);
                        break;
                    case "endcidchar":
                        CidChars(map, operands);
                        break;
                    case "endcidrange":
                        CidRanges(map, operands);
                        break;
                    case "def":
                        if (operands.Count >= 2 && operands[operands.Count - 2] is Name { Value: "/WMode" } && operands[operands.Count - 1] is int mode)
                            map.Vertical = mode == 1;
                        break;
                }
                operands.Clear();
            }
            return map;
        }

        static void CodeSpaces(CodeMap map, List<object> o)
        {
            for (int i = 0; i + 1 < o.Count; i += 2)
            {
                if (o[i] is byte[] low && o[i + 1] is byte[] high && low.Length == high.Length && low.Length >= 1 && low.Length <= 4)
                    map.AddSpace(low, high);
            }
        }

        static void Chars(CodeMap map, List<object> o)
        {
            for (int i = 0; i + 1 < o.Count; i += 2)
            {
                if (o[i] is not byte[] code || code.Length < 1 || code.Length > 4)
                    continue;
                var text = Destination(o[i + 1]);
                if (text != null)
                    map.AddText(code.Length, CodeMap.Value(code), text);
            }
        }

        static void Ranges(CodeMap map, List<object> o)
        {
            for (int i = 0; i + 2 < o.Count; i += 3)
            {
                if (!Bounds(o[i], o[i + 1], out int length, out uint low, out uint high))
                    continue;
                if (o[i + 2] is List<object> array)
                    map.AddTextRange(length, low, high, array.Select(Destination).ToArray());
                else if (o[i + 2] is byte[] start && start.Length > 0)
                    map.AddTextRange(length, low, high, Utf16(start));
            }
        }

        static void CidChars(CodeMap map, List<object> o)
        {
            for (int i = 0; i + 1 < o.Count; i += 2)
            {
                if (o[i] is byte[] code && code.Length >= 1 && code.Length <= 4 && o[i + 1] is int cid)
                    map.AddCid(code.Length, CodeMap.Value(code), cid);
            }
        }

        static void CidRanges(CodeMap map, List<object> o)
        {
            for (int i = 0; i + 2 < o.Count; i += 3)
            {
                if (Bounds(o[i], o[i + 1], out int length, out uint low, out uint high) && o[i + 2] is int cid)
                    map.AddCidRange(length, low, high, cid);
            }
        }

        static bool Bounds(object first, object last, out int length, out uint low, out uint high)
        {
            length = 0;
            low = high = 0;
            if (first is not byte[] a || last is not byte[] b || a.Length != b.Length || a.Length < 1 || a.Length > 4)
                return false;
            length = a.Length;
            low = CodeMap.Value(a);
            high = CodeMap.Value(b);
            return low <= high;
        }

        static string? Destination(object item)
        {
            if (item is byte[] bytes && bytes.Length > 0)
                return Utf16(bytes);
            if (item is Name name)
                return GlyphNames.ToUnicode(name.Value, false);
            return null;
        }

        // Destinations are UTF-16BE; a single byte is taken as one character, as some producers write it.
        static string Utf16(byte[] bytes)
        {
            if (bytes.Length == 1)
                return ((char)bytes[0]).ToString();
            var text = new StringBuilder(bytes.Length / 2);
            for (int i = 0; i + 1 < bytes.Length; i += 2)
                text.Append((char)((bytes[i] << 8) | bytes[i + 1]));
            return text.ToString();
        }

        sealed class Tokenizer
        {
            readonly byte[] _data;
            int _position;

            internal Tokenizer(byte[] data)
            {
                _data = data;
            }

            /// <summary>The next token: byte[] (string), int, Name or Keyword; null at the end.</summary>
            internal object? Next()
            {
                while (true)
                {
                    SkipWhitespace();
                    if (_position >= _data.Length)
                        return null;
                    byte b = _data[_position];
                    switch (b)
                    {
                        case (byte)'%':
                            while (_position < _data.Length && _data[_position] != '\n' && _data[_position] != '\r')
                                _position++;
                            continue;
                        case (byte)'<':
                            if (Peek(1) == '<')
                            {
                                _position += 2;
                                return new Keyword("<<");
                            }
                            return HexString();
                        case (byte)'>':
                            _position++;
                            if (Peek(0) == '>')
                                _position++;
                            return new Keyword(">>");
                        case (byte)'(':
                            return LiteralString();
                        case (byte)'[':
                        case (byte)']':
                        case (byte)'{':
                        case (byte)'}':
                            _position++;
                            return new Keyword(((char)b).ToString());
                        case (byte)'/':
                            _position++;
                            return new Name("/" + Regular());
                        default:
                            var word = Regular();
                            if (word.Length == 0)
                            {
                                _position++;
                                continue;
                            }
                            if (int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number))
                                return number;
                            return new Keyword(word);
                    }
                }
            }

            byte Peek(int ahead) => _position + ahead < _data.Length ? _data[_position + ahead] : (byte)0;

            void SkipWhitespace()
            {
                while (_position < _data.Length && IsWhitespace(_data[_position]))
                    _position++;
            }

            static bool IsWhitespace(byte b) => b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;

            static bool IsDelimiter(byte b) => b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']'
                || b == '{' || b == '}' || b == '/' || b == '%';

            string Regular()
            {
                int start = _position;
                while (_position < _data.Length && !IsWhitespace(_data[_position]) && !IsDelimiter(_data[_position]))
                    _position++;
                return Encoding.ASCII.GetString(_data, start, _position - start);
            }

            byte[] HexString()
            {
                _position++;
                var bytes = new List<byte>();
                int high = -1;
                while (_position < _data.Length && _data[_position] != '>')
                {
                    int digit = HexDigit(_data[_position++]);
                    if (digit < 0)
                        continue;
                    if (high < 0)
                    {
                        high = digit;
                    }
                    else
                    {
                        bytes.Add((byte)(high * 16 + digit));
                        high = -1;
                    }
                }
                _position++;
                if (high >= 0)
                    bytes.Add((byte)(high * 16));
                return bytes.ToArray();
            }

            static int HexDigit(byte b)
            {
                if (b >= '0' && b <= '9')
                    return b - '0';
                if (b >= 'A' && b <= 'F')
                    return b - 'A' + 10;
                if (b >= 'a' && b <= 'f')
                    return b - 'a' + 10;
                return -1;
            }

            byte[] LiteralString()
            {
                _position++;
                var bytes = new List<byte>();
                int depth = 1;
                while (_position < _data.Length)
                {
                    byte b = _data[_position++];
                    if (b == '\\' && _position < _data.Length)
                    {
                        byte escaped = _data[_position++];
                        if (escaped >= '0' && escaped <= '7')
                        {
                            int value = escaped - '0';
                            for (int k = 0; k < 2 && _position < _data.Length && _data[_position] >= '0' && _data[_position] <= '7'; k++)
                                value = value * 8 + _data[_position++] - '0';
                            bytes.Add((byte)value);
                        }
                        else
                        {
                            bytes.Add(Escaped(escaped));
                        }
                        continue;
                    }
                    if (b == '(')
                        depth++;
                    else if (b == ')' && --depth == 0)
                        break;
                    bytes.Add(b);
                }
                return bytes.ToArray();
            }

            static byte Escaped(byte b)
            {
                switch (b)
                {
                    case (byte)'n': return (byte)'\n';
                    case (byte)'r': return (byte)'\r';
                    case (byte)'t': return (byte)'\t';
                    case (byte)'b': return (byte)'\b';
                    case (byte)'f': return (byte)'\f';
                    default: return b;
                }
            }
        }
    }
}
