// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;

namespace PdfSharp.UniversalAccessibility
{
    // Rule GLYPH: a shown code must not select .notdef (ISO 14289-1 7.21.8, Matterhorn 31-030), and text used for
    // rendering must not select a glyph the embedded program lacks (7.21.4.1, Matterhorn 31-011).
    sealed partial class AccessibilityScan
    {
        enum GlyphUse : byte { Unknown, Present, Notdef, Missing }

        // Width 1: Simple holds the outcome of each one-byte code and Names its glyph name, where known. Width 2: a
        // CIDFont behind Identity-H or Identity-V, with the program's glyph count and, for a CIDFontType2, its
        // CIDToGIDMap (null for Identity); a CID-keyed CFF program gives the glyph of each CID itself (CidCff).
        // Embedded is false when the font has no program at all.
        sealed record GlyphFont(int Width, GlyphUse[]? Simple, string?[]? Names, int GlyphCount, byte[]? CidMap, bool Embedded = true,
            EmbeddedCff? CidCff = null);

        // The glyph names of an embedded Type 1 or CFF program, and its built-in encoding: a glyph name for each code,
        // null where it encodes none; BuiltIn is null when the encoding could not be read.
        sealed record NamedProgram(Func<string, bool> HasGlyph, string?[]? BuiltIn);

        // Font programs are decoded once each, and not charged to MaxDecodedBytes: a program over its limit, or past
        // the total, leaves its fonts unassessed instead of ending the scan.
        const int MaxFontProgramBytes = 32 * 1024 * 1024;
        const long MaxFontProgramTotal = 256L * 1024 * 1024;
        long _fontProgramBytes;

        readonly Dictionary<PdfDictionary, GlyphFont?> _glyphFonts = new();
        readonly Dictionary<PdfDictionary, EmbeddedTrueType?> _trueTypePrograms = new();
        readonly Dictionary<PdfDictionary, EmbeddedCff?> _cffPrograms = new();
        readonly Dictionary<PdfDictionary, EmbeddedType1?> _type1Programs = new();
        readonly HashSet<int> _notdefPages = new();
        readonly HashSet<int> _missingGlyphPages = new();
        readonly HashSet<int> _unassessedGlyphPages = new();

        static Dictionary<char, int>? _macRomanCodes;

        void InspectGlyphs(COperator op, TextState text, int page, ContentStream stream)
        {
            var path = text.Selected ? text.Path : stream.ContentPath;
            // ISO 14289-1 7.21.4.1 concerns text used for rendering; 7.21.8 every rendering mode.
            bool invisible = text.Mode == 3;
            void Unknown()
            {
                if (_unassessedGlyphPages.Add(page))
                    Record("GLYPH", PdfAccessibilityCheckStatus.NotChecked,
                        "Some shown text on this page uses a font whose glyphs could not be assessed.", page, path);
            }

            void InspectString(string value)
            {
                if (value.Length == 0) return;
                var font = text.Font == null ? null : GlyphFontOf(text.Font);
                if (font == null)
                {
                    Unknown();
                    return;
                }
                bool passed = true;
                bool assessed = false;
                for (int offset = 0; offset < value.Length; offset += font.Width)
                {
                    Tick();
                    int code = (byte)value[offset];
                    if (font.Width == 2)
                    {
                        if (offset + 1 == value.Length) { Unknown(); return; }
                        code = (code << 8) | (byte)value[offset + 1];
                    }
                    var use = font.Width == 1 ? font.Simple![code] : CidGlyph(font, code);
                    // Visible text in a font without a program fails the font check, and Matterhorn 31-011 concerns
                    // embedded programs, so only its .notdef names are judged. Nothing else covers invisible text.
                    if (!font.Embedded && use != GlyphUse.Notdef)
                    {
                        if (!invisible) continue;
                        use = GlyphUse.Unknown;
                    }
                    if (use == GlyphUse.Present || (use == GlyphUse.Missing && invisible))
                    {
                        assessed = true;
                        continue;
                    }
                    passed = false;
                    if (use == GlyphUse.Notdef)
                    {
                        if (_notdefPages.Add(page))
                            Record("GLYPH", PdfAccessibilityCheckStatus.Failed,
                                $"Shown character code <{Hex(code, font.Width)}> selects the .notdef glyph.", page, path);
                    }
                    else if (use == GlyphUse.Missing)
                    {
                        if (_missingGlyphPages.Add(page))
                        {
                            var glyph = "";
                            if (font.Names?[code] is string name) glyph = " (glyph " + ShortName(name) + ")";
                            Record("GLYPH", PdfAccessibilityCheckStatus.Failed,
                                $"Shown character code <{Hex(code, font.Width)}>{glyph} selects a glyph that the embedded font program does not contain.",
                                page, path);
                        }
                    }
                    else Unknown();
                }
                if (passed && assessed) Record("GLYPH", PdfAccessibilityCheckStatus.Passed);
            }

            ForEachShownString(op, InspectString, Unknown);
        }

        // ISO 32000-1 9.7.4.2: the CID is the code under Identity-H/V. A CID-keyed CFF program's charset gives its
        // glyph; CIDToGIDMap does for a TrueType program, and other CFF programs use the CID as the glyph index. Glyph 0
        // is .notdef.
        static GlyphUse CidGlyph(GlyphFont font, int cid)
        {
            if (font.CidCff != null)
            {
                if (cid == 0) return GlyphUse.Notdef;
                return font.CidCff.GlyphOfCid(cid) > 0 ? GlyphUse.Present : GlyphUse.Missing;
            }
            int glyph = cid;
            if (font.CidMap != null)
            {
                if (2 * cid + 1 >= font.CidMap.Length) return GlyphUse.Missing;
                glyph = (font.CidMap[2 * cid] << 8) | font.CidMap[2 * cid + 1];
            }
            if (glyph == 0) return GlyphUse.Notdef;
            if (glyph >= font.GlyphCount) return GlyphUse.Missing;
            return GlyphUse.Present;
        }

        GlyphFont? GlyphFontOf(PdfDictionary font)
        {
            if (_glyphFonts.TryGetValue(font, out var known)) return known;
            GlyphFont? result = null;
            switch (Name(font, "/Subtype"))
            {
                case "/Type0" when Name(font, "/Encoding") is "/Identity-H" or "/Identity-V":
                    result = CidFont(font);
                    break;
                case "/Type1":
                case "/MMType1":
                    result = NamedGlyphs(font, null);
                    break;
                case "/Type3":
                    result = NamedGlyphs(font, Dict(font, "/CharProcs"));
                    break;
                case "/TrueType":
                    result = SimpleTrueType(font);
                    break;
            }
            _glyphFonts.Add(font, result);
            return result;
        }

        GlyphFont? CidFont(PdfDictionary font)
        {
            if (Resolve(font.Elements["/DescendantFonts"]) is not PdfArray { Elements.Count: 1 } kids) return null;
            if (Resolve(kids.Elements[0]) is not PdfDictionary child) return null;
            var descriptor = Dict(child, "/FontDescriptor");
            // CID 0 is .notdef in every CIDFont, so even without a program the identity mapping can judge it.
            if (!HasFontProgram(descriptor)) return new GlyphFont(2, null, null, int.MaxValue, null, Embedded: false);
            if (Name(child, "/Subtype") == "/CIDFontType0")
            {
                var cff = CffProgram(Dict(descriptor, "/FontFile3"));
                if (cff == null) return null;
                return new GlyphFont(2, null, null, cff.GlyphCount, null, CidCff: cff.CidKeyed ? cff : null);
            }
            if (Name(child, "/Subtype") != "/CIDFontType2") return null;
            var program = TrueTypeProgram(Dict(descriptor, "/FontFile2"));
            if (program == null) return null;
            byte[]? map = null;
            var mapItem = Resolve(child.Elements["/CIDToGIDMap"]);
            if (mapItem is PdfDictionary { Stream: not null } stream)
            {
                map = CidToGidMap(stream);
                if (map == null) return null;
            }
            else if (!IsNull(mapItem) && Name(child, "/CIDToGIDMap") != "/Identity") return null;
            return new GlyphFont(2, null, null, program.GlyphCount, map);
        }

        // Fonts often share a descendant and its map, so each map stream is decoded once. Null for a damaged stream.
        readonly Dictionary<PdfDictionary, byte[]?> _cidToGidMaps = new();

        byte[]? CidToGidMap(PdfDictionary stream)
        {
            if (_cidToGidMaps.TryGetValue(stream, out var known)) return known;
            byte[]? map = null;
            try { map = Decode(stream); }
            catch (Exception ex) when (!EndsScan(ex)) { }
            _cidToGidMaps.Add(stream, map);
            return map;
        }

        // Type 1 and Type 3 (ISO 32000-1 9.6.6.2 and 9.6.6.3): a code's glyph name comes from /Differences, else from
        // the base encoding, and the program has a glyph of that name or not. The base is a named encoding (Annex D);
        // without one, an embedded Type 1 or CFF program's built-in encoding (Table 114). Type 3 fonts have no built-in
        // encoding, and their glyphs are the procedures in /CharProcs.
        GlyphFont NamedGlyphs(PdfDictionary font, PdfDictionary? charProcs)
        {
            var descriptor = Dict(font, "/FontDescriptor");
            var names = DifferencesNames(font);
            var program = charProcs == null ? NamedProgramOf(descriptor) : null;
            // An array that cannot be read hides which codes it changes, so then no base is used.
            string[]? baseNames = null;
            bool builtIn = false;
            var encodingItem = Resolve(font.Elements["/Encoding"]);
            if (encodingItem is PdfDictionary encoding)
            {
                if (names != null || IsNull(Resolve(encoding.Elements["/Differences"])))
                {
                    if (!IsNull(Resolve(encoding.Elements["/BaseEncoding"]))) baseNames = SimpleEncodings.Names(Name(encoding, "/BaseEncoding"));
                    else builtIn = true;
                }
            }
            else if (charProcs == null && encodingItem is PdfName or PdfNameObject) baseNames = SimpleEncodings.Names(Name(font, "/Encoding"));
            else if (IsNull(encodingItem)) builtIn = true;
            var uses = new GlyphUse[256];
            var resolved = new string?[256];
            for (int code = 0; code < 256; code++)
            {
                var name = names?[code];
                if (name == null && baseNames != null)
                {
                    if (baseNames[code].Length > 0) name = "/" + baseNames[code];
                    // A code that the encoding does not name selects .notdef in a Type 1 font program.
                    else if (program != null) name = "/.notdef";
                }
                if (name == null && builtIn && program?.BuiltIn != null) name = "/" + (program.BuiltIn[code] ?? ".notdef");
                resolved[code] = name;
                if (name == null) continue;
                if (name == "/.notdef") uses[code] = GlyphUse.Notdef;
                else if (charProcs != null)
                    uses[code] = Resolve(charProcs.Elements[name]) is PdfDictionary { Stream: not null } ? GlyphUse.Present : GlyphUse.Missing;
                else if (program != null) uses[code] = program.HasGlyph(name.Substring(1)) ? GlyphUse.Present : GlyphUse.Missing;
            }
            // A Type 3 font's glyph procedures are its program.
            bool embedded = charProcs != null || HasFontProgram(descriptor);
            return new GlyphFont(1, uses, resolved, 0, null, embedded);
        }

        // The names and built-in encoding of a Type 1 font's embedded program: Type 1 (FontFile) or CFF (FontFile3,
        // without CIDFont operators). Null when there is none the check can read.
        NamedProgram? NamedProgramOf(PdfDictionary? descriptor)
        {
            var type1 = Type1Program(Dict(descriptor, "/FontFile"));
            if (type1 != null) return new NamedProgram(type1.HasGlyph, type1.BuiltInEncoding);
            var cff = CffProgram(Dict(descriptor, "/FontFile3"));
            if (cff != null && !cff.CidKeyed) return new NamedProgram(cff.HasGlyph, cff.BuiltInEncoding);
            return null;
        }

        EmbeddedType1? Type1Program(PdfDictionary? file)
        {
            if (file?.Stream == null) return null;
            if (_type1Programs.TryGetValue(file, out var known)) return known;
            var data = DecodeFontProgram(file);
            EmbeddedType1? program = null;
            if (data != null) program = EmbeddedType1.Read(data, Tick);
            _type1Programs.Add(file, program);
            return program;
        }

        // ISO 32000-1 Table 126: Type1C and CIDFontType0C streams hold a bare CFF program; OpenType ones are not read.
        EmbeddedCff? CffProgram(PdfDictionary? file)
        {
            if (file?.Stream == null || Name(file, "/Subtype") is not ("/Type1C" or "/CIDFontType0C")) return null;
            if (_cffPrograms.TryGetValue(file, out var known)) return known;
            var data = DecodeFontProgram(file);
            EmbeddedCff? program = null;
            if (data != null) program = EmbeddedCff.Read(data, Tick);
            _cffPrograms.Add(file, program);
            return program;
        }

        static bool HasFontProgram(PdfDictionary? descriptor)
        {
            foreach (var key in new[] { "/FontFile", "/FontFile2", "/FontFile3" })
            {
                if (Dict(descriptor, key)?.Stream?.Value.Length > 0) return true;
            }
            return false;
        }

        string?[]? DifferencesNames(PdfDictionary font)
        {
            if (Dict(font, "/Encoding") is not PdfDictionary encoding) return null;
            if (Resolve(encoding.Elements["/Differences"]) is not PdfArray array) return null;
            return DifferencesOf(array).Names;
        }

        // ISO 32000-1 9.6.6.4. The nonsymbolic lookup applies with a named WinAnsi or MacRoman encoding or the
        // Nonsymbolic flag; otherwise the symbolic one.
        GlyphFont SimpleTrueType(PdfDictionary font)
        {
            var names = DifferencesNames(font);
            var descriptor = Dict(font, "/FontDescriptor");
            int flags = Integer(descriptor?.Elements["/Flags"]) ?? 0;
            var named = Name(font, "/Encoding");
            bool nonsymbolic = named is "/WinAnsiEncoding" or "/MacRomanEncoding" || (flags & 32) != 0;
            var baseName = named;
            if (Dict(font, "/Encoding") is PdfDictionary encoding) baseName = Name(encoding, "/BaseEncoding");
            var baseTable = SimpleEncodings.Named(baseName);
            if (baseName == "/StandardEncoding") baseTable = null;
            var baseNames = baseTable == null ? null : SimpleEncodings.Names(baseName);
            // No MacExpert table is at hand, so codes that encoding gives stay unknown.
            bool unknownBase = baseName == "/MacExpertEncoding";
            var program = TrueTypeProgram(Dict(descriptor, "/FontFile2"));
            var uses = new GlyphUse[256];
            for (int code = 0; code < 256; code++)
            {
                Tick();
                var name = names?[code];
                if (nonsymbolic && name == "/.notdef") uses[code] = GlyphUse.Notdef;
                else if (program == null || (nonsymbolic && name == null && unknownBase)) uses[code] = GlyphUse.Unknown;
                else if (nonsymbolic) uses[code] = NonsymbolicGlyph(program, code, name, baseTable, baseNames);
                else uses[code] = SymbolicGlyph(program, code);
            }
            return new GlyphFont(1, uses, names, 0, null, HasFontProgram(descriptor));
        }

        static GlyphUse SymbolicGlyph(EmbeddedTrueType program, int code)
        {
            if (program.HasSubtable(3, 0))
            {
                bool undecided = false;
                foreach (int candidate in new[] { code, 0xF000 + code, 0xF100 + code, 0xF200 + code })
                {
                    var glyph = program.Lookup(3, 0, candidate);
                    if (glyph == null) undecided = true;
                    else if (glyph > 0 && glyph < program.GlyphCount) return GlyphUse.Present;
                }
                if (undecided) return GlyphUse.Unknown;
                return GlyphUse.Missing;
            }
            if (!program.HasSubtable(1, 0)) return GlyphUse.Unknown;
            return Found(program, program.Lookup(1, 0, code));
        }

        // The glyph name comes from /Differences, else the base encoding (ISO 32000-1 Annex D), else StandardEncoding.
        // Its Unicode value is looked up in the (3,1) subtable, its MacRoman code in the (1,0) subtable, and the name in
        // the 'post' table. A glyph is missing only when every lookup that could find it is known to fail.
        static GlyphUse NonsymbolicGlyph(EmbeddedTrueType program, int code, string? differencesName, string?[]? baseTable,
            string[]? baseNames)
        {
            string? name = differencesName?.Substring(1);
            string? unicode = null;
            if (name == null)
            {
                unicode = baseTable?[code];
                if (unicode != null && baseNames?[code] is { Length: > 0 } baseName) name = baseName;
            }
            if (name == null && unicode == null && GlyphTables.StandardEncoding[code].Length > 0)
                name = GlyphTables.StandardEncoding[code];
            if (name == null && unicode == null) return GlyphUse.Unknown;
            if (unicode == null) unicode = GlyphNames.ToUnicode(name!, false);
            // A value outside the BMP, or several characters, may be found by a lookup this check does not make.
            bool undecided = unicode != null && unicode.Length != 1;
            if (unicode != null && unicode.Length == 1)
            {
                var found = Found(program, program.Lookup(3, 1, unicode[0]));
                if (found == GlyphUse.Present) return found;
                undecided |= found == GlyphUse.Unknown && program.HasSubtable(3, 1);
                if (MacRomanCode(unicode[0]) is int macCode)
                {
                    found = Found(program, program.Lookup(1, 0, macCode));
                    if (found == GlyphUse.Present) return found;
                    undecided |= found == GlyphUse.Unknown && program.HasSubtable(1, 0);
                }
            }
            if (name != null && program.GlyphByCustomName(name) is int named && named > 0 && named < program.GlyphCount)
                return GlyphUse.Present;
            // The 'post' table may still name the glyph: under a standard Macintosh name (those are in the Adobe Glyph
            // List, or are .null or nonmarkingreturn), under a custom name when the base encoding gives the code a
            // character but no name (a Mac OS Roman code that MacRomanEncoding lacks), or in a part the reader could
            // not read.
            bool standardName = name == null || GlyphNames.IsListed(name) || name is ".null" or "nonmarkingreturn";
            bool postMayName = !program.PostNamesComplete || (standardName && program.HasStandardNames)
                || (name == null && program.HasCustomNames);
            if (undecided || postMayName) return GlyphUse.Unknown;
            return GlyphUse.Missing;
        }


        static GlyphUse Found(EmbeddedTrueType program, int? glyph)
        {
            if (glyph == null) return GlyphUse.Unknown;
            if (glyph > 0 && glyph < program.GlyphCount) return GlyphUse.Present;
            return GlyphUse.Missing;
        }

        static int? MacRomanCode(char unicode)
        {
            if (_macRomanCodes == null)
            {
                var codes = new Dictionary<char, int>();
                for (int code = 0; code < 256; code++)
                {
                    char c = GlyphTables.MacRomanEncoding[code];
                    if (c != '�' && !codes.ContainsKey(c)) codes.Add(c, code);
                }
                _macRomanCodes = codes;
            }
            return _macRomanCodes.TryGetValue(unicode, out var macCode) ? macCode : null;
        }

        EmbeddedTrueType? TrueTypeProgram(PdfDictionary? file)
        {
            if (file?.Stream == null) return null;
            if (_trueTypePrograms.TryGetValue(file, out var known)) return known;
            var data = DecodeFontProgram(file);
            EmbeddedTrueType? program = null;
            if (data != null) program = EmbeddedTrueType.Read(data, Tick);
            _trueTypePrograms.Add(file, program);
            return program;
        }

        byte[]? DecodeFontProgram(PdfDictionary file)
        {
            long left = MaxFontProgramTotal - _fontProgramBytes;
            if (left <= 0) return null;
            long limit = Math.Min(MaxFontProgramBytes, left);
            var bytes = file.Stream!.Value;
            var filter = Resolve(file.Elements["/Filter"]);
            long spent = 0;
            if (!IsNull(filter))
            {
                var decodeLimit = new DecodeLimit(limit, limit);
                try { bytes = Filtering.Decode(bytes, filter!, file.Elements["/DecodeParms"], decodeLimit); }
                // The program's own limit or damaged data: only this font is left unassessed, but what was decoded
                // counts toward the total, so distinct oversized programs cannot each spend a full limit. A filter
                // refused before it runs has decoded nothing and costs nothing.
                catch (PdfReaderException ex) when (Parser.IsLimitExceeded(ex))
                {
                    _fontProgramBytes += decodeLimit.Spent;
                    return null;
                }
                catch (Exception ex) when (!EndsScan(ex))
                {
                    _fontProgramBytes += decodeLimit.Spent;
                    return null;
                }
                spent = decodeLimit.Spent;
            }
            _fontProgramBytes += Math.Max(spent, bytes.Length);
            return bytes.Length > limit ? null : bytes;
        }
    }
}
