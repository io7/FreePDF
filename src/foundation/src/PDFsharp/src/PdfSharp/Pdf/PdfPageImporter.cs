// FreePDF - MIT licence. See the LICENSE file in the solution root.

using PdfSharp.Drawing;
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
    /// another page comes along. Links and bookmarks get explicit destinations on the appended pages; those that lead
    /// to a page that is not appended are left out.
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
        /// whose target does not exist yet.
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
            if (startIndex < 0 || startIndex >= source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex, $"The document has {source.PageCount} pages.");
            if (pageCount < 1 || startIndex + pageCount > source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, $"The document has {source.PageCount} pages from index {startIndex}.");
            if (target?.UAManager != null)
                throw new InvalidOperationException("Pages cannot be imported into a PDF/UA document.");
        }

        internal static PdfPageImportResult Import(PdfDocument target, PdfDocument source, int startIndex, int pageCount, PdfPageImportOptions options)
        {
            Validate(target, source, startIndex, pageCount, options);
            return new PdfPageImporter(target, source, options).Run(startIndex, pageCount);
        }

        PdfPageImportResult Run(int startIndex, int pageCount)
        {
            var sourceForm = Resolve(_source.Catalog.Elements[PdfCatalog.Keys.AcroForm]) as PdfDictionary;
            CollectBarriers(sourceForm);
            _shared = _target.FormTable.GetImportedObjectTable(_source.Pages[startIndex]);

            var sourcePages = new List<PdfPage>();
            var pages = new List<PdfPage>();
            for (int index = startIndex; index < startIndex + pageCount; index++)
            {
                var sourcePage = _source.Pages[index];
                var page = _target.Pages.AppendImportedPageWithoutAnnotations(sourcePage);
                _pages[sourcePage.ObjectID] = page;
                sourcePages.Add(sourcePage);
                pages.Add(page);
            }

            CollectKeptFields(sourcePages);
            for (int index = 0; index < pages.Count; index++)
                CopyAnnotations(sourcePages[index], pages[index]);

            var renamedFields = new Dictionary<string, string>();
            var fieldRoots = new List<PdfReference>();
            if (sourceForm != null)
                fieldRoots = AddFields(sourceForm, renamedFields);
            TypeCopies(fieldRoots, pages, _target);

            if (_options.KeepBookmarks || _options.BookmarkTitle != null)
                AddBookmarks(pages[0]);

            return new PdfPageImportResult(pages, renamedFields);
        }

        /// <summary>
        /// Collects the source objects a copied annotation must not pull in: every page (an appended page is mapped,
        /// the others are dropped), the catalog, the page tree, the form, the outline and every field node.
        /// </summary>
        void CollectBarriers(PdfDictionary? sourceForm)
        {
            foreach (var page in _source.Pages)
                _sourcePages.Add(page.ObjectID);
            AddBarrier(_source.Catalog);
            AddBarrier(_source.Pages);
            AddBarrier(sourceForm);
            AddBarrier(Resolve(_source.Catalog.Elements[PdfCatalog.Keys.Outlines]) as PdfObject);

            if (sourceForm == null)
                return;
            var stack = new Stack<PdfItem>(ArrayItems(sourceForm.Elements[PdfForm.Keys.Fields]));
            while (stack.Count > 0)
            {
                if (stack.Pop() is not PdfReference reference || reference.Value is not PdfDictionary node || !_fieldNodes.Add(reference.ObjectID))
                    continue;
                foreach (var kid in ArrayItems(node.Elements["/Kids"]))
                    stack.Push(kid);
            }

            void AddBarrier(PdfObject? obj)
            {
                if (obj?.Reference != null)
                    _barriers.Add(obj.ObjectID);
            }
        }

        /// <summary>
        /// Keeps the field nodes that have a widget on an appended page, and the ancestors of those nodes.
        /// </summary>
        void CollectKeptFields(List<PdfPage> sourcePages)
        {
            foreach (var sourcePage in sourcePages)
            {
                foreach (var item in ArrayItems(sourcePage.Elements[PdfPage.Keys.Annots]))
                {
                    var current = item as PdfReference;
                    while (current != null && _fieldNodes.Contains(current.ObjectID) && _keptFieldNodes.Add(current.ObjectID))
                        current = (current.Value as PdfDictionary)?.Elements["/Parent"] as PdfReference;
                }
            }
        }

        void CopyAnnotations(PdfPage sourcePage, PdfPage page)
        {
            var annotations = new PdfArray(_target);
            var links = new List<(PdfDictionary Link, PdfArray Destination)>();
            foreach (var item in ArrayItems(sourcePage.Elements[PdfPage.Keys.Annots]))
            {
                if (Resolve(item) is not PdfDictionary annotation)
                    continue;

                PdfArray? destination = null;
                if (NameOf(annotation, "/Subtype") == "/Link")
                {
                    var target = DestinationOf(annotation);
                    if (target.LeadsToSourcePage)
                    {
                        destination = target.Destination;
                        // The page it leads to is not appended.
                        if (destination == null)
                            continue;
                    }
                }

                var copy = MapItem(item);
                if (copy == null)
                    continue;
                annotations.Elements.Add(copy);
                if (destination != null)
                    links.Add(((PdfDictionary)Resolve(copy)!, destination));
            }
            ProcessPending();

            // The copied link still holds the source's destination; it gets the one on the appended page.
            foreach (var (link, destination) in links)
            {
                link.Elements.Remove("/A");
                link.Elements["/Dest"] = destination;
            }

            if (annotations.Elements.Count > 0)
                page.Elements[PdfPage.Keys.Annots] = annotations;
        }

        /// <summary>
        /// Adds the kept top-level fields to the document's form, renaming those whose name is taken, and carries over
        /// the form entries the fields depend on. XFA is not carried: it would describe the source's form only.
        /// </summary>
        List<PdfReference> AddFields(PdfDictionary sourceForm, Dictionary<string, string> renamedFields)
        {
            var roots = new List<PdfReference>();
            foreach (var item in ArrayItems(sourceForm.Elements[PdfForm.Keys.Fields]))
            {
                if (item is PdfReference reference && _keptFieldNodes.Contains(reference.ObjectID) && _clones.TryGetValue(reference.ObjectID, out var copy))
                    roots.Add(copy);
            }
            if (roots.Count == 0)
                return roots;

            var form = _target.Catalog.GetOrCreateAcroForm();
            var fields = form.Fields;
            var names = new HashSet<string>();
            foreach (var item in fields.Elements)
            {
                if (Resolve(item) is PdfDictionary field && field.Elements.TryGetString("/T", out var name))
                    names.Add(name);
            }
            foreach (var root in roots)
            {
                var field = (PdfDictionary)root.Value;
                if (field.Elements.TryGetString("/T", out var name))
                {
                    var newName = name;
                    for (int suffix = 2; names.Contains(newName); suffix++)
                        newName = $"{name}_{suffix}";
                    if (newName != name)
                    {
                        // ASCII names stay plain strings, as most forms write them.
                        var encoding = PdfStringEncoding.Unicode;
                        if (newName.All(ch => ch < 128))
                            encoding = PdfStringEncoding.PDFDocEncoding;
                        field.Elements["/T"] = new PdfString(newName, encoding);
                        if (!renamedFields.ContainsKey(name))
                            renamedFields.Add(name, newName);
                    }
                    names.Add(newName);
                }
                fields.Elements.Add(root);
            }

            if (!form.Elements.ContainsKey(PdfForm.Keys.DA) && sourceForm.Elements[PdfForm.Keys.DA] is PdfString defaultAppearance)
                form.Elements[PdfForm.Keys.DA] = defaultAppearance.Clone();
            if (!form.Elements.ContainsKey(PdfForm.Keys.Q) && Resolve(sourceForm.Elements[PdfForm.Keys.Q]) is PdfInteger quadding)
                form.Elements[PdfForm.Keys.Q] = new PdfInteger(quadding.Value);
            if (Resolve(sourceForm.Elements[PdfForm.Keys.NeedAppearances]) is PdfBoolean { Value: true })
                form.NeedAppearances = true;
            if (Resolve(sourceForm.Elements[PdfForm.Keys.SigFlags]) is PdfInteger sigFlags)
            {
                form.Elements.TryGetInteger(PdfForm.Keys.SigFlags, out var flags);
                form.Elements[PdfForm.Keys.SigFlags] = new PdfInteger(flags | sigFlags.Value);
            }
            AddResources(form, sourceForm);
            AddCalculationOrder(form, sourceForm);
            ProcessPending();
            return roots;
        }

        /// <summary>
        /// Adds the source form's default resources that the document's form does not have yet, by name.
        /// </summary>
        void AddResources(PdfDictionary form, PdfDictionary sourceForm)
        {
            var sourceItem = sourceForm.Elements[PdfForm.Keys.DR];
            if (Resolve(sourceItem) is not PdfDictionary sourceResources)
                return;
            if (Resolve(form.Elements[PdfForm.Keys.DR]) is not PdfDictionary resources)
            {
                var copy = MapItem(sourceItem);
                if (copy != null)
                    form.Elements[PdfForm.Keys.DR] = copy;
                return;
            }
            foreach (var category in sourceResources.Elements.KeyNames)
            {
                var sourceCategoryItem = sourceResources.Elements[category];
                if (Resolve(sourceCategoryItem) is not PdfDictionary sourceCategory)
                    continue;
                if (Resolve(resources.Elements[category]) is not PdfDictionary categoryDictionary)
                {
                    var copy = MapItem(sourceCategoryItem);
                    if (copy != null)
                        resources.Elements[category] = copy;
                    continue;
                }
                foreach (var name in sourceCategory.Elements.KeyNames)
                {
                    if (categoryDictionary.Elements.ContainsKey(name.Value))
                        continue;
                    var copy = MapItem(sourceCategory.Elements[name]);
                    if (copy != null)
                        categoryDictionary.Elements[name] = copy;
                }
            }
        }

        void AddCalculationOrder(PdfDictionary form, PdfDictionary sourceForm)
        {
            var calculated = ArrayItems(sourceForm.Elements[PdfForm.Keys.CO])
                .OfType<PdfReference>()
                .Where(reference => _clones.ContainsKey(reference.ObjectID))
                .Select(reference => _clones[reference.ObjectID])
                .ToList();
            if (calculated.Count == 0)
                return;
            if (Resolve(form.Elements[PdfForm.Keys.CO]) is not PdfArray order)
            {
                order = new PdfArray(_target);
                form.Elements[PdfForm.Keys.CO] = order;
            }
            foreach (var reference in calculated)
                order.Elements.Add(reference);
        }

        /// <summary>
        /// Adds the source's bookmarks that lead to appended pages, keeping a bookmark without such a destination only
        /// as the parent of bookmarks that have one.
        /// </summary>
        void AddBookmarks(PdfPage firstPage)
        {
            var bookmarks = new List<Bookmark>();
            if (_options.KeepBookmarks && Resolve(_source.Catalog.Elements[PdfCatalog.Keys.Outlines]) is PdfDictionary outlineRoot)
                bookmarks = ReadBookmarks(outlineRoot, []);

            var parent = _target.Outlines;
            if (_options.BookmarkTitle != null)
                parent = _target.Outlines.Add(_options.BookmarkTitle, firstPage).Outlines;
            foreach (var bookmark in bookmarks)
                AddBookmark(parent, bookmark);
        }

        // PdfDictionary has reference equality, so the visited sets compare objects, not contents.
        List<Bookmark> ReadBookmarks(PdfDictionary parent, HashSet<PdfDictionary> visited)
        {
            var bookmarks = new List<Bookmark>();
            var item = Resolve(parent.Elements[PdfOutline.Keys.First]) as PdfDictionary;
            while (item != null && visited.Add(item))
            {
                var children = ReadBookmarks(item, visited);
                var destination = DestinationOf(item).Destination;
                if (destination != null || children.Count > 0)
                    bookmarks.Add(new Bookmark(item, destination, children));
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
                outline.TextColor = XColor.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
            }
            if (Resolve(source.Elements[PdfOutline.Keys.F]) is PdfInteger flags)
                outline.Elements[PdfOutline.Keys.F] = new PdfInteger(flags.Value);
            if (bookmark.Destination != null)
                outline.Elements[PdfOutline.Keys.Dest] = bookmark.Destination;
            foreach (var child in bookmark.Children)
                AddBookmark(outline.Outlines, child);
        }

        /// <summary>
        /// Finds where a link or bookmark leads: its /Dest, or the /D of its GoTo action, with a named destination
        /// looked up in the source's /Dests dictionary and /Names /Dests tree. When it leads to a page of the source,
        /// the result says so and holds a copy of the destination on the appended page, or null if that page is not
        /// appended.
        /// </summary>
        (bool LeadsToSourcePage, PdfArray? Destination) DestinationOf(PdfDictionary dict)
        {
            var destination = Resolve(dict.Elements["/Dest"]);
            if (destination == null && Resolve(dict.Elements["/A"]) is PdfDictionary action && NameOf(action, "/S") == "/GoTo")
                destination = Resolve(action.Elements["/D"]);
            if (destination is PdfString or PdfName)
                destination = NamedDestination(destination);
            if (Resolve(destination) is not PdfArray { Elements.Count: > 0 } array
                || array.Elements[0] is not PdfReference pageReference || !_sourcePages.Contains(pageReference.ObjectID))
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

            // Documents with links to named destinations often have thousands of both: read the tree once.
            _namedDestinations ??= ReadNameTree((Resolve(_source.Catalog.Elements[PdfCatalog.Keys.Names]) as PdfDictionary)?.Elements["/Dests"]);
            if (_namedDestinations.TryGetValue(key, out var named))
                return DestinationArray(named);
            return null;

            // A named destination is an array or a dictionary whose /D is the array.
            static PdfItem? DestinationArray(PdfItem? value)
                => value is PdfDictionary dict ? Resolve(dict.Elements["/D"]) : value;
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
            if (_sourcePages.Contains(id) || _barriers.Contains(id) || (_fieldNodes.Contains(id) && !_keptFieldNodes.Contains(id)))
                return null;
            if (reference.Value is not PdfObject obj)
                return null;
            // A page that is not in the page tree.
            if (obj is PdfDictionary dict && NameOf(dict, "/Type") == "/Page")
                return null;
            // A font or image the page content (or an earlier import of this source) already brought along is
            // shared, as AddPage shares them between pages. Annotations, fields and actions are always copied: they
            // are changed after copying or lead to pages of this import.
            if (_shared!.Contains(id) && !_fieldNodes.Contains(id) && !IsAnnotationOrAction(obj))
                return _shared[id];

            var copy = NewContainer(obj);
            _target.IrefTable.Add(copy);
            _clones[id] = copy.Reference!;
            if (IsFontOrImage(obj))
                _shared.Add(id, copy.Reference!);
            _pending.Enqueue(new PendingCopy(obj, copy, _fieldNodes.Contains(id), filterKids));
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
                    if (pending.FilterKids && item is PdfReference kid && _fieldNodes.Contains(kid.ObjectID) && !_keptFieldNodes.Contains(kid.ObjectID))
                        continue;
                    copyArray.Elements.Add(MapItem(item) ?? PdfNull.Value);
                }
            }
        }

        /// <summary>
        /// Types the copied fields, their widgets and the other copied annotations, as reading a document types them.
        /// </summary>
        static void TypeCopies(List<PdfReference> fieldRoots, List<PdfPage> pages, PdfDocument target)
        {
            if (fieldRoots.Count > 0)
            {
                var roots = new PdfArray(target);
                foreach (var root in fieldRoots)
                    roots.Elements.Add(root);
                PdfFormFields.AcroFieldPreparer.PrepareFields(roots);
            }
            foreach (var page in pages)
            {
                PdfFormFields.AcroFieldPreparer.PrepareWidgets(page);
                PdfAnnotations.AnnotationPreparer.PreparePage(page);
            }
        }

        static bool IsAnnotationOrAction(PdfObject obj)
        {
            if (obj is not PdfDictionary dict)
                return false;
            if (NameOf(dict, "/Type") == "/Annot" || dict.Elements.ContainsKey("/S"))
                return true;
            return dict.Elements.ContainsKey("/Subtype") && dict.Elements.ContainsKey("/Rect");
        }

        static bool IsFontOrImage(PdfObject obj)
            => obj is PdfDictionary dict && (NameOf(dict, "/Type") == "/Font" || NameOf(dict, "/Subtype") == "/Image");

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

        static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

        static IEnumerable<PdfItem> ArrayItems(PdfItem? item)
            => Resolve(item) is PdfArray array ? array.Elements.ToList() : [];

        sealed record Bookmark(PdfDictionary Source, PdfArray? Destination, List<Bookmark> Children);

        // A copy waiting to be filled. IsField: the source is a field node, whose /Kids are filtered. FilterKids: the
        // source is the /Kids array of a field node.
        sealed record PendingCopy(PdfObject Source, PdfObject Copy, bool IsField, bool FilterKids);

        readonly PdfDocument _target;
        readonly PdfDocument _source;
        readonly PdfPageImportOptions _options;

        // Appended pages by the source page's object ID.
        readonly Dictionary<PdfObjectID, PdfPage> _pages = [];
        // Copies made by this import by the source object's ID. Each import copies anew, so a source appended twice
        // gets two independent sets of fields.
        readonly Dictionary<PdfObjectID, PdfReference> _clones = [];
        readonly HashSet<PdfObjectID> _sourcePages = [];
        readonly HashSet<PdfObjectID> _barriers = [];
        readonly HashSet<PdfObjectID> _fieldNodes = [];
        readonly HashSet<PdfObjectID> _keptFieldNodes = [];
        readonly Queue<PendingCopy> _pending = new();
        // The document's table of objects imported from the source, shared with AddPage and the page content import.
        PdfImportedObjectTable? _shared;
        // The source's /Names /Dests tree, read on first use.
        Dictionary<string, PdfItem?>? _namedDestinations;
    }
}
