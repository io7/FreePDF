// Original FreePDF implementation from public PDF/WCAG specifications. See PROVENANCE.md.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfSharp.UniversalAccessibility
{
    /// <summary>Read-only, native PDF/UA-1 accessibility preflight with explicitly partial coverage.</summary>
    public static class PdfAccessibilityScanner
    {
        /// <summary>Scans the original file without modifying it. I/O and cancellation exceptions propagate.</summary>
        public static PdfAccessibilityReport Scan(string path, PdfAccessibilityScanOptions? options = null, CancellationToken cancellationToken = default)
        {
            using var stream = File.OpenRead(path);
            return Scan(stream, options, cancellationToken);
        }

        /// <summary>Scans from the current position to EOF. Leaves the stream open and restores its position when seekable.</summary>
        public static PdfAccessibilityReport Scan(Stream stream, PdfAccessibilityScanOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            options ??= new PdfAccessibilityScanOptions();
            if (options.MaxFileBytes < 1 || options.MaxPages < 1 || options.MaxNodes < 1 || options.MaxDepth < 1 || options.MaxDepth > 256 || options.MaxFindings < 1 || options.MaxDecodedBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "All limits must be positive; MaxDepth must not exceed 256.");
            var scan = new AccessibilityScan(options, cancellationToken);
            long? position = stream.CanSeek ? stream.Position : null;
            try
            {
                using var input = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (input.Length + count > options.MaxFileBytes)
                        return scan.Unreadable("Input exceeds MaxFileBytes; the file was not fully read or hashed.");
                    input.Write(buffer, 0, count);
                }
                var bytes = input.ToArray();
                using (var hash = SHA256.Create())
                    scan.Report.DocumentSha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                input.Position = 0;
                try
                {
                    using var document = PdfReader.Open(input, options.Password, PdfDocumentOpenMode.Import);
                    // The reader repairs damaged cross-reference data and stream lengths silently. Such a scan
                    // describes the repaired file, which independent validators may reject.
                    scan.RepairedByReader = document.RepairedWhileReading;
                    scan.Inspect(document);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException && ex is not IOException)
                {
                    // Do not expose parser messages, passwords or document contents in diagnostics.
                    return scan.Unreadable(ex is AccessibilityLimitException ? ex.Message : "PDF could not be fully inspected (malformed, unsupported or password required). Exception type: " + ex.GetType().Name);
                }
                return scan.Finish();
            }
            finally
            {
                if (position.HasValue) stream.Position = position.Value;
            }
        }
    }

    sealed class AccessibilityLimitException(string message) : Exception(message);

    sealed partial class AccessibilityScan
    {
        internal readonly PdfAccessibilityReport Report = new();
        readonly PdfAccessibilityScanOptions _options;
        readonly CancellationToken _cancellation;
        readonly List<PdfAccessibilityFinding> _findings = new();
        readonly List<PdfAccessibilityStructureEntry> _order = new();
        readonly Dictionary<string, PdfAccessibilityCheck> _checks = new();
        readonly Dictionary<PdfDictionary, int> _pages = new();
        readonly HashSet<PdfDictionary> _visited = new();
        readonly Dictionary<string, PdfDictionary> _mcids = new();
        readonly HashSet<PdfDictionary> _objects = new();
        // Checks whose every occurrence was inspected. After a limit stops the scan, the others are not reported as passed.
        readonly HashSet<string> _finished = new();
        PdfDictionary? _roleMap;
        int _nodes, _decoded;
        bool _incomplete;
        internal bool RepairedByReader;
        const string Technique = "https://www.w3.org/WAI/WCAG22/Techniques/pdf/";
        const string Matterhorn = "https://pdfa.org/resource/the-matterhorn-protocol/";

        internal AccessibilityScan(PdfAccessibilityScanOptions options, CancellationToken cancellation)
        {
            _options = options; _cancellation = cancellation;
            Add("READ", "Input can be inspected without repair", "Provide an undamaged, readable PDF and its password if required; resolve resource limits or unsupported content.");
            Add("LANG", "Catalog language is present", "Set the document's default language; review language changes within the content.", "PDF16.html");
            Add("TITLE", "XMP document title is present", "Supply a descriptive dc:title in XMP metadata.", "PDF18.html");
            Add("UAID", "XMP identifies PDF/UA-1", "Only declare pdfuaid:part=1 after satisfying the full standard.");
            Add("DISPLAYTITLE", "Viewer displays the document title", "Set ViewerPreferences/DisplayDocTitle to true.", "PDF18.html");
            Add("MARKED", "Document declares marked content", "Create a tagged document and set MarkInfo/Marked to true.");
            Add("SUSPECTS", "Suspect tagging is not declared", "Resolve suspect tagging and review the tag tree.");
            Add("TREE", "Structure tree and parent links exist", "Supply a nonempty structure tree with unique elements and correct parent references.");
            Add("ROLE", "Structure roles resolve to PDF 1.7 standard roles", "Map custom roles to standard structure types without cycles.");
            Add("ALT", "Figures and formulas have a text alternative", "Provide meaningful Alt or ActualText; mark decorative content as artifacts.", "PDF1.html");
            Add("NESTING", "List and table children have expected parents", "Place list items in lists, list labels/bodies in items, rows in tables and cells in rows.");
            Add("MCID", "Page marked content matches structure references", "Associate each page MCID with one structure element and its ParentTree entry.");
            Add("CONTENT", "Page painting occurs inside marked content", "Tag meaningful content and mark decorative content as artifacts.", "PDF4.html");
            Add("FONT", "Fonts that show visible page text have embedded programs", "Embed the fonts used by page text and independently validate their glyph and Unicode mappings.");
            Add("TABS", "Annotated pages use structure tab order", "Set page Tabs to S and verify keyboard navigation.", "PDF3.html");
            Add("ANNOT", "Links and widgets have structure object references", "Include each link/widget annotation in the structure tree and review its accessible name.", "PDF11.html");
            Add("REVIEW", "Human accessibility review", "Review reading order, text alternatives, headings, table associations, language, contrast, forms and assistive-technology behavior.");
        }

        void Add(string id, string title, string guidance, string? technique = null)
        { _checks.Add(id, new PdfAccessibilityCheck("FPDF-A11Y-" + id, title, guidance, technique == null ? Matterhorn : Technique + technique)); }

        internal void Record(string id, PdfAccessibilityCheckStatus status, string message = "", int? page = null, string path = "/Root")
        {
            _cancellation.ThrowIfCancellationRequested();
            var check = _checks[id];
            check.Occurrences++;
            if (status > check.Status) check.Status = status;
            if (status == PdfAccessibilityCheckStatus.NotChecked) _incomplete = true;
            if (status <= PdfAccessibilityCheckStatus.Passed) return;
            if (_findings.Count >= _options.MaxFindings) throw new AccessibilityLimitException("MaxFindings reached; remaining checks were not completed.");
            _findings.Add(new PdfAccessibilityFinding(check.Id, status, message, page, path));
        }

        void Test(string id, bool success, string message, int? page = null, string path = "/Root")
            => Record(id, success ? PdfAccessibilityCheckStatus.Passed : PdfAccessibilityCheckStatus.Failed, message, page, path);

        // Counts structure, role-map and number-tree nodes. Page content is bounded by MaxDecodedBytes instead:
        // counting its operators here made ordinary long or dense documents incomplete.
        void Visit(int depth)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (++_nodes > _options.MaxNodes)
                throw new AccessibilityLimitException("Structure or number-tree traversal exceeded MaxNodes.");
            if (depth > _options.MaxDepth)
                throw new AccessibilityLimitException("Structure nesting exceeded MaxDepth.");
        }

        void Finished(params string[] ids) => _finished.UnionWith(ids);

        internal PdfAccessibilityReport Unreadable(string reason)
        {
            _incomplete = true;
            _checks["READ"].Status = PdfAccessibilityCheckStatus.NotChecked;
            if (_findings.Count >= _options.MaxFindings) _findings.RemoveAt(_findings.Count - 1);
            _findings.Add(new PdfAccessibilityFinding("FPDF-A11Y-READ", PdfAccessibilityCheckStatus.NotChecked, reason, null, "/"));
            // A check that did not inspect every occurrence has no passed or not-applicable outcome. Failures stay.
            foreach (var check in _checks)
                if (!_finished.Contains(check.Key) && check.Value.Status is PdfAccessibilityCheckStatus.NotApplicable or PdfAccessibilityCheckStatus.Passed)
                    check.Value.Status = PdfAccessibilityCheckStatus.NotChecked;
            return Finish();
        }

        internal PdfAccessibilityReport Finish()
        {
            Report.ScanCompleted = !_incomplete;
            Report.Status = _incomplete ? PdfAccessibilityScanStatus.Incomplete : _checks.Values.Any(c => c.Status == PdfAccessibilityCheckStatus.Failed) ? PdfAccessibilityScanStatus.Failed : PdfAccessibilityScanStatus.NeedsReview;
            Report.Checks = _checks.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList().AsReadOnly();
            Report.Findings = _findings.AsReadOnly(); Report.StructureOrder = _order.AsReadOnly();
            return Report;
        }

        internal static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;
        static PdfDictionary? Dict(PdfDictionary? d, string key) => d == null ? null : Resolve(d.Elements[key]) as PdfDictionary;
        static string Text(PdfDictionary? d, string key) => d == null ? "" : Resolve(d.Elements[key]) switch
        { PdfString s => s.Value, PdfStringObject s => s.Value, _ => "" };
        static string Name(PdfDictionary? d, string key) => d == null ? "" : Resolve(d.Elements[key]) switch
        { PdfName n => n.Value, PdfNameObject n => n.Value, _ => "" };
        static int? Integer(PdfItem? item) => Resolve(item) switch { PdfInteger i => i.Value, PdfIntegerObject i => i.Value, _ => null };
        static bool True(PdfDictionary? d, string key) => d != null && (Resolve(d.Elements[key]) switch { PdfBoolean b => b.Value, PdfBooleanObject b => b.Value, _ => false });
        static bool FalseOrAbsent(PdfDictionary? d, string key) => d == null || (Resolve(d.Elements[key]) switch
        { null or PdfNull or PdfNullObject => true, PdfBoolean b => !b.Value, PdfBooleanObject b => !b.Value, _ => false });
        static IEnumerable<PdfItem> Items(PdfItem? item)
        {
            item = Resolve(item);
            if (item is PdfArray array) { foreach (var child in array.Elements) yield return child; }
            else if (item != null && item is not PdfNull) yield return item;
        }

        internal void Inspect(PdfDocument document)
        {
            Report.PageCount = document.PageCount;
            if (document.PageCount > _options.MaxPages) throw new AccessibilityLimitException("Page count exceeds MaxPages.");
            for (int i = 0; i < document.PageCount; i++) _pages.Add(document.Pages[i], i + 1);
            var catalog = document.Catalog;
            Test("LANG", !String.IsNullOrWhiteSpace(Text(catalog, "/Lang")), "Default document language is missing.");
            Test("DISPLAYTITLE", True(Dict(catalog, "/ViewerPreferences"), "/DisplayDocTitle"), "DisplayDocTitle is not true.");
            var markInfo = Dict(catalog, "/MarkInfo");
            Test("MARKED", True(markInfo, "/Marked"), "MarkInfo/Marked is not true.");
            Test("SUSPECTS", FalseOrAbsent(markInfo, "/Suspects"), "MarkInfo/Suspects is true or is not a boolean; tagging needs review.");
            InspectMetadata(Dict(catalog, "/Metadata"));
            Finished("LANG", "DISPLAYTITLE", "MARKED", "SUSPECTS", "TITLE", "UAID");
            var root = Dict(catalog, "/StructTreeRoot");
            Test("TREE", root != null && Name(root, "/Type") == "/StructTreeRoot" && Items(root.Elements["/K"]).Any(), "A nonempty StructTreeRoot with the correct Type is required.");
            if (root != null)
            {
                _roleMap = Dict(root, "/RoleMap");
                WalkKids(root.Elements["/K"], root, null, "", "/StructTreeRoot/K", 0);
            }
            Finished("TREE", "ROLE", "ALT", "NESTING");
            foreach (var page in _pages)
            {
                InspectContent(page.Key, page.Value);
                var annotations = Items(page.Key.Elements["/Annots"]).Select(Resolve).OfType<PdfDictionary>().ToList();
                if (annotations.Count != 0)
                    Test("TABS", Name(page.Key, "/Tabs") == "/S", "Annotated page does not use structure tab order.", page.Value, "/Tabs");
                foreach (var annotation in annotations)
                    if (Name(annotation, "/Subtype") is "/Link" or "/Widget")
                        Test("ANNOT", _objects.Contains(annotation), "Link or widget has no structure OBJR association.", page.Value, "/Annots");
            }
            Finished("CONTENT", "FONT", "TABS", "ANNOT");
            InspectParentTree(root);
            Finished("MCID");
            Record("REVIEW", PdfAccessibilityCheckStatus.NeedsReview, _checks["REVIEW"].Guidance);
            Record("READ", RepairedByReader ? PdfAccessibilityCheckStatus.NotChecked : PdfAccessibilityCheckStatus.Passed,
                "The PDF reader repaired damaged cross-reference data or stream lengths. Results describe the repaired file; validate the original independently.");
            Finished("REVIEW", "READ");
        }

        byte[] Decode(PdfDictionary d)
        {
            var bytes = d.Stream?.UnfilteredValue ?? Array.Empty<byte>();
            if (bytes.Length > _options.MaxDecodedBytes - _decoded) throw new AccessibilityLimitException("Decoded content exceeds MaxDecodedBytes.");
            _decoded += bytes.Length;
            return bytes;
        }

        void InspectMetadata(PdfDictionary? metadata)
        {
            XDocument? xmp = null;
            if (metadata?.Stream != null)
            {
                using var bytes = new MemoryStream(Decode(metadata));
                using var reader = XmlReader.Create(bytes, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = _options.MaxDecodedBytes });
                try { xmp = XDocument.Load(reader); }
                catch (XmlException) { Record("TITLE", PdfAccessibilityCheckStatus.NotChecked, "XMP could not be parsed safely."); Record("UAID", PdfAccessibilityCheckStatus.NotChecked, "XMP could not be parsed safely."); return; }
            }
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XNamespace ua = "http://www.aiim.org/pdfua/ns/id/";
            var declarations = xmp?.Descendants().SelectMany(e => e.Attributes(ua + "part").Select(a => a.Value).Concat(e.Name == ua + "part" ? new[] { e.Value } : Array.Empty<string>())).ToList();
            Test("TITLE", xmp != null && xmp.Descendants(dc + "title").Any(t => !String.IsNullOrWhiteSpace(t.Value)), "XMP dc:title is missing or empty.", path: "/Metadata");
            if (declarations?.Contains("2") == true) Record("UAID", PdfAccessibilityCheckStatus.NotChecked, "PDF/UA-2 is outside this profile.", path: "/Metadata");
            else Test("UAID", declarations?.Count > 0 && declarations.All(x => x == "1"), "XMP does not unambiguously declare pdfuaid:part=1.", path: "/Metadata");
        }

        static readonly HashSet<string> StandardRoles = new("Document Part Art Sect Div BlockQuote Caption TOC TOCI Index NonStruct Private P H H1 H2 H3 H4 H5 H6 L LI Lbl LBody Table TR TH TD THead TBody TFoot Span Quote Note Reference BibEntry Code Link Annot Ruby RB RT RP Warichu WT WP Figure Formula Form".Split(' ').Select(x => "/" + x));

        string Role(PdfDictionary element)
        {
            var role = Name(element, "/S");
            var seen = new HashSet<string>();
            while (!StandardRoles.Contains(role) && role.Length > 0 && seen.Add(role))
            {
                Visit(seen.Count);
                role = Name(_roleMap, role);
            }
            return StandardRoles.Contains(role) ? role : "";
        }

        void WalkKids(PdfItem? kids, PdfDictionary parent, PdfDictionary? inheritedPage, string parentRole, string path, int depth)
        {
            int index = 0;
            foreach (var raw in Items(kids))
            {
                Visit(depth);
                var item = Resolve(raw); var here = path + "[" + index++ + "]";
                if (Integer(item) is int mcid) { AddMcid(inheritedPage, mcid, parent, here); continue; }
                if (item is not PdfDictionary d) { Test("TREE", false, "Unexpected structure child type.", path: here); continue; }
                var page = Dict(d, "/Pg") ?? inheritedPage;
                int? number = page != null && _pages.TryGetValue(page, out var n) ? n : null;
                if (Name(d, "/Type") == "/MCR")
                {
                    if (d.Elements.ContainsKey("/Stm")) Record("MCID", PdfAccessibilityCheckStatus.NotChecked, "Content in a separate stream is outside this preflight's page-content coverage.", number, here);
                    else if (Integer(d.Elements["/MCID"]) is int id) AddMcid(page, id, parent, here);
                    else Test("MCID", false, "MCR is missing its integer MCID.", number, here);
                    continue;
                }
                if (Name(d, "/Type") == "/OBJR")
                {
                    var obj = Dict(d, "/Obj");
                    Test("ANNOT", obj != null && _objects.Add(obj), "OBJR target is missing or referenced more than once.", number, here);
                    continue;
                }
                if (!_visited.Add(d)) { Test("TREE", false, "Structure cycle or repeated structure element.", number, here); continue; }
                Test("TREE", ReferenceEquals(Dict(d, "/P"), parent), "Structure parent reference does not match the containing element.", number, here);
                var role = Role(d);
                Test("ROLE", role.Length > 0, "Structure role is absent, unknown or cyclic.", number, here);
                var alt = Text(d, "/Alt"); if (String.IsNullOrWhiteSpace(alt)) alt = Text(d, "/ActualText");
                _order.Add(new PdfAccessibilityStructureEntry(role, number, here, alt));
                if (role is "/Figure" or "/Formula") Test("ALT", !String.IsNullOrWhiteSpace(alt), "Figure or formula has no Alt or ActualText.", number, here);
                bool valid = role switch { "/LI" => parentRole == "/L", "/Lbl" or "/LBody" => parentRole == "/LI", "/TR" => parentRole is "/Table" or "/THead" or "/TBody" or "/TFoot", "/TH" or "/TD" => parentRole == "/TR", "/THead" or "/TBody" or "/TFoot" => parentRole == "/Table", _ => true };
                if (role is "/LI" or "/Lbl" or "/LBody" or "/TR" or "/TH" or "/TD" or "/THead" or "/TBody" or "/TFoot") Test("NESTING", valid, "List or table element has an unexpected parent role.", number, here);
                WalkKids(d.Elements["/K"], d, page, role, here + "/K", depth + 1);
            }
        }

        void AddMcid(PdfDictionary? page, int id, PdfDictionary element, string path)
        {
            int? number = page != null && _pages.TryGetValue(page, out var n) ? n : null;
            var key = number + ":" + id;
            bool valid = number.HasValue && id >= 0 && !_mcids.ContainsKey(key);
            Test("MCID", valid, "MCID has no valid page, is negative or is referenced more than once.", number, path);
            if (valid) _mcids.Add(key, element);
        }

        void InspectParentTree(PdfDictionary? root)
        {
            if (_mcids.Count == 0) return;
            var entries = new Dictionary<int, PdfItem>();
            var seen = new HashSet<PdfDictionary>();
            void Walk(PdfDictionary? node, int depth)
            {
                if (node == null) return;
                Visit(depth);
                if (!seen.Add(node)) { Test("MCID", false, "ParentTree contains a cycle or repeated node."); return; }
                if (Resolve(node.Elements["/Nums"]) is PdfArray nums)
                {
                    Test("MCID", nums.Elements.Count % 2 == 0, "ParentTree Nums must contain key/value pairs.");
                    for (int i = 0; i + 1 < nums.Elements.Count; i += 2)
                    {
                        Visit(depth);
                        if (Integer(nums.Elements[i]) is int key && key >= 0 && !entries.ContainsKey(key)) entries.Add(key, nums.Elements[i + 1]);
                        else Test("MCID", false, "ParentTree key is invalid or duplicated.");
                    }
                }
                foreach (var child in Items(node.Elements["/Kids"])) Walk(Resolve(child) as PdfDictionary, depth + 1);
            }
            Walk(Dict(root, "/ParentTree"), 0);
            var pageKeys = new HashSet<int>();
            foreach (var page in _pages)
            {
                var pageMcids = _mcids.Where(x => x.Key.StartsWith(page.Value + ":", StringComparison.Ordinal)).ToList();
                if (pageMcids.Count == 0) continue;
                var key = Integer(page.Key.Elements["/StructParents"]);
                PdfItem? value = null;
                bool valid = key.HasValue && key.Value >= 0 && pageKeys.Add(key.Value) && entries.TryGetValue(key.Value, out value);
                Test("MCID", valid, "Page StructParents is missing, negative, duplicated or absent from ParentTree.", page.Value);
                var array = Resolve(value) as PdfArray;
                foreach (var entry in pageMcids)
                {
                    int id = Int32.Parse(entry.Key.Substring(entry.Key.IndexOf(':') + 1), System.Globalization.CultureInfo.InvariantCulture);
                    Test("MCID", array != null && id < array.Elements.Count && ReferenceEquals(Resolve(array.Elements[id]), entry.Value), "ParentTree does not point back to the MCID's structure element.", page.Value, "/ParentTree");
                }
            }
        }
    }
}
