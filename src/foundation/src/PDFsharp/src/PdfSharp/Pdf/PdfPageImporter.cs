// FreePDF - MIT licence. See the LICENSE file in the solution root.

using System.Text.RegularExpressions;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;

namespace PdfSharp.Pdf
{
    /// <summary>
    /// Appends pages of an import-mode document together with their form fields, bookmarks and links
    /// (<see cref="PdfDocument.AppendPages(PdfDocument, int, int, PdfPageImportOptions)"/>).
    /// Page content is imported as AddPage imports it. Annotations are copied here instead: references to the
    /// source's pages lead to the appended pages, or are dropped when the page is not appended, so no hidden copy of
    /// another page comes along. Links, GoTo actions and bookmarks get explicit destinations on the appended pages;
    /// those that lead to a page that is not appended are left out.
    /// The import first copies everything without attaching it to the document, then attaches it in one step, so a
    /// failure on a damaged source leaves the document's pages, form and bookmarks as they were.
    /// </summary>
    sealed class PdfPageImporter
    {
        PdfPageImporter(PdfDocument target, PdfDocument source, PdfPageImportOptions options)
        {
            _target = target;
            _source = source;
            _options = options;
        }

        /// <summary>
        /// Checks the arguments of an import, before anything is changed. The target is null for an extraction,
        /// whose target does not exist yet. A document without pages may be imported as a whole (start 0, count 0).
        /// </summary>
        internal static void Validate(PdfDocument? target, PdfDocument source, int startIndex, int pageCount, PdfPageImportOptions options)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (ReferenceEquals(target, source))
                throw new ArgumentException("A document cannot append its own pages.", nameof(source));
            if (source.OpenMode != PdfDocumentOpenMode.Import)
                throw new InvalidOperationException("A PDF document must be opened with PdfDocumentOpenMode.Import to import pages from it.");
            if (target?.UAManager != null)
                throw new InvalidOperationException("Pages cannot be imported into a PDF/UA document.");
            if (source.PageCount == 0 && startIndex == 0 && pageCount == 0)
                return;
            if (startIndex < 0 || startIndex >= source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex, $"The document has {source.PageCount} pages.");
            if (pageCount < 1 || startIndex + pageCount > source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, $"The document has {source.PageCount} pages from index {startIndex}.");
        }

        internal static PdfPageImportResult Import(PdfDocument target, PdfDocument source, int startIndex, int pageCount, PdfPageImportOptions options)
        {
            Validate(target, source, startIndex, pageCount, options);
            if (pageCount == 0)
                return new PdfPageImportResult([], new Dictionary<string, string>());
            return new PdfPageImporter(target, source, options).Run(startIndex, pageCount);
        }

        PdfPageImportResult Run(int startIndex, int pageCount)
        {
            _index = SourceIndex.For(_source);
            _shared = _target.FormTable.GetImportedObjectTable(_source.Pages[startIndex]);
            var snapshot = _shared.CreateSnapshot();
            try
            {
                Stage(startIndex, pageCount);
            }
            catch
            {
                // Nothing is attached yet. Forget what the page content import registered, so that a later import does
                // not reuse copies that saving drops as unreferenced.
                _shared.RestoreSnapshot(snapshot);
                throw;
            }
            Commit();
            return new PdfPageImportResult(_newPages.Select(pair => pair.Page).ToList(), _renamedFields);
        }

        /// <summary>
        /// Copies the pages, their annotations, the kept fields and the bookmarks, without attaching anything to the
        /// document.
        /// </summary>
        void Stage(int startIndex, int pageCount)
        {
            _sourceForm = Resolve(_source.Catalog.Elements[PdfCatalog.Keys.AcroForm]) as PdfDictionary;
            for (int index = startIndex; index < startIndex + pageCount; index++)
            {
                var sourcePage = _source.Pages[index];
                var page = _target.Pages.ImportPageWithoutAnnotations(sourcePage);
                _pages[sourcePage.ObjectID] = page;
                _newPages.Add((sourcePage, page));
            }

            CollectKeptFields();
            foreach (var (sourcePage, page) in _newPages)
                CopyAnnotations(sourcePage, page);
            if (_sourceForm != null)
                StageFields(_sourceForm);
            TypeCopies();

            if (_options.KeepBookmarks && Resolve(_source.Catalog.Elements[PdfCatalog.Keys.Outlines]) is PdfDictionary outlineRoot)
                _bookmarks = ReadBookmarks(outlineRoot, []);
            ProcessPending();
        }

        /// <summary>
        /// Attaches the staged pages, fields and bookmarks to the document.
        /// </summary>
        void Commit()
        {
            foreach (var (sourcePage, page) in _newPages)
                _target.Pages.AppendImportedPage(page, sourcePage);
            foreach (var (id, reference) in _sharedCopies)
                _shared!.Add(id, reference);

            if (_fieldRoots.Count > 0)
                CommitFields();

            var parent = _target.Outlines;
            if (_options.BookmarkTitle != null)
                parent = _target.Outlines.Add(_options.BookmarkTitle, _newPages[0].Page).Outlines;
            foreach (var bookmark in _bookmarks)
                AddBookmark(parent, bookmark);
        }

        /// <summary>
        /// Keeps the field nodes that have a widget on an appended page, and the ancestors of those nodes.
        /// </summary>
        void CollectKeptFields()
        {
            foreach (var (sourcePage, _) in _newPages)
            {
                foreach (var item in ArrayItems(sourcePage.Elements[PdfPage.Keys.Annots]))
                {
                    var current = item as PdfReference;
                    while (current != null && _index!.FieldNodes.Contains(current.ObjectID) && _keptFieldNodes.Add(current.ObjectID))
                        current = (current.Value as PdfDictionary)?.Elements["/Parent"] as PdfReference;
                }
            }
        }

        void CopyAnnotations(PdfPage sourcePage, PdfPage page)
        {
            var annotations = new PdfArray(_target);
            var retargeted = new List<(PdfDictionary Copy, PdfArray? Destination, bool IsLink)>();
            foreach (var item in ArrayItems(sourcePage.Elements[PdfPage.Keys.Annots]))
            {
                if (Resolve(item) is not PdfDictionary annotation)
                    continue;

                bool isLink = NameOf(annotation, "/Subtype") == "/Link";
                var target = (LeadsToSourcePage: false, Destination: (PdfArray?)null);
                if (isLink || IsGoTo(Resolve(annotation.Elements["/A"])))
                    target = DestinationOf(annotation);
                // A link to a page that is not appended has nowhere to go.
                if (isLink && target.LeadsToSourcePage && target.Destination == null)
                    continue;

                var copy = MapItem(item);
                if (copy == null)
                    continue;
                annotations.Elements.Add(copy);
                if (target.LeadsToSourcePage)
                    retargeted.Add(((PdfDictionary)Resolve(copy)!, target.Destination, isLink));
            }
            ProcessPending();

            // The copies still hold the source's destinations. A link gets the destination on the appended page; another
            // annotation (a button) gets a GoTo action to it, or loses its action if the page is not appended.
            foreach (var (copy, destination, isLink) in retargeted)
            {
                if (isLink)
                {
                    copy.Elements.Remove("/A");
                    copy.Elements["/Dest"] = destination!;
                }
                else if (destination != null)
                {
                    var action = new PdfDictionary(_target);
                    action.Elements.SetName("/S", "/GoTo");
                    action.Elements["/D"] = destination;
                    copy.Elements["/A"] = action;
                }
                else
                {
                    copy.Elements.Remove("/A");
                }
            }

            if (annotations.Elements.Count > 0)
                page.Elements[PdfPage.Keys.Annots] = annotations;
        }

        /// <summary>
        /// Prepares the kept top-level fields for the document's form: renames those whose name the form already uses
        /// (copies of one source name get one new name), keeps the source form's default appearance with the fields
        /// when the document's form has another, and stages the form entries the fields depend on. XFA is not
        /// carried: it would describe the source's form only.
        /// </summary>
        void StageFields(PdfDictionary sourceForm)
        {
            foreach (var item in ArrayItems(sourceForm.Elements[PdfForm.Keys.Fields]))
            {
                if (item is PdfReference reference && _keptFieldNodes.Contains(reference.ObjectID) && _clones.TryGetValue(reference.ObjectID, out var copy))
                    _fieldRoots.Add(copy);
            }
            if (_fieldRoots.Count == 0)
                return;

            var existingForm = _target.Catalog.GetAcroForm();
            RenameFields(existingForm);

            // Inheritable /DA and /Q: the form-level value applies to every field without its own. With no form in the
            // document, the source's become the form's; otherwise the copied fields keep them.
            var sourceAppearance = sourceForm.Elements[PdfForm.Keys.DA] as PdfString;
            var sourceQuadding = IntegerOf(sourceForm, PdfForm.Keys.Q);
            if (existingForm == null)
            {
                _formAppearance = sourceAppearance;
                if (sourceForm.Elements.ContainsKey(PdfForm.Keys.Q))
                    _formQuadding = sourceQuadding;
            }
            else
            {
                existingForm.Elements.TryGetString(PdfForm.Keys.DA, out var targetAppearance);
                var targetQuadding = IntegerOf(existingForm, PdfForm.Keys.Q);
                foreach (var root in _fieldRoots)
                {
                    var field = (PdfDictionary)root.Value;
                    if (sourceAppearance != null && sourceAppearance.Value != targetAppearance && !field.Elements.ContainsKey(PdfForm.Keys.DA))
                        field.Elements[PdfForm.Keys.DA] = sourceAppearance;
                    if (sourceQuadding != targetQuadding && !field.Elements.ContainsKey(PdfForm.Keys.Q))
                        field.Elements[PdfForm.Keys.Q] = new PdfInteger(sourceQuadding);
                }
            }

            _needAppearances = Resolve(sourceForm.Elements[PdfForm.Keys.NeedAppearances]) is PdfBoolean { Value: true };
            _signatureFlags = IntegerOf(sourceForm, PdfForm.Keys.SigFlags);
            StageResources(sourceForm, existingForm);
            foreach (var reference in ArrayItems(sourceForm.Elements[PdfForm.Keys.CO]).OfType<PdfReference>())
            {
                if (_clones.TryGetValue(reference.ObjectID, out var copy))
                    _calculationOrder.Add(copy);
            }
            ProcessPending();
        }

        void RenameFields(PdfForm? existingForm)
        {
            var names = new HashSet<string>();
            if (existingForm != null)
            {
                foreach (var item in ArrayItems(existingForm.Elements[PdfForm.Keys.Fields]))
                {
                    if (Resolve(item) is PdfDictionary field && field.Elements.TryGetString("/T", out var name))
                        names.Add(name);
                }
            }

            // Top-level fields of one name are one field (ISO 32000-1 12.7.3.2): their copies share one new name.
            var assigned = new Dictionary<string, string>();
            foreach (var root in _fieldRoots)
            {
                var field = (PdfDictionary)root.Value;
                if (!field.Elements.TryGetString("/T", out var name))
                    continue;
                if (!assigned.TryGetValue(name, out var newName))
                {
                    newName = name;
                    for (int suffix = 2; names.Contains(newName); suffix++)
                        newName = $"{name}_{suffix}";
                    assigned.Add(name, newName);
                    names.Add(newName);
                    if (newName != name)
                        _renamedFields.Add(name, newName);
                }
                if (newName != name)
                {
                    // ASCII names stay plain strings, as most forms write them.
                    var encoding = PdfStringEncoding.Unicode;
                    if (newName.All(ch => ch < 128))
                        encoding = PdfStringEncoding.PDFDocEncoding;
                    field.Elements["/T"] = new PdfString(newName, encoding);
                }
            }
        }

        /// <summary>
        /// Stages the source form's default resources (/DR): all of them if the document's form has none, else the
        /// names it does not have. A font name the document's form uses for another font is added under a new name,
        /// and the copied fields' /DA strings are changed to it, so the fields keep their font.
        /// </summary>
        void StageResources(PdfDictionary sourceForm, PdfForm? existingForm)
        {
            var sourceItem = sourceForm.Elements[PdfForm.Keys.DR];
            if (Resolve(sourceItem) is not PdfDictionary sourceResources)
                return;
            if (Resolve(existingForm?.Elements[PdfForm.Keys.DR]) is not PdfDictionary resources)
            {
                _allResources = MapItem(sourceItem);
                return;
            }

            var fontNames = new Dictionary<string, string>();
            foreach (var category in sourceResources.Elements.KeyNames)
            {
                var sourceCategoryItem = sourceResources.Elements[category];
                if (Resolve(sourceCategoryItem) is not PdfDictionary sourceCategory)
                    continue;
                if (Resolve(resources.Elements[category]) is not PdfDictionary targetCategory)
                {
                    var copy = MapItem(sourceCategoryItem);
                    if (copy != null)
                        _resourceAdditions.Add((category.Value, null, copy));
                    continue;
                }
                foreach (var name in sourceCategory.Elements.KeyNames)
                {
                    var sourceEntry = sourceCategory.Elements[name];
                    var newName = name.Value;
                    if (targetCategory.Elements.ContainsKey(name.Value))
                    {
                        if (category.Value != "/Font" || IsSameFont(targetCategory.Elements[name], sourceEntry))
                            continue;
                        for (int suffix = 2; targetCategory.Elements.ContainsKey(newName) || sourceCategory.Elements.ContainsKey(newName)
                             || _resourceAdditions.Any(addition => addition.Category == "/Font" && addition.Name == newName); suffix++)
                        {
                            newName = $"{name.Value}_{suffix}";
                        }
                        fontNames.Add(name.Value.Substring(1), newName.Substring(1));
                    }
                    var entryCopy = MapItem(sourceEntry);
                    if (entryCopy != null)
                        _resourceAdditions.Add((category.Value, newName, entryCopy));
                }
            }

            if (fontNames.Count == 0)
                return;
            foreach (var field in _fieldCopies)
            {
                if (field.Elements[PdfForm.Keys.DA] is PdfString appearance)
                    field.Elements[PdfForm.Keys.DA] = new PdfString(RenameFonts(appearance.Value, fontNames));
            }
        }

        /// <summary>
        /// Whether a font in the document's form is the source form's font of the same name: the copy an earlier
        /// import made of it (the same source appended again), or a font that is not embedded with the same base font,
        /// type and encoding (the same form, opened from another file).
        /// </summary>
        bool IsSameFont(PdfItem? targetEntry, PdfItem? sourceEntry)
        {
            if (targetEntry is PdfReference target && sourceEntry is PdfReference source
                && _shared!.Contains(source.ObjectID) && _shared[source.ObjectID].ObjectID == target.ObjectID)
            {
                return true;
            }
            if (Resolve(targetEntry) is not PdfDictionary targetFont || Resolve(sourceEntry) is not PdfDictionary sourceFont)
                return false;
            if (targetFont.Elements.ContainsKey("/FontDescriptor") || sourceFont.Elements.ContainsKey("/FontDescriptor"))
                return false;
            if (targetFont.Elements.ContainsKey("/Encoding") != sourceFont.Elements.ContainsKey("/Encoding"))
                return false;
            return NameOf(targetFont, "/BaseFont") is { } baseFont && baseFont == NameOf(sourceFont, "/BaseFont")
                   && NameOf(targetFont, "/Subtype") == NameOf(sourceFont, "/Subtype")
                   && NameOf(targetFont, "/Encoding") == NameOf(sourceFont, "/Encoding");
        }

        static readonly Regex AppearanceFont = new(@"/([^\s/\[\]()<>{}%]+)(\s+[-+]?(?:\d+(?:\.\d*)?|\.\d+)\s+Tf)", RegexOptions.CultureInvariant);

        static string RenameFonts(string appearance, Dictionary<string, string> fontNames)
        {
            return AppearanceFont.Replace(appearance, Evaluate);

            string Evaluate(Match match)
            {
                if (fontNames.TryGetValue(match.Groups[1].Value, out var newName))
                    return "/" + newName + match.Groups[2].Value;
                return match.Value;
            }
        }

        void CommitFields()
        {
            var form = _target.Catalog.GetOrCreateAcroForm();
            var fields = form.Fields;
            foreach (var root in _fieldRoots)
                fields.Elements.Add(root);

            if (_formAppearance != null && !form.Elements.ContainsKey(PdfForm.Keys.DA))
                form.Elements[PdfForm.Keys.DA] = _formAppearance;
            if (_formQuadding != null && !form.Elements.ContainsKey(PdfForm.Keys.Q))
                form.Elements[PdfForm.Keys.Q] = new PdfInteger(_formQuadding.Value);
            if (_needAppearances)
                form.NeedAppearances = true;
            if (_signatureFlags != 0)
            {
                form.Elements.TryGetInteger(PdfForm.Keys.SigFlags, out var flags);
                form.Elements[PdfForm.Keys.SigFlags] = new PdfInteger(flags | _signatureFlags);
            }

            if (_allResources != null && !form.Elements.ContainsKey(PdfForm.Keys.DR))
                form.Elements[PdfForm.Keys.DR] = _allResources;
            if (Resolve(form.Elements[PdfForm.Keys.DR]) is PdfDictionary resources)
            {
                foreach (var (category, name, item) in _resourceAdditions)
                {
                    if (name == null)
                        resources.Elements[category] = item;
                    else if (Resolve(resources.Elements[category]) is PdfDictionary categoryDictionary)
                        categoryDictionary.Elements[name] = item;
                }
            }

            if (_calculationOrder.Count > 0)
            {
                if (Resolve(form.Elements[PdfForm.Keys.CO]) is not PdfArray order)
                {
                    order = new PdfArray(_target);
                    form.Elements[PdfForm.Keys.CO] = order;
                }
                foreach (var reference in _calculationOrder)
                    order.Elements.Add(reference);
            }
        }

        /// <summary>
        /// Reads the source's bookmarks. A bookmark that leads to a page of the source is kept with the destination on
        /// the appended page; if that page is not appended, it is kept without a destination only as the parent of
        /// kept bookmarks. A bookmark that leads elsewhere (a web address, a script, another file) is kept as it is; one
        /// that leads nowhere only as a parent.
        /// </summary>
        // PdfDictionary has reference equality, so the visited sets compare objects, not contents.
        List<Bookmark> ReadBookmarks(PdfDictionary parent, HashSet<PdfDictionary> visited)
        {
            var bookmarks = new List<Bookmark>();
            var item = Resolve(parent.Elements[PdfOutline.Keys.First]) as PdfDictionary;
            while (item != null && visited.Add(item))
            {
                var children = ReadBookmarks(item, visited);
                var target = DestinationOf(item);
                PdfItem? destination = target.Destination;
                PdfItem? action = null;
                if (!target.LeadsToSourcePage)
                {
                    action = MapItem(item.Elements[PdfOutline.Keys.A]);
                    if (action == null)
                        destination = MapItem(item.Elements[PdfOutline.Keys.Dest]);
                }
                if (destination != null || action != null || children.Count > 0)
                    bookmarks.Add(new Bookmark(item, destination, action, children));
                item = Resolve(item.Elements[PdfOutline.Keys.Next]) as PdfDictionary;
            }
            return bookmarks;
        }

        static void AddBookmark(PdfOutlineCollection parent, Bookmark bookmark)
        {
            var source = bookmark.Source;
            var outline = new PdfOutline();
            parent.Add(outline);
            source.Elements.TryGetString(PdfOutline.Keys.Title, out var title);
            outline.Title = title ?? "";
            outline.Opened = source.Elements.TryGetInteger(PdfOutline.Keys.Count, out var count) && count > 0;
            if (Resolve(source.Elements[PdfOutline.Keys.C]) is PdfArray { Elements.Count: 3 } colors
                && colors.Elements.TryGetReal(0, out var r) && colors.Elements.TryGetReal(1, out var g) && colors.Elements.TryGetReal(2, out var b))
            {
                outline.TextColor = PdfOutline.ColorFromComponents(r, g, b);
            }
            if (Resolve(source.Elements[PdfOutline.Keys.F]) is PdfInteger flags)
                outline.Elements[PdfOutline.Keys.F] = new PdfInteger(flags.Value);
            if (bookmark.Destination != null)
                outline.Elements[PdfOutline.Keys.Dest] = bookmark.Destination;
            if (bookmark.Action != null)
                outline.Elements[PdfOutline.Keys.A] = bookmark.Action;
            foreach (var child in bookmark.Children)
                AddBookmark(outline.Outlines, child);
        }

        /// <summary>
        /// Finds where a link, button or bookmark leads: its /Dest, or the /D of its GoTo action, with a named
        /// destination looked up in the source's /Dests dictionary and /Names /Dests tree. When it leads to a page of
        /// the source, the result says so and holds a copy of the destination on the appended page, or null if that
        /// page is not appended.
        /// </summary>
        (bool LeadsToSourcePage, PdfArray? Destination) DestinationOf(PdfDictionary dict)
        {
            var destination = Resolve(dict.Elements["/Dest"]);
            if (destination == null && Resolve(dict.Elements["/A"]) is PdfDictionary action && IsGoTo(action))
                destination = Resolve(action.Elements["/D"]);
            if (destination is PdfString or PdfName)
                destination = NamedDestination(destination);
            if (Resolve(destination) is not PdfArray { Elements.Count: > 0 } array
                || array.Elements[0] is not PdfReference pageReference || !_index!.SourcePages.Contains(pageReference.ObjectID))
            {
                return (false, null);
            }
            if (!_pages.TryGetValue(pageReference.ObjectID, out var page))
                return (true, null);

            var copy = new PdfArray(_target);
            copy.Elements.Add(page.RequiredReference);
            for (int index = 1; index < array.Elements.Count; index++)
            {
                // A destination's parameters are names, numbers and nulls.
                var parameter = Resolve(array.Elements[index]);
                if (parameter is PdfObject or null)
                    copy.Elements.Add(PdfNull.Value);
                else
                    copy.Elements.Add(parameter.Clone());
            }
            return (true, copy);
        }

        static bool IsGoTo(PdfItem? action)
            => action is PdfDictionary dict && NameOf(dict, "/S") == "/GoTo";

        PdfItem? NamedDestination(PdfItem name)
        {
            string key;
            if (name is PdfName pdfName)
                key = pdfName.Value.Substring(1);
            else
                key = ((PdfString)name).Value;
            if (Resolve(_source.Catalog.Elements["/Dests"]) is PdfDictionary dests)
            {
                var value = Resolve(dests.Elements["/" + key]);
                if (value != null)
                    return DestinationArray(value);
            }

            // Documents with links to named destinations often have thousands of both: the tree is read once.
            _index!.NamedDestinations ??= ReadNameTree((Resolve(_source.Catalog.Elements[PdfCatalog.Keys.Names]) as PdfDictionary)?.Elements["/Dests"]);
            if (_index.NamedDestinations.TryGetValue(key, out var named))
                return DestinationArray(named);
            return null;

            // A named destination is an array or a dictionary whose /D is the array.
            static PdfItem? DestinationArray(PdfItem? value)
            {
                if (value is PdfDictionary dict)
                    return Resolve(dict.Elements["/D"]);
                return value;
            }
        }

        /// <summary>
        /// Reads the entries of a name tree. The first entry of a name wins; loops through /Kids end at a node already
        /// read.
        /// </summary>
        static Dictionary<string, PdfItem?> ReadNameTree(PdfItem? root)
        {
            var entries = new Dictionary<string, PdfItem?>();
            var visited = new HashSet<PdfDictionary>();
            var nodes = new Stack<PdfItem?>();
            nodes.Push(root);
            while (nodes.Count > 0)
            {
                if (Resolve(nodes.Pop()) is not PdfDictionary node || !visited.Add(node))
                    continue;
                var names = ArrayItems(node.Elements["/Names"]).ToList();
                for (int index = 0; index + 1 < names.Count; index += 2)
                {
                    if (Resolve(names[index]) is PdfString name && !entries.ContainsKey(name.Value))
                        entries.Add(name.Value, Resolve(names[index + 1]));
                }
                foreach (var kid in ArrayItems(node.Elements["/Kids"]))
                    nodes.Push(kid);
            }
            return entries;
        }

        /// <summary>
        /// Gets the copy of an indirect source object, copying it when this import has not yet. Returns null for an
        /// object that must not be copied: a page that is not appended, the catalog, the page tree, the form or
        /// outline root, a field without a widget on an appended page, or a reference to nothing.
        /// </summary>
        PdfReference? MapReference(PdfReference reference, bool filterKids = false)
        {
            var id = reference.ObjectID;
            if (_clones.TryGetValue(id, out var done))
                return done;
            if (_pages.TryGetValue(id, out var page))
                return page.RequiredReference;
            var index = _index!;
            if (index.SourcePages.Contains(id) || index.Barriers.Contains(id) || (index.FieldNodes.Contains(id) && !_keptFieldNodes.Contains(id)))
                return null;
            if (reference.Value is not PdfObject obj)
                return null;
            // A page that is not in the page tree.
            if (obj is PdfDictionary dict && NameOf(dict, "/Type") == "/Page")
                return null;
            // A font, image or other resource the page content (or an earlier import of this source) already brought
            // along is shared, as AddPage shares them between pages.
            if (_shared!.Contains(id) && IsSharedResource(obj))
                return _shared[id];

            var copy = NewContainer(obj);
            _target.IrefTable.Add(copy);
            _clones[id] = copy.Reference!;
            // Registered when the import is attached. Form XObjects are not registered: they are appearance streams
            // here, which belong to their annotation.
            if (IsSharedResource(obj) && !(obj is PdfDictionary resource && NameOf(resource, "/Subtype") == "/Form"))
                _sharedCopies.Add((id, copy.Reference!));
            bool isField = index.FieldNodes.Contains(id);
            if (isField)
                _fieldCopies.Add((PdfDictionary)copy);
            _pending.Enqueue(new PendingCopy(obj, copy, isField, filterKids));
            return copy.Reference;
        }

        /// <summary>
        /// Copies a source item: an indirect object through <see cref="MapReference"/>, a direct dictionary or array
        /// in place, a primitive as it is.
        /// </summary>
        PdfItem? MapItem(PdfItem? item, bool filterKids = false)
        {
            if (item is PdfReference reference)
                return MapReference(reference, filterKids);
            if (item is PdfObject obj)
            {
                var copy = NewContainer(obj);
                Fill(new PendingCopy(obj, copy, false, filterKids));
                return copy;
            }
            return item;
        }

        /// <summary>
        /// Creates an empty plain dictionary or array for a copy. The source's objects may be typed (form fields,
        /// widget annotations and their shared proxies); copies are plain, and the fields and annotations among them
        /// are typed afterwards as reading a document types them.
        /// </summary>
        PdfObject NewContainer(PdfObject source)
        {
            if (source is PdfDictionary)
                return new PdfDictionary(_target);
            if (source is PdfArray)
                return new PdfArray(_target);
            var copy = source.Clone();
            copy.Document = _target;
            return copy;
        }

        void ProcessPending()
        {
            while (_pending.Count > 0)
                Fill(_pending.Dequeue());
        }

        /// <summary>
        /// Fills a copy with the entries of its source. References become references to copies; a reference that must
        /// not be copied is left out of a dictionary and becomes null in an array. The /Kids of a copied field keep
        /// only the kept fields and widgets.
        /// </summary>
        void Fill(PendingCopy pending)
        {
            if (pending.Source is PdfDictionary source && pending.Copy is PdfDictionary copy)
            {
                foreach (var key in source.Elements.KeyNames)
                {
                    var item = MapItem(source.Elements[key], pending.IsField && key.Value == "/Kids");
                    if (item != null)
                        copy.Elements[key] = item;
                }
                if (source.Stream != null)
                {
                    var stream = source.Stream.Clone();
                    stream.ChangeOwner(copy);
                    copy.Stream = stream;
                }
            }
            else if (pending.Source is PdfArray sourceArray && pending.Copy is PdfArray copyArray)
            {
                foreach (var item in sourceArray.Elements)
                {
                    if (pending.FilterKids && item is PdfReference kid && _index!.FieldNodes.Contains(kid.ObjectID) && !_keptFieldNodes.Contains(kid.ObjectID))
                        continue;
                    copyArray.Elements.Add(MapItem(item) ?? PdfNull.Value);
                }
            }
        }

        /// <summary>
        /// Types the copied fields, their widgets and the other copied annotations, as reading a document types them:
        /// the fields first, then the widgets of every page, then the other annotations of every page.
        /// </summary>
        void TypeCopies()
        {
            if (_fieldRoots.Count > 0)
            {
                var roots = new PdfArray(_target);
                foreach (var root in _fieldRoots)
                    roots.Elements.Add(root);
                PdfFormFields.AcroFieldPreparer.PrepareFields(roots);
            }
            foreach (var (_, page) in _newPages)
                PdfFormFields.AcroFieldPreparer.PrepareWidgets(page);
            foreach (var (_, page) in _newPages)
                PdfAnnotations.AnnotationPreparer.PreparePage(page);
        }

        /// <summary>
        /// Whether an object is a resource that copies can share: a font and its parts, an image, a form XObject, a
        /// graphics state, a colour space, a pattern, a shading or a function. Nothing in these leads to a page, and
        /// they are not changed after copying.
        /// </summary>
        static bool IsSharedResource(PdfObject obj)
        {
            if (obj is PdfArray array)
                return array.Elements.Count > 0 && array.Elements[0] is PdfName family && ColourSpaceFamilies.Contains(family.Value);
            if (obj is not PdfDictionary dict)
                return false;
            var type = NameOf(dict, "/Type");
            var subtype = NameOf(dict, "/Subtype");
            if (type is "/Font" or "/FontDescriptor" or "/ExtGState")
                return true;
            if (subtype is "/Image" or "/Form" or "/Type1C" or "/CIDFontType0C" or "/OpenType")
                return true;
            if (dict.Elements.ContainsKey("/Length1") || dict.Elements.ContainsKey("/PatternType")
                || dict.Elements.ContainsKey("/ShadingType") || dict.Elements.ContainsKey("/FunctionType"))
            {
                return true;
            }
            // An ICC profile stream.
            return dict.Stream != null && dict.Elements.ContainsKey("/N") && type == null && subtype == null;
        }

        static readonly HashSet<string> ColourSpaceFamilies =
            ["/ICCBased", "/Indexed", "/Separation", "/DeviceN", "/CalRGB", "/CalGray", "/Lab", "/Pattern"];

        /// <summary>
        /// Gets a name entry, or null if the entry is missing or not a name: copied files can be damaged, and a
        /// /Subtype that is a number must not stop the import.
        /// </summary>
        static string? NameOf(PdfDictionary dict, string key) => Resolve(dict.Elements[key]) switch
        {
            PdfName name => name.Value,
            PdfNameObject nameObject => nameObject.Value,
            _ => null
        };

        /// <summary>
        /// Gets an integer entry, or 0 if it is missing or not an integer.
        /// </summary>
        static int IntegerOf(PdfDictionary dict, string key)
        {
            if (Resolve(dict.Elements[key]) is PdfInteger integer)
                return integer.Value;
            return 0;
        }

        static PdfItem? Resolve(PdfItem? item)
        {
            if (item is PdfReference reference)
                return reference.Value;
            return item;
        }

        static IEnumerable<PdfItem> ArrayItems(PdfItem? item)
        {
            if (Resolve(item) is PdfArray array)
                return array.Elements.ToList();
            return [];
        }

        sealed record Bookmark(PdfDictionary Source, PdfItem? Destination, PdfItem? Action, List<Bookmark> Children);

        // A copy waiting to be filled. IsField: the source is a field node, whose /Kids are filtered. FilterKids: the
        // source is the /Kids array of a field node.
        sealed record PendingCopy(PdfObject Source, PdfObject Copy, bool IsField, bool FilterKids);

        /// <summary>
        /// What an import needs to know about a source document as a whole: its pages, the objects a copy must not
        /// pull in, its field nodes and its named destinations. Kept with the source, because splitting a document
        /// page by page imports from it once per page; a document opened for import is not changed.
        /// </summary>
        internal sealed class SourceIndex
        {
            public static SourceIndex For(PdfDocument source)
                => source.PageImportIndex ??= new SourceIndex(source);

            SourceIndex(PdfDocument source)
            {
                foreach (var page in source.Pages)
                    SourcePages.Add(page.ObjectID);
                var form = Resolve(source.Catalog.Elements[PdfCatalog.Keys.AcroForm]) as PdfDictionary;
                AddBarrier(source.Catalog);
                AddBarrier(source.Pages);
                AddBarrier(form);
                AddBarrier(Resolve(source.Catalog.Elements[PdfCatalog.Keys.Outlines]) as PdfObject);
                if (form == null)
                    return;

                var stack = new Stack<PdfItem>(ArrayItems(form.Elements[PdfForm.Keys.Fields]));
                while (stack.Count > 0)
                {
                    if (stack.Pop() is not PdfReference reference || reference.Value is not PdfDictionary node || !FieldNodes.Add(reference.ObjectID))
                        continue;
                    foreach (var kid in ArrayItems(node.Elements["/Kids"]))
                        stack.Push(kid);
                }
            }

            void AddBarrier(PdfObject? obj)
            {
                if (obj?.Reference != null)
                    Barriers.Add(obj.ObjectID);
            }

            public HashSet<PdfObjectID> SourcePages { get; } = [];
            public HashSet<PdfObjectID> Barriers { get; } = [];
            public HashSet<PdfObjectID> FieldNodes { get; } = [];
            public Dictionary<string, PdfItem?>? NamedDestinations { get; set; }
        }

        readonly PdfDocument _target;
        readonly PdfDocument _source;
        readonly PdfPageImportOptions _options;
        SourceIndex? _index;
        // The document's table of objects imported from the source, shared with AddPage and the page content import.
        PdfImportedObjectTable? _shared;
        PdfDictionary? _sourceForm;

        // Appended pages by the source page's object ID, and in order with their source pages.
        readonly Dictionary<PdfObjectID, PdfPage> _pages = [];
        readonly List<(PdfPage Source, PdfPage Page)> _newPages = [];
        // Copies made by this import by the source object's ID. Each import copies anew, so a source appended twice
        // gets two independent sets of fields.
        readonly Dictionary<PdfObjectID, PdfReference> _clones = [];
        readonly List<(PdfObjectID Id, PdfReference Copy)> _sharedCopies = [];
        readonly HashSet<PdfObjectID> _keptFieldNodes = [];
        readonly List<PdfDictionary> _fieldCopies = [];
        readonly Queue<PendingCopy> _pending = new();

        // Staged for the document's form.
        readonly List<PdfReference> _fieldRoots = [];
        readonly Dictionary<string, string> _renamedFields = [];
        PdfString? _formAppearance;
        int? _formQuadding;
        bool _needAppearances;
        int _signatureFlags;
        PdfItem? _allResources;
        readonly List<(string Category, string? Name, PdfItem Item)> _resourceAdditions = [];
        readonly List<PdfReference> _calculationOrder = [];
        List<Bookmark> _bookmarks = [];
    }
}
