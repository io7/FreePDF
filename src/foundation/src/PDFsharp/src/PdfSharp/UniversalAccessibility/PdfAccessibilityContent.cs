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
        static readonly HashSet<string> Painting = new("Tj TJ ' \" Do sh S s f F f* B B* b b* BI".Split(' '));

        static readonly HashSet<string> ShowText = new("Tj TJ ' \"".Split(' '));

        // Page content is bounded by MaxDecodedBytes, not MaxNodes; this loop only observes cancellation.
        void InspectContent(PdfDictionary page, int number)
        {
            using var content = new MemoryStream();
            foreach (var raw in Items(page.Elements["/Contents"]))
            {
                _cancellation.ThrowIfCancellationRequested();
                if (Resolve(raw) is not PdfDictionary { Stream: not null } stream)
                { Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "Page content is not a stream.", number); return; }
                var bytes = Decode(stream);
                content.Write(bytes, 0, bytes.Length); content.WriteByte(10);
            }
            var resources = InheritedResources(page);
            var scopes = new Stack<bool>();
            var ids = new HashSet<int>();
            COperator? dictionary = null;
            bool understood = true;
            // Only fonts that paint glyphs need an embedded program (PDF/UA-1 7.21.4.1). The font and the text
            // rendering mode are graphics state: q/Q save and restore them, BT/ET do not reset them, and
            // mode 3 paints nothing (ISO 32000-1 9.3.6).
            var text = new TextState(null, "", false, 0);
            var saved = new Stack<TextState>();
            foreach (var item in ContentReader.ReadContent(content.ToArray()))
            {
                _cancellation.ThrowIfCancellationRequested();
                if (item is not COperator op) continue;
                if (op.Name == "q") saved.Push(text);
                else if (op.Name == "Q" && saved.Count > 0) text = saved.Pop();
                else if (op.Name == "Tr" && op.Operands.FirstOrDefault() is CInteger mode) text = text with { Mode = mode.Value };
                else if (op.Name == "Tf" && op.Operands.FirstOrDefault() is CName fontName)
                    text = new TextState(Dict(Dict(resources, "/Font"), fontName.Name), "/Resources/Font" + fontName.Name, true, text.Mode);
                else if (op.Name == "gs" && op.Operands.FirstOrDefault() is CName stateName
                    && Resolve(Dict(Dict(resources, "/ExtGState"), stateName.Name)?.Elements["/Font"]) is PdfArray { Elements.Count: > 0 } stateFont)
                    text = new TextState(Resolve(stateFont.Elements[0]) as PdfDictionary, "/Resources/ExtGState" + stateName.Name + "/Font", true, text.Mode);
                if (ShowText.Contains(op.Name) && text.Mode != 3)
                {
                    if (text.Selected) InspectFont(text.Font, number, text.Path);
                    else if (_reportedFonts.Add(number + ":/Contents"))
                        Record("FONT", PdfAccessibilityCheckStatus.NotChecked, "Text is shown before a font is selected.", number, "/Contents");
                }
                if (op.OpCode.OpCodeName == OpCodeName.Dictionary) { dictionary = op; continue; }
                if (op.Name is "BDC" or "BMC")
                {
                    var operands = dictionary?.Operands ?? op.Operands;
                    var tag = operands.FirstOrDefault() as CName;
                    bool artifact = tag?.Name == "/Artifact";
                    int? id = null;
                    if (op.Name == "BDC" && !artifact)
                    {
                        var property = operands.Skip(1).FirstOrDefault() as CString;
                        var match = McidProperty.Match(property?.Value ?? "");
                        if (match.Success && Int32.TryParse(match.Groups[1].Value, out var parsed)) id = parsed;
                        else
                        {
                            understood = false;
                            Record("MCID", PdfAccessibilityCheckStatus.NotChecked, "Named or complex marked-content properties require independent validation.", number, "/Contents");
                        }
                    }
                    if (id.HasValue)
                    {
                        Test("MCID", ids.Add(id.Value), "MCID occurs more than once on this page.", number, "/Contents");
                        Test("MCID", _mcids.ContainsKey(number + ":" + id.Value), "Page MCID has no structure-tree association.", number, "/Contents");
                    }
                    scopes.Push(artifact || id.HasValue || (scopes.Count > 0 && scopes.Peek()));
                    if (scopes.Count > _options.MaxDepth) throw new AccessibilityLimitException("Marked-content nesting exceeds MaxDepth.");
                }
                else if (op.Name == "EMC")
                {
                    Test("CONTENT", scopes.Count > 0, "EMC has no matching marked-content begin.", number, "/Contents");
                    if (scopes.Count > 0) scopes.Pop();
                }
                else if (Painting.Contains(op.Name))
                {
                    // Once properties are unassessed, a missing scope is uncertain, not proof of failure.
                    if (understood) Test("CONTENT", scopes.Count > 0 && scopes.Peek(), "Painting operation is outside tagged content or an artifact.", number, "/Contents");
                    if (op.Name == "Do" && op.Operands.FirstOrDefault() is CName name)
                    {
                        if (Name(Dict(Dict(resources, "/XObject"), name.Name), "/Subtype") == "/Form")
                        {
                            understood = false;
                            Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "Form XObject content is outside this preflight's coverage.", number, "/Resources/XObject" + name.Name);
                        }
                    }
                }
                dictionary = null;
            }
            Test("CONTENT", scopes.Count == 0, "Marked-content sequence is not closed.", number, "/Contents");
            if (understood)
                foreach (var entry in _mcids.Where(x => x.Key.StartsWith(number + ":", StringComparison.Ordinal)))
                {
                    int id = Int32.Parse(entry.Key.Substring(entry.Key.IndexOf(':') + 1), System.Globalization.CultureInfo.InvariantCulture);
                    Test("MCID", ids.Contains(id), "Structure MCID does not occur in the page content.", number, "/Contents");
                }
        }

        readonly HashSet<PdfDictionary> _fonts = new();
        // Text shows repeat per string; report a missing or unknown font once per page and resource name.
        readonly HashSet<string> _reportedFonts = new();

        void InspectFont(PdfDictionary? font, int page, string path)
        {
            if (font == null)
            {
                if (_reportedFonts.Add(page + ":" + path)) Test("FONT", false, "Font used for text is missing from the page resources.", page, path);
                return;
            }
            if (!_fonts.Add(font)) return;
            if (Name(font, "/Subtype") == "/Type0")
            {
                var children = Items(font.Elements["/DescendantFonts"]).Select(Resolve).OfType<PdfDictionary>().ToList();
                Test("FONT", children.Count == 1 && Name(children[0], "/Subtype") is "/CIDFontType0" or "/CIDFontType2", "Type0 font must identify a CID descendant font.", page, path);
                foreach (var child in children) InspectFont(child, page, path + "/DescendantFonts");
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
