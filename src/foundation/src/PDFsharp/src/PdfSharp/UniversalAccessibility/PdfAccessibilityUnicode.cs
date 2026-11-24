// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;

namespace PdfSharp.UniversalAccessibility
{
    sealed partial class AccessibilityScan
    {
        sealed record UnicodeFont(int Width, CMapCoverage? Map, string?[]? Simple, bool RequiresMap);
        // Every font and map is assessed once. A coverage grows with its map's entries, which the decode budget
        // bounds, so neither cache needs a count limit. Fonts past such a limit would decode their map for each string.
        readonly Dictionary<PdfDictionary, UnicodeFont> _unicodeFonts = new();
        readonly Dictionary<(PdfDictionary, int), CMapCoverage?> _unicodeMaps = new();
        readonly HashSet<(int Page, PdfDictionary Font)> _missingUnicode = new();
        readonly HashSet<int> _unassessedUnicodePages = new();

        static readonly string?[] WinAnsi = SimpleEncodings.Named("/WinAnsiEncoding")!;
        static readonly string?[] MacRoman = SimpleEncodings.Named("/MacRomanEncoding")!;
        static readonly string?[] Standard = SimpleEncodings.Standard();
        static readonly string?[] Symbol = SimpleEncodings.Symbol();
        static readonly string?[] Dingbats = SimpleEncodings.ZapfDingbats();

        UnicodeFont UnicodeMapping(PdfDictionary font)
        {
            if (_unicodeFonts.TryGetValue(font, out var cached)) return cached;
            int width = 0;
            bool requiresMap = false;
            string?[]? simple = null;
            var encoding = Name(font, "/Encoding");
            switch (Name(font, "/Subtype"))
            {
                case "/Type0" when encoding is "/Identity-H" or "/Identity-V":
                    width = 2;
                    if (Resolve(font.Elements["/DescendantFonts"]) is PdfArray { Elements.Count: 1 } kids)
                    {
                        var child = Resolve(kids.Elements[0]) as PdfDictionary;
                        var info = Dict(child, "/CIDSystemInfo");
                        requiresMap = Name(child, "/Subtype") is "/CIDFontType0" or "/CIDFontType2"
                            && Text(info, "/Registry") == "Adobe" && Text(info, "/Ordering") == "Identity";
                    }
                    break;
                case "/Type1":
                case "/MMType1":
                case "/TrueType":
                case "/Type3":
                    width = 1;
                    if (Dict(font, "/Encoding") is PdfDictionary enc && !enc.Elements.ContainsKey("/Differences"))
                        encoding = Name(enc, "/BaseEncoding");
                    simple = encoding switch
                    {
                        "/WinAnsiEncoding" => WinAnsi,
                        "/MacRomanEncoding" => MacRoman,
                        "/StandardEncoding" => Standard,
                        _ => null
                    };
                    if (Name(font, "/Subtype") == "/Type1" && !font.Elements.ContainsKey("/Encoding")
                        && !font.Elements.ContainsKey("/FontDescriptor"))
                    {
                        var baseName = Name(font, "/BaseFont");
                        simple = baseName switch
                        {
                            "/Symbol" => Symbol,
                            "/ZapfDingbats" => Dingbats,
                            _ when baseName.Length > 1 && StandardFontWidths.Get(baseName.Substring(1)) != null => Standard,
                            _ => null
                        };
                    }
                    break;
            }
            CMapCoverage? map = null;
            if (width > 0 && Resolve(font.Elements["/ToUnicode"]) is PdfItem mapItem && mapItem is not (PdfNull or PdfNullObject))
            {
                var stream = mapItem as PdfDictionary;
                if (stream?.Stream != null && !stream.Elements.ContainsKey("/UseCMap"))
                {
                    if (!_unicodeMaps.TryGetValue((stream, width), out map))
                    {
                        // A damaged map stream or an unsupported filter leaves only the fonts using that map unassessed.
                        try { map = CMapParser.ReadCoverage(Decode(stream), width, Tick); }
                        catch (Exception ex) when (!EndsScan(ex)) { }
                        _unicodeMaps.Add((stream, width), map);
                    }
                }
                if (map == null)
                {
                    // A map we cannot interpret takes precedence over the simple encoding.
                    simple = null;
                    requiresMap = false;
                }
            }
            var result = new UnicodeFont(width, map, simple, requiresMap);
            _unicodeFonts.Add(font, result);
            return result;
        }

        // Limits, cancellation and memory exhaustion end the whole scan, also while a map is read.
        static bool EndsScan(Exception ex)
            => ex is AccessibilityLimitException or OperationCanceledException or OutOfMemoryException
                || (ex is PdfReaderException reader && Parser.IsLimitExceeded(reader));

        void InspectUnicode(COperator op, TextState text, int page, ContentStream stream)
        {
            var path = text.Selected ? text.Path : stream.ContentPath;
            void Unknown()
            {
                // Keep the first example per page so repeated coverage gaps do not exhaust MaxFindings.
                if (_unassessedUnicodePages.Add(page))
                    Record("UNICODE", PdfAccessibilityCheckStatus.NotChecked,
                        "Some shown text on this page uses a font encoding or mapping that could not be assessed.", page, path);
            }

            void InspectString(string value)
            {
                if (value.Length == 0) return;
                var font = text.Font == null ? null : UnicodeMapping(text.Font);
                if (font == null || font.Width == 0 || (font.Map == null && font.Simple == null && !font.RequiresMap))
                {
                    Unknown();
                    return;
                }
                bool passed = true;
                for (int offset = 0; offset < value.Length; offset += font.Width)
                {
                    Tick();
                    int code = (byte)value[offset];
                    if (font.Width == 2)
                    {
                        if (offset + 1 == value.Length) { Unknown(); return; }
                        code = (code << 8) | (byte)value[offset + 1];
                    }
                    if (font.Map?.Contains(code) == true || font.Simple?[code] != null) continue;
                    passed = false;
                    if (font.RequiresMap)
                    {
                        if (_missingUnicode.Add((page, text.Font!)))
                            Record("UNICODE", PdfAccessibilityCheckStatus.Failed,
                                $"Shown character code <{code:X4}> has no Unicode mapping in this font.", page, path);
                    }
                    else Unknown();
                }
                if (passed) Record("UNICODE", PdfAccessibilityCheckStatus.Passed);
            }

            if (op.Name == "TJ" && op.Operands.Count == 1 && op.Operands[0] is CArray array)
            {
                foreach (var item in array)
                {
                    Tick();
                    if (item is CString str) InspectString(str.Value);
                    else if (item is not (CInteger or CReal)) Unknown();
                }
            }
            else if (op.Name != "TJ" && op.Operands.Count == (op.Name == "\"" ? 3 : 1)
                && op.Operands.Last() is CString str)
                InspectString(str.Value);
            else Unknown();
        }
    }
}
