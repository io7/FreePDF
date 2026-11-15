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

        void InspectContent(PdfDictionary page, int number)
        {
            using var content = new MemoryStream();
            foreach (var raw in Items(page.Elements["/Contents"]))
            {
                Visit(0);
                if (Resolve(raw) is not PdfDictionary { Stream: not null } stream)
                { Record("CONTENT", PdfAccessibilityCheckStatus.NotChecked, "Page content is not a stream.", number); return; }
                var bytes = Decode(stream);
                content.Write(bytes, 0, bytes.Length); content.WriteByte(10);
            }
            var scopes = new Stack<bool>();
            var ids = new HashSet<int>();
            COperator? dictionary = null;
            bool understood = true;
            foreach (var item in ContentReader.ReadContent(content.ToArray()))
            {
                Visit(0);
                if (item is not COperator op) continue;
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
                else if (op.Name == "Tf" && op.Operands.FirstOrDefault() is CName fontName)
                    InspectFont(Dict(Dict(InheritedResources(page), "/Font"), fontName.Name), number, "/Resources/Font" + fontName.Name);
                else if (Painting.Contains(op.Name))
                {
                    // Once properties are unassessed, a missing scope is uncertain, not proof of failure.
                    if (understood) Test("CONTENT", scopes.Count > 0 && scopes.Peek(), "Painting operation is outside tagged content or an artifact.", number, "/Contents");
                    if (op.Name == "Do" && op.Operands.FirstOrDefault() is CName name)
                    {
                        var resource = InheritedResources(page);
                        if (Name(Dict(Dict(resource, "/XObject"), name.Name), "/Subtype") == "/Form")
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

        void InspectFont(PdfDictionary? font, int page, string path)
        {
            if (font == null) { Test("FONT", false, "Selected font resource is missing.", page, path); return; }
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

        PdfDictionary? InheritedResources(PdfDictionary page)
        {
            var seen = new HashSet<PdfDictionary>();
            PdfDictionary? node = page;
            while (node != null && seen.Add(node))
            {
                Visit(seen.Count);
                var resources = Dict(node, "/Resources");
                if (resources != null) return resources;
                node = Dict(node, "/Parent");
            }
            return null;
        }
    }
}
