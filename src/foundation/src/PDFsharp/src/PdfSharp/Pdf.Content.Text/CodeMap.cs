// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// The code space and the mappings of a CMap (Adobe Technical Note #5099; ISO 32000-1 9.7.5 and 9.10.3): codes to
    /// Unicode text (ToUnicode) or codes to CIDs (an embedded encoding CMap). Ranges stay ranges, so a CMap costs memory
    /// in proportion to its size, never to the number of codes it covers.
    /// </summary>
    sealed class CodeMap
    {
        sealed class Space
        {
            internal byte[] Low = null!, High = null!;
        }

        sealed class Range
        {
            internal int Length;
            internal uint Low, High;
            internal string? Text;
            internal string?[]? Texts;
            internal int Cid;
        }

        // Overlapping ranges are an error in a CMap; looking back this far still finds a containing earlier range.
        const int MaxOverlapSteps = 64;

        readonly List<Space> _spaces = new();
        readonly Dictionary<long, string> _texts = new();
        readonly Dictionary<long, int> _cids = new();
        readonly List<Range> _textRanges = new();
        readonly List<Range> _cidRanges = new();
        bool _sorted = true;

        internal bool Vertical { get; set; }

        internal bool HasSpaces => _spaces.Count > 0;

        internal void AddSpace(byte[] low, byte[] high) => _spaces.Add(new Space { Low = low, High = high });

        internal void AddText(int length, uint code, string text) => _texts[Key(length, code)] = text;

        internal void AddCid(int length, uint code, int cid) => _cids[Key(length, code)] = cid;

        internal void AddTextRange(int length, uint low, uint high, string text)
        {
            if (text.Length == 0)
                return;
            _textRanges.Add(new Range { Length = length, Low = low, High = high, Text = text });
            _sorted = false;
        }

        internal void AddTextRange(int length, uint low, uint high, string?[] texts)
        {
            _textRanges.Add(new Range { Length = length, Low = low, High = high, Texts = texts });
            _sorted = false;
        }

        internal void AddCidRange(int length, uint low, uint high, int cid)
        {
            _cidRanges.Add(new Range { Length = length, Low = low, High = high, Cid = cid });
            _sorted = false;
        }

        internal static uint Value(byte[] bytes) => Value(bytes, 0, bytes.Length);

        internal static uint Value(byte[] bytes, int offset, int length)
        {
            uint value = 0;
            for (int i = 0; i < length; i++)
                value = (value << 8) | bytes[offset + i];
            return value;
        }

        static long Key(int length, uint code) => ((long)length << 32) | code;

        /// <summary>
        /// Reads one code at <paramref name="offset"/> (ISO 32000-1 9.7.6.2) and returns its length in bytes. Without a
        /// matching code space range it takes the shortest code length, so the caller always advances.
        /// </summary>
        internal int ReadCode(byte[] bytes, int offset, out uint code)
        {
            for (int length = 1; length <= 4 && offset + length <= bytes.Length; length++)
            {
                foreach (var space in _spaces)
                {
                    if (space.Low.Length == length && Contains(space, bytes, offset))
                    {
                        code = Value(bytes, offset, length);
                        return length;
                    }
                }
            }
            int shortest = _spaces.Count == 0 ? 1 : 4;
            foreach (var space in _spaces)
                shortest = Math.Min(shortest, space.Low.Length);
            int fallback = Math.Min(shortest, bytes.Length - offset);
            code = Value(bytes, offset, fallback);
            return fallback;
        }

        static bool Contains(Space space, byte[] bytes, int offset)
        {
            for (int i = 0; i < space.Low.Length; i++)
            {
                if (bytes[offset + i] < space.Low[i] || bytes[offset + i] > space.High[i])
                    return false;
            }
            return true;
        }

        internal string? Text(int length, uint code)
        {
            if (_texts.TryGetValue(Key(length, code), out var text))
                return text;
            var range = Find(_textRanges, length, code);
            if (range == null)
                return null;
            uint offset = code - range.Low;
            if (range.Texts != null)
            {
                if (offset < range.Texts.Length)
                    return range.Texts[offset];
                return null;
            }
            // The last character is incremented (ISO 32000-1 9.10.3); a range must not cross its last byte's limit.
            var start = range.Text!;
            return start.Substring(0, start.Length - 1) + (char)(start[start.Length - 1] + offset);
        }

        internal int? Cid(int length, uint code)
        {
            if (_cids.TryGetValue(Key(length, code), out var cid))
                return cid;
            var range = Find(_cidRanges, length, code);
            if (range == null)
                return null;
            return range.Cid + (int)(code - range.Low);
        }

        Range? Find(List<Range> ranges, int length, uint code)
        {
            if (!_sorted)
            {
                _textRanges.Sort(Compare);
                _cidRanges.Sort(Compare);
                _sorted = true;
            }
            // The last range that starts at or before the code.
            int low = 0, high = ranges.Count - 1, last = -1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (Compare(ranges[middle], length, code) <= 0)
                {
                    last = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            for (int i = last, steps = 0; i >= 0 && steps < MaxOverlapSteps; i--, steps++)
            {
                var range = ranges[i];
                if (range.Length != length)
                    break;
                if (code <= range.High)
                    return range;
            }
            return null;
        }

        static int Compare(Range a, Range b)
        {
            if (a.Length != b.Length)
                return a.Length.CompareTo(b.Length);
            return a.Low.CompareTo(b.Low);
        }

        static int Compare(Range range, int length, uint code)
        {
            if (range.Length != length)
                return range.Length.CompareTo(length);
            return range.Low.CompareTo(code);
        }
    }
}
