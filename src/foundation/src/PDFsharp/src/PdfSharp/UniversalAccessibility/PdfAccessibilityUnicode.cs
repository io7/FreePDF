// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using System.Globalization;
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
        enum CodeMapping : byte { Unknown, Mapped, Missing }

        // Simple: what a simple font's encoding says about each code when no ToUnicode entry decides it; Glyphs: the
        // /Differences name of each code, for the report.
        sealed record UnicodeFont(int Width, CMapCoverage? Map, CodeMapping[]? Simple, string?[]? Glyphs, bool RequiresMap);
        // Every font, map and /Differences array is assessed once. A coverage grows with its map's entries, which the
        // decode budget bounds, so no cache needs a count limit. Fonts past such a limit would read their map for each
        // string, and fonts sharing one /Differences array would each walk all of it.
        readonly Dictionary<PdfDictionary, UnicodeFont> _unicodeFonts = new();
        readonly Dictionary<(PdfDictionary, int), CMapCoverage?> _unicodeMaps = new();
        readonly Dictionary<PdfArray, (string?[]? Names, CodeMapping[] Mapping)> _differences = new();
        // Pages that already report a missing mapping, a forbidden value or an unassessed code. A page can select
        // thousands of fonts, so each kind is reported once per page, with the first font and code, within MaxFindings.
        readonly HashSet<int> _missingUnicodePages = new();
        readonly HashSet<int> _forbiddenUnicodePages = new();
        readonly HashSet<int> _unassessedUnicodePages = new();

        // A code as it appears in a hex string of the font: two digits for a simple font, four for a two-byte code.
        static string Hex(int code, int width) => code.ToString(width == 1 ? "X2" : "X4", CultureInfo.InvariantCulture);

        static readonly string?[] Standard = SimpleEncodings.Standard();
        static readonly string?[] Symbol = SimpleEncodings.Symbol();
        static readonly string?[] Dingbats = SimpleEncodings.ZapfDingbats();

        UnicodeFont UnicodeMapping(PdfDictionary font)
        {
            if (_unicodeFonts.TryGetValue(font, out var cached)) return cached;
            int width = 0;
            bool requiresMap = false;
            CodeMapping[]? simple = null;
            string?[]? glyphs = null;
            switch (Name(font, "/Subtype"))
            {
                case "/Type0" when Name(font, "/Encoding") is "/Identity-H" or "/Identity-V":
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
                    simple = SimpleMapping(font, out glyphs);
                    break;
            }
            CMapCoverage? map = null;
            if (width > 0 && Resolve(font.Elements["/ToUnicode"]) is PdfItem mapItem && !IsNull(mapItem))
            {
                var stream = mapItem as PdfDictionary;
                if (stream?.Stream != null && IsNull(Resolve(stream.Elements["/UseCMap"])))
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
                    // A map the checker cannot read might give any code any value, U+0000 included, so the encoding
                    // cannot settle a code either way.
                    simple = null;
                    glyphs = null;
                    requiresMap = false;
                }
            }
            var result = new UnicodeFont(width, map, simple, glyphs, requiresMap);
            _unicodeFonts.Add(font, result);
            return result;
        }

        // A name is shortened as in locations: each page keeps its finding, so a long name would be held once per page.
        static string ShortName(string name)
        {
            if (name.Length <= MaxLocationNameLength + 1) return name;
            return Head(name, MaxLocationNameLength + 1) + Elision;
        }

        // ISO 32000-1 7.3.9: a dictionary entry whose value is null is the same as an absent entry.
        static bool IsNull(PdfItem? item) => item is null or PdfNull or PdfNullObject;

        // What a simple font's encoding says about each code when no ToUnicode entry decides it (Matterhorn 31-027,
        // ISO 32000-1 9.6.6). A named WinAnsi, MacRoman or MacExpert encoding maps every code; as the BaseEncoding of
        // a dictionary it maps the codes that /Differences leaves alone. A /Differences name maps when the Adobe Glyph
        // List or the Zapf Dingbats list has it, and is missing otherwise. Codes that depend on a font program stay
        // unknown, and so does .notdef, which a glyph check has to judge.
        CodeMapping[] SimpleMapping(PdfDictionary font, out string?[]? glyphs)
        {
            glyphs = null;
            var mapping = new CodeMapping[256];
            var encoding = Resolve(font.Elements["/Encoding"]);
            if (encoding is PdfName or PdfNameObject)
            {
                if (NamedEncoding(Name(font, "/Encoding"))) Fill(mapping, CodeMapping.Mapped);
                return mapping;
            }
            if (encoding is PdfDictionary dictionary)
            {
                if (NamedEncoding(Name(dictionary, "/BaseEncoding"))) Fill(mapping, CodeMapping.Mapped);
                var differences = Resolve(dictionary.Elements["/Differences"]);
                if (IsNull(differences)) return mapping;
                (string?[]? Names, CodeMapping[] Mapping) assessed = (null, Array.Empty<CodeMapping>());
                if (differences is PdfArray array) assessed = DifferencesOf(array);
                glyphs = assessed.Names;
                if (glyphs == null)
                {
                    // Which codes the array changes is not known, so no code is.
                    Fill(mapping, CodeMapping.Unknown);
                    return mapping;
                }
                for (int code = 0; code < 256; code++)
                {
                    if (glyphs[code] != null) mapping[code] = assessed.Mapping[code];
                }
                return mapping;
            }
            // An unembedded standard 14 font without an encoding uses its built-in one.
            if (IsNull(encoding) && Name(font, "/Subtype") == "/Type1" && IsNull(Resolve(font.Elements["/FontDescriptor"])))
            {
                var baseName = Name(font, "/BaseFont");
                var builtIn = baseName switch
                {
                    "/Symbol" => Symbol,
                    "/ZapfDingbats" => Dingbats,
                    _ when baseName.Length > 1 && StandardFontWidths.Get(baseName.Substring(1)) != null => Standard,
                    _ => null
                };
                for (int code = 0; builtIn != null && code < 256; code++)
                    if (builtIn[code] != null) mapping[code] = CodeMapping.Mapped;
            }
            return mapping;
        }

        // ISO 32000-1 Table 111 names no other predefined simple encodings; /StandardEncoding is not one of them.
        static bool NamedEncoding(string name) => name is "/WinAnsiEncoding" or "/MacRomanEncoding" or "/MacExpertEncoding";

        static void Fill(CodeMapping[] mapping, CodeMapping value)
        {
            for (int code = 0; code < mapping.Length; code++) mapping[code] = value;
        }

        // The name a /Differences array gives each code from 0 to 255 and what that name says about the code. Names is
        // null when the array holds something other than non-negative integers and names, or a name before the first
        // integer. Fonts often share one array, so each array is walked once per scan.
        (string?[]? Names, CodeMapping[] Mapping) DifferencesOf(PdfArray array)
        {
            if (_differences.TryGetValue(array, out var known)) return known;
            var names = Differences(array);
            var mapping = new CodeMapping[256];
            for (int code = 0; names != null && code < 256; code++)
            {
                var glyph = names[code];
                if (glyph == null || glyph == "/.notdef") continue;
                if (GlyphNames.IsListed(glyph.Substring(1))) mapping[code] = CodeMapping.Mapped;
                else mapping[code] = CodeMapping.Missing;
            }
            _differences.Add(array, (names, mapping));
            return (names, mapping);
        }

        string?[]? Differences(PdfArray array)
        {
            var names = new string?[256];
            int code = -1;
            foreach (var raw in array.Elements)
            {
                Tick();
                var entry = Resolve(raw);
                if (Integer(entry) is int start)
                {
                    if (start < 0) return null;
                    code = start;
                    continue;
                }
                var name = entry switch { PdfName n => n.Value, PdfNameObject n => n.Value, _ => null };
                if (name == null || code < 0) return null;
                if (code < 256) names[code++] = name;
            }
            return names;
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
                    if (font.Map?.Contains(code) == true)
                    {
                        if (font.Map.MapsToForbiddenValue(code))
                        {
                            passed = false;
                            if (_forbiddenUnicodePages.Add(page))
                                Record("UNICODE", PdfAccessibilityCheckStatus.Failed,
                                    $"Shown character code <{Hex(code, font.Width)}> maps to U+0000, U+FEFF or U+FFFE in the ToUnicode map.", page, path);
                        }
                        continue;
                    }
                    var fallback = CodeMapping.Unknown;
                    if (font.Simple != null) fallback = font.Simple[code];
                    else if (font.RequiresMap) fallback = CodeMapping.Missing;
                    if (fallback == CodeMapping.Mapped) continue;
                    passed = false;
                    if (fallback == CodeMapping.Missing)
                    {
                        if (_missingUnicodePages.Add(page))
                        {
                            var glyph = "";
                            if (font.Glyphs?[code] is string name) glyph = " (glyph " + ShortName(name) + ")";
                            Record("UNICODE", PdfAccessibilityCheckStatus.Failed,
                                $"Shown character code <{Hex(code, font.Width)}>{glyph} has no Unicode mapping in this font.", page, path);
                        }
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
