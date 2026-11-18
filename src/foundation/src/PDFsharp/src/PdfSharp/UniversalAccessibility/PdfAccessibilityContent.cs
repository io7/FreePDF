// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace PdfSharp.UniversalAccessibility
{
    sealed partial class AccessibilityScan
    {
        // Only this deliberately narrow inline property dictionary is interpreted.
        // Other dictionaries are reported as unassessed, never guessed with a substring match.
        static readonly Regex McidProperty = new(@"^<<\s*/MCID\s+([0-9]+)\s*>>$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        static readonly HashSet<string> Painting = new("Tj TJ ' \" sh S s f F f* B B* b b* BI".Split(' '));

        static readonly HashSet<string> ShowText = new("Tj TJ ' \"".Split(' '));

        enum Marking { None, Tagged, Artifact, Unknown }

        sealed class PageContent(int number, PdfDictionary? resources)
        {
            internal readonly int Number = number;
            internal readonly PdfDictionary? Resources = resources;
            internal readonly HashSet<PdfDictionary> ActiveForms = new();
        }

        readonly record struct ContentStream(PdfDictionary Owner, PdfDictionary? Resources, string Path)
        {
            internal string ContentPath => Path.Length == 0 ? "/Contents" : Path;
            internal string ResourcePath => Path + "/Resources";
        }

        // A location repeats the resource names from the page down to the object. A file can give an outer form a
        // very long name and then record many findings inside it, so each name and each whole location is shortened
        // before a finding keeps it. Readers can rely on names of up to 127 bytes (ISO 32000-1, Annex C).
        internal const int MaxLocationNameLength = 127;
        internal const int MaxLocationLength = 1024;
        const string Elision = "...";

        static string Location(string parent, string category, string? name = null)
        {
            var key = name ?? "";
            // The name includes its leading slash.
            if (key.Length > MaxLocationNameLength + 1) key = Head(key, MaxLocationNameLength + 1) + Elision;
            var location = parent + category + key;
            if (location.Length <= MaxLocationLength) return location;
            // Keep the start (where the page invokes the content) and the end (the object with the finding).
            int head = MaxLocationLength / 4;
            return Head(location, head) + "/" + Elision + Tail(location, MaxLocationLength - head - Elision.Length - 1);
        }

        static string Head(string value, int length) =>
            value.Substring(0, char.IsHighSurrogate(value[length - 1]) ? length - 1 : length);

        static string Tail(string value, int length)
        {
            int start = value.Length - length;
            return value.Substring(char.IsLowSurrogate(value[start]) ? start + 1 : start);
        }

        sealed class FormUse
        {
            internal readonly HashSet<int> Pages = new();
            internal bool ContainsMcids;
        }

        readonly Dictionary<PdfDictionary, FormUse> _formUses = new();
        readonly HashSet<string> _unknownPainting = new();

        void CheckFormPage(PdfDictionary form, int page)
        {
            if (_formUses.TryGetValue(form, out var uses))
                Test("MCID", uses.Pages.Contains(page), "Form was not drawn on the page specified by its structure reference.", page, "/ParentTree");
            else
                Record("MCID", PdfAccessibilityCheckStatus.NotChecked, "The referenced stream was not reached through page or form content.", page, "/ParentTree");
        }

        void InspectContent(PdfDictionary page, int number)
        {
            using var content = new MemoryStream();
            foreach (var raw in Items(page.Elements["/Contents"]))
            {
                _cancellation.ThrowIfCancellationRequested();
                if (Resolve(raw) is not PdfDictionary { Stream: not null } stream)
                { Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "Page content is not a stream.", number); return; }
                var bytes = Decode(stream);
                content.Write(bytes, 0, bytes.Length);
                content.WriteByte(10);
            }
            var resources = InheritedResources(page);
            InspectOperators(new PageContent(number, resources), new ContentStream(page, resources, ""),
                content.ToArray(), Marking.None, new TextState(null, "", false, 0));
        }

        bool InspectOperators(PageContent page, ContentStream stream, byte[] bytes, Marking inherited, TextState text)
        {
            var scopes = new Stack<Marking>();
            var saved = new Stack<TextState>();
            var ids = new HashSet<int>();
            _mcids.TryGetValue(stream.Owner, out var expected);
            COperator? dictionary = null;
            bool allIdsKnown = true;
            foreach (var item in ContentReader.ReadContent(bytes))
            {
                _cancellation.ThrowIfCancellationRequested();
                if (item is not COperator op) continue;
                if (op.OpCode.OpCodeName == OpCodeName.Dictionary) { dictionary = op; continue; }
                var inline = dictionary != null && op.Operands.Count == 0;
                var operands = inline ? dictionary!.Operands : op.Operands;
                dictionary = null;
                var marking = scopes.Count == 0 ? inherited : scopes.Peek();
                if (op.Name is "BDC" or "BMC")
                {
                    bool artifact = operands.FirstOrDefault() is CName { Name: "/Artifact" };
                    bool known = true;
                    int? id = op.Name == "BDC" ? PropertyMcid(operands.ElementAtOrDefault(1), inline, artifact, page.Number, stream, out known) : null;
                    allIdsKnown &= known;
                    if (id.HasValue)
                    {
                        Test("MCID", ids.Add(id.Value), "MCID occurs more than once in this content stream.", page.Number, stream.ContentPath);
                        Test("MCID", expected != null && expected.TryGetValue(id.Value, out var reference) && reference.Page == page.Number,
                            "MCID has no structure-tree association for this stream and page.", page.Number, stream.ContentPath);
                        Test("CONTENT", !artifact && marking != Marking.Artifact && inherited != Marking.Tagged,
                            "Tagged content occurs inside an artifact or a form already tagged as a whole.", page.Number, stream.ContentPath);
                    }
                    if (artifact && marking == Marking.Tagged)
                        Test("CONTENT", false, "An artifact occurs inside tagged content.", page.Number, stream.ContentPath);
                    if (artifact) marking = Marking.Artifact;
                    else if (id.HasValue) marking = Marking.Tagged;
                    else if (!known && marking == Marking.None) marking = Marking.Unknown;
                    scopes.Push(marking);
                    if (scopes.Count > _options.MaxDepth) throw new AccessibilityLimitException("Marked-content nesting exceeds MaxDepth.");
                    continue;
                }
                if (op.Name == "EMC")
                {
                    Test("CONTENT", scopes.Count > 0, "EMC has no matching marked-content begin.", page.Number, stream.ContentPath);
                    if (scopes.Count > 0) scopes.Pop();
                    continue;
                }
                if (op.Name == "DP" && operands.ElementAtOrDefault(1) is CName property)
                    PropertyMcid(property, false, false, page.Number, stream, out _);

                text = InspectText(op, page.Number, stream, saved, text);
                if (op.Name == "Do")
                    InspectXObject(op.Operands.FirstOrDefault() as CName, page, stream, marking, text);
                else if (Painting.Contains(op.Name)) CheckPainting(marking, page.Number, stream.ContentPath);
            }
            Test("CONTENT", scopes.Count == 0, "Marked-content sequence is not closed.", page.Number, stream.ContentPath);
            if (allIdsKnown && expected != null)
                foreach (var entry in expected)
                    if (entry.Value.Page == page.Number)
                        Test("MCID", ids.Contains(entry.Key), "Structure MCID does not occur in its content stream.", page.Number, stream.ContentPath);
            return ids.Count != 0;
        }

        TextState InspectText(COperator op, int page, ContentStream stream, Stack<TextState> saved, TextState text)
        {
            // A form inherits its caller's graphics state and restores it on return (ISO 32000-1 8.10.2).
            if (op.Name == "q") saved.Push(text);
            else if (op.Name == "Q" && saved.Count > 0) return saved.Pop();
            else if (op.Name == "Tr" && op.Operands.FirstOrDefault() is CInteger mode) return text with { Mode = mode.Value };
            else if (op.Name == "Tf" && op.Operands.FirstOrDefault() is CName fontName)
                return new TextState(Dict(Dict(stream.Resources, "/Font"), fontName.Name), Location(stream.ResourcePath, "/Font", fontName.Name), true, text.Mode);
            else if (op.Name == "gs" && op.Operands.FirstOrDefault() is CName stateName
                && Resolve(Dict(Dict(stream.Resources, "/ExtGState"), stateName.Name)?.Elements["/Font"]) is PdfArray { Elements.Count: > 0 } stateFont)
                return new TextState(Resolve(stateFont.Elements[0]) as PdfDictionary,
                    Location(Location(stream.ResourcePath, "/ExtGState", stateName.Name), "/Font"), true, text.Mode);
            else if (ShowText.Contains(op.Name) && text.Mode != 3)
            {
                if (text.Selected) InspectFont(text.Font, page, text.Path);
                else if (_reportedFonts.Add(page + ":" + stream.ContentPath))
                    Record("FONT", PdfAccessibilityCheckStatus.NotChecked, "Text is shown before a font is selected.", page, stream.ContentPath);
            }
            return text;
        }

        void CheckPainting(Marking marking, int page, string path)
        {
            if (marking == Marking.Unknown)
            {
                if (_unknownPainting.Add(page + ":" + path))
                    Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "The markings for this painting operation could not be resolved.", page, path);
            }
            else
                Test("CONTENT", marking != Marking.None, "Painting operation is outside tagged content or an artifact.", page, path);
        }

        int? PropertyMcid(CObject? operand, bool inline, bool artifact, int page, ContentStream stream, out bool known)
        {
            known = true;
            var path = stream.ContentPath;
            if (operand is CName name)
            {
                path = Location(stream.ResourcePath, "/Properties", name.Name);
                var properties = Dict(Dict(stream.Resources, "/Properties"), name.Name);
                if (properties != null && properties.Stream == null)
                {
                    if (!properties.Elements.ContainsKey("/MCID")) return null;
                    var id = Integer(properties.Elements["/MCID"]);
                    if (id >= 0) return id;
                    Test("MCID", false, "Named property MCID must be a non-negative integer.", page, path);
                    return null;
                }
                Record("MCID", PdfAccessibilityCheckStatus.NotChecked, "Named marked-content property list could not be resolved to a dictionary.", page, path);
            }
            else if (inline && operand is CString property)
            {
                var match = McidProperty.Match(property.Value);
                if (match.Success && Int32.TryParse(match.Groups[1].Value, out var id)) return id;
                if (artifact) return null;
                Record("MCID", PdfAccessibilityCheckStatus.NotChecked, "Complex inline marked-content properties require independent validation.", page, path);
            }
            else Record("MCID", PdfAccessibilityCheckStatus.NotChecked, "Marked-content property operand could not be inspected.", page, path);
            known = false;
            return null;
        }

        void InspectXObject(CName? name, PageContent page, ContentStream caller, Marking marking, TextState text)
        {
            var path = Location(caller.ResourcePath, "/XObject", name?.Name);
            var obj = name == null ? null : Dict(Dict(caller.Resources, "/XObject"), name.Name);
            if (obj?.Stream == null)
            {
                Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "XObject could not be resolved to a stream.", page.Number, path);
                return;
            }
            if (Name(obj, "/Subtype") == "/Image") { CheckPainting(marking, page.Number, path); return; }
            if (Name(obj, "/Subtype") != "/Form" || obj.Elements.ContainsKey("/Ref") || obj.Elements.ContainsKey("/F"))
            {
                Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "This XObject's content cannot be inspected as a local form or image.", page.Number, path);
                return;
            }
            if (!page.ActiveForms.Add(obj))
            {
                Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "Form XObject invocation is recursive.", page.Number, path);
                return;
            }
            if (page.ActiveForms.Count > _options.MaxDepth) throw new AccessibilityLimitException("Form XObject nesting exceeds MaxDepth.");
            bool repeated = _formUses.TryGetValue(obj, out var use);
            if (use == null) _formUses.Add(obj, use = new FormUse());
            use.Pages.Add(page.Number);
            bool wholeObject = _formObjects.ContainsKey(new FormPage(obj, page.Number));
            if (wholeObject || obj.Elements.ContainsKey("/StructParent"))
            {
                Test("MCID", wholeObject && !obj.Elements.ContainsKey("/StructParents"),
                    "A form tagged as an object needs its own OBJR and cannot also own structural sequences.", page.Number, path);
                if (wholeObject)
                {
                    Test("CONTENT", marking is not (Marking.Tagged or Marking.Artifact),
                        "A form tagged by an object reference is also enclosed by a tag or artifact.", page.Number, path);
                    marking = Marking.Tagged;
                }
            }
            // A legacy form without Resources looks up names on the page. A form with Resources uses only its own dictionary.
            var resources = obj.Elements.ContainsKey("/Resources") ? Dict(obj, "/Resources") : page.Resources;
            var stream = new ContentStream(obj, resources, path);
            use.ContainsMcids |= InspectOperators(page, stream, Decode(obj), marking, text);
            if (repeated && use.ContainsMcids)
                Test("MCID", false, "A Form XObject containing MCIDs is invoked more than once.", page.Number, path);
            page.ActiveForms.Remove(obj);
        }

        readonly HashSet<PdfDictionary> _fonts = new();
        // Text shows repeat per string; report a missing or unknown font once per page and resource name.
        readonly HashSet<string> _reportedFonts = new();

        void InspectFont(PdfDictionary? font, int page, string path)
        {
            if (font == null)
            {
                if (_reportedFonts.Add(page + ":" + path)) Test("FONT", false, "Font used for text is missing from the active resources.", page, path);
                return;
            }
            if (!_fonts.Add(font)) return;
            if (Name(font, "/Subtype") == "/Type0")
            {
                var children = Items(font.Elements["/DescendantFonts"]).Select(Resolve).OfType<PdfDictionary>().ToList();
                Test("FONT", children.Count == 1 && Name(children[0], "/Subtype") is "/CIDFontType0" or "/CIDFontType2", "Type0 font must identify a CID descendant font.", page, path);
                // Only a CIDFont can be a descendant. Following another Type0 font would let a chain of them nest the
                // inspection, and its location, once per font; the check above already fails such a chain.
                foreach (var child in children)
                    if (Name(child, "/Subtype") is "/CIDFontType0" or "/CIDFontType2")
                        InspectFont(child, page, Location(path, "/DescendantFonts"));
                return;
            }
            if (Name(font, "/Subtype") == "/Type3")
            {
                var glyphs = Dict(font, "/CharProcs");
                Test("FONT", glyphs != null && glyphs.Elements.Count > 0 && glyphs.Elements.Values.All(x => Resolve(x) is PdfDictionary { Stream: not null }), "Type3 font does not contain glyph programs.", page, path);
                return;
            }
            var descriptor = Dict(font, "/FontDescriptor");
            bool embedded = new[] { "/FontFile", "/FontFile2", "/FontFile3" }.Any(key => Dict(descriptor, key)?.Stream?.Value.Length > 0);
            Test("FONT", embedded, "Selected font has no embedded font program.", page, path);
        }

        // Looked up once per page; the page-tree ancestors are bounded by MaxDepth and do not count as structure nodes.
        PdfDictionary? InheritedResources(PdfDictionary page)
        {
            var seen = new HashSet<PdfDictionary>();
            PdfDictionary? node = page;
            while (node != null && seen.Add(node))
            {
                _cancellation.ThrowIfCancellationRequested();
                if (seen.Count > _options.MaxDepth) throw new AccessibilityLimitException("Page tree nesting exceeded MaxDepth.");
                var resources = Dict(node, "/Resources");
                if (resources != null) return resources;
                node = Dict(node, "/Parent");
            }
            return null;
        }

        readonly record struct TextState(PdfDictionary? Font, string Path, bool Selected, int Mode);
    }
}
