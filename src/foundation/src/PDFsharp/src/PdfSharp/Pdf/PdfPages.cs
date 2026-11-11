// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Collections;
using PdfSharp.Internal;
using PdfSharp.Events;
using PdfSharp.Logging;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;

// v7.0.0 TODO review, delete test code.

namespace PdfSharp.Pdf
{
    /// <summary>
    /// Represents the root of the page tree of the document.
    /// </summary>
    //[DebuggerDisplay("(PageCount={" + nameof(Count) + "})")]
    public sealed class PdfPages : PdfPageTreeNode, IEnumerable<PdfPage>
    {
        // Reference 2.0: 7.7.3.2  Page tree nodes / Page 102
        // PdfPages is the root of the page tree.

        internal PdfPages(PdfDocument document)
            : base(document)
        { }

        /// <summary>
        /// Initializes a new instance of this class using the elements of the specified dictionary.
        /// After this type transformation the specified dictionary is dead and cannot be used anymore.
        /// </summary>
        internal PdfPages(PdfDictionary dictionary)
            : base(dictionary)
        { }

        /// <summary>
        /// Gets the number of pages.
        /// </summary>
        //public int Count => PagesArray.Elements.Count; // This can be wrong in Import mode.
        //public int Count => Document.PageCount; // Slower, but also works in Import mode.
        public new int Count // TODO Remove and use Count from base class.
        {
            get
            {
                // TODO Delete test code.
                //var count1 = Document.CanModify
                //    ? PagesArray.Elements.Count // Only valid in Modify mode.
                //    : Document.PageCount; // Valid in Import mode.

                var count2 = base.Count;


                var result = PagesArray.Elements.Count;
                Debug.Assert(count2 == result);
                return result;
            }
        }

        /// <summary>
        /// Gets the page with the specified index.
        /// </summary>
        public PdfPage this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, PsMsgs.PageIndexOutOfRange);

                // Is document modifiable and page tree flattened?
                if (_flattenedPages == null)
                    return PagesArray.Elements.GetRequiredDictionary<PdfPage>(index);

                Debug.Assert(!Document.CanModify || Document.OpenMode == PdfDocumentOpenMode.Append);
                return _flattenedPages.Elements.GetRequiredDictionary<PdfPage>(index);
            }
        }

        /// <summary>
        /// Finds a page by its id. Transforms it to PdfPage if necessary.
        /// </summary>
        internal PdfPage? FindPage(PdfObjectID id)
        {
            PdfPage? page = null;
            foreach (var item in PagesArray)
            {
                if (item is PdfReference iref)
                {
                    if (iref.Value is PdfDictionary dictionary && dictionary.ObjectID == id)
                    {
                        page = dictionary as PdfPage ?? new PdfPage(dictionary);
                        break;
                    }
                }
            }
            return page;
        }

        /// <summary>
        /// Creates a new PdfPage, adds it to the end of this document, and returns it.
        /// </summary>
        public PdfPage Add()
        {
            var page = new PdfPage();
            Insert(Count, page);
            return page;
        }

        /// <summary>
        /// Adds the specified PdfPage to the end of this document and maybe returns a new PdfPage object.
        /// The value returned is a new object if the added page comes from a foreign document.
        /// </summary>
        public PdfPage Add(PdfPage page)
            => Insert(Count, page);

        /// <summary>
        /// Creates a new PdfPage, inserts it at the specified position into this document, and returns it.
        /// </summary>
        public PdfPage Insert(int index)
        {
            var page = new PdfPage();
            Insert(index, page);
            return page;
        }

        /// <summary>
        /// Inserts the specified PdfPage at the specified position to this document and maybe returns a new PdfPage object.
        /// The value returned is a new object if the inserted page comes from a foreign document.
        /// </summary>
        public PdfPage Insert(int index, PdfPage page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            // Is the page already owned by this document?
            if (page.Owner == Owner)
            {
                // Case: Page is first removed and then inserted again, maybe at another position.

                int count = Count;
                // Check if page is not already part of the document.
                for (int idx = 0; idx < count; idx++)
                {
                    if (ReferenceEquals(this[idx], page))
                        throw new InvalidOperationException(PsMsgs.MultiplePageInsert);
                }

                // Because the owner of the inserted page is this document we assume that the page was
                // former already a part of it, and is therefore well-defined. Removing it left it in the table.
                Owner.IrefTable.TryAdd(page);
                Debug.Assert(page.Owner == Owner);

                InsertIntoTree(index, page);

                // #PDF-UA: Pages must not be moved.
                if (Document.UAManager != null)
                    Document.Events.OnPageAdded(Document, new PageEventArgs(Document) { Page = page, PageIndex = index, EventType = PageEventType.Moved });

                PdfSharpLogHost.Logger.ExistingPdfPageAdded(Document?.Name);

                return page;
            }

            // All new page insertions come here.
            if (page.Owner == null!)
            {
                // Case: New page was newly created and inserted now.
                page.Document = Owner;

                Owner.IrefTable.Add(page);
                Debug.Assert(page.Owner == Owner);
                InsertIntoTree(index, page);

                // #PDF-UA: Page was created.
                if (Document.UAManager != null)
                    Document.Events.OnPageAdded(Document, new PageEventArgs(Document) { Page = page, PageIndex = index, EventType = PageEventType.Created });
            }
            else
            {
                // Case: Page is from an external document -> import it.
                PdfPage importPage = page;
                page = ImportExternalPage(importPage);
                //Owner.IrefTable.Add(page); // DELETE Pages are now always indirect

                // Add page substitute to importedObjectTable.
                PdfImportedObjectTable importedObjectTable = Owner.FormTable.GetImportedObjectTable(importPage);
                importedObjectTable.Add(importPage.ObjectID, page.RequiredReference);

                InsertIntoTree(index, page);
                PdfAnnotations.FixImportedAnnotation(page);

                // #PDF-UA: Page was imported.
                if (Document.UAManager != null)
                    Document.Events.OnPageAdded(Document, new PageEventArgs(Document) { Page = page, PageIndex = index, EventType = PageEventType.Imported });
            }

            PdfSharpLogHost.Logger.NewPdfPageCreated(Document?.Name);

            if (Owner.Settings.TrimMargins.AreSet)
                page.TrimMargins = Owner.Settings.TrimMargins;

            return page;
        }

        /// <summary>
        /// Inserts  pages of the specified document into this document.
        /// </summary>
        /// <param name="index">The index in this document where to insert the page .</param>
        /// <param name="document">The document to be inserted.</param>
        /// <param name="startIndex">The index of the first page to be inserted.</param>
        /// <param name="pageCount">The number of pages to be inserted.</param>
        public void InsertRange(int index, PdfDocument document, int startIndex, int pageCount)
        {
            // #PDF-UA
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (index < 0 || index > Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Argument 'index' out of range.");

            int importDocumentPageCount = document.PageCount;

            if (startIndex < 0 || startIndex + pageCount > importDocumentPageCount)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Argument 'startIndex' out of range.");

            if (pageCount > importDocumentPageCount)
                throw new ArgumentOutOfRangeException(nameof(pageCount), "Argument 'pageCount' out of range.");

            var insertPages = new PdfPage[pageCount];
            var importPages = new PdfPage[pageCount];

            // 1st create all new pages.
            for (int idx = 0, insertIndex = index, importIndex = startIndex;
                importIndex < startIndex + pageCount;
                idx++, insertIndex++, importIndex++)
            {
                var importPage = document.Pages[importIndex];
                var page = ImportExternalPage(importPage);
                insertPages[idx] = page;
                importPages[idx] = importPage;

                // Add page substitute to importedObjectTable.
                var importedObjectTable = Owner.FormTable.GetImportedObjectTable(importPage);
                importedObjectTable.Add(importPage.ObjectID, page.RequiredReference);

                InsertIntoTree(insertIndex, page);

                if (Owner.Settings.TrimMargins.AreSet)
                    page.TrimMargins = Owner.Settings.TrimMargins;
            }

            // 2nd copy link annotations that are in the range of the imported pages.
            for (int idx = 0, importIndex = startIndex;
                importIndex < startIndex + pageCount;
                idx++, importIndex++)
            {
                var importPage = document.Pages[importIndex];
                var page = insertPages[idx];

                // Get annotations.
                var annots = importPage.Elements.GetArray(PdfPage.Keys.Annots);
                if (annots != null)
                {
                    var annotations = page.Annotations;

                    // Loop through annotations.
                    int count = annots.Elements.Count;
                    for (int idxAnnotation = 0; idxAnnotation < count; idxAnnotation++)
                    {
                        var annot = annots.Elements.GetDictionary(idxAnnotation);
                        if (annot != null)
                        {
                            string subtype = annot.Elements.GetString(PdfAnnotation.Keys.Subtype);
                            if (subtype == "/Link")
                            {
                                bool addAnnotation = false;
                                var newAnnotation = new PdfLinkAnnotation(Owner);

                                PdfName[] importAnnotationKeyNames = annot.Elements.KeyNames;
                                foreach (var pdfItem in importAnnotationKeyNames)
                                {
                                    PdfItem? impItem;
                                    switch (pdfItem.Value)
                                    {
                                        case PdfLinkAnnotation.Keys.BS:
                                            newAnnotation.Elements.Add(PdfLinkAnnotation.Keys.BS, new PdfLiteral("<</W 0>>"));
                                            break;

                                        case PdfAnnotation.Keys.F:  // /F 4
                                            impItem = annot.Elements.GetValue(PdfAnnotation.Keys.F);
                                            Debug.Assert(impItem is PdfInteger);
                                            newAnnotation.Elements.Add(PdfAnnotation.Keys.F, impItem.Clone());
                                            break;

                                        case PdfAnnotation.Keys.Rect:  // /Rect [68.6 681.08 145.71 702.53]
                                            impItem = annot.Elements.GetValue(PdfAnnotation.Keys.Rect);
                                            Debug.Assert(impItem is PdfRectangle);
                                            newAnnotation.Elements.Add(PdfAnnotation.Keys.Rect, impItem.Clone());
                                            break;

                                        case PdfAnnotation.Keys.StructParent:  // /StructParent 3
                                            impItem = annot.Elements.GetValue(PdfAnnotation.Keys.StructParent);
                                            Debug.Assert(impItem is PdfInteger);
                                            newAnnotation.Elements.Add(PdfAnnotation.Keys.StructParent, impItem.Clone());
                                            break;

                                        case PdfAnnotation.Keys.Subtype:  // Already set.
                                            break;

                                        case PdfLinkAnnotation.Keys.Dest:  // /Dest [30 0 R /XYZ 68 771 0]
                                            impItem = annot.Elements.GetValue(PdfLinkAnnotation.Keys.Dest);
                                            impItem = impItem!.Clone(); // NRT

                                            // Is value an array with 5 elements where the first one is an iref?
                                            if (impItem is PdfArray { Elements.Count: 5 } destArray)
                                            {
                                                if (destArray.Elements[0] is PdfReference iref)
                                                {
                                                    var iref2 = RemapReference(insertPages, importPages, iref);
                                                    if (iref2 != null)
                                                    {
                                                        destArray.Elements[0] = iref2;
                                                        newAnnotation.Elements.Add("/Dest", destArray);
                                                        addAnnotation = true;
                                                    }
                                                }
                                            }
                                            break;

                                        default:
#if DEBUG_
                                            Debug-Break.Break(true);
#endif
                                            break;

                                    }
                                }
                                // Add newAnnotations only it points to an imported page.
                                if (addAnnotation)
                                    annotations.Add(newAnnotation);
                            }
                        }
                    }

                    // At least one link annotation found?
                    if (annotations.Count > 0)
                    {
#if DEBUG_
                        // BUG_OLD!!! HACK_OLD to make it work: Fails if there already are annotations. // ReviewSTLA.
                        var annots2 = page.Elements.GetArray(PdfPage.Keys.Annots);
                        if (annots2 is null)
                        {
                            page.Elements.Add(PdfPage.Keys.Annots, annotations);
                        }
#else
                        var annots2 = page.Elements.GetArray(PdfPage.Keys.Annots);
                        if (annots2 is not null)
                            _ = typeof(int);  // Temporary line for breakpoints.

                        //Owner._irefTable.Add(annotations);
                        page.Elements.Add(PdfPage.Keys.Annots, annotations);
#endif
                    }
                }
            }

            // #PDF-UA: Pages were imported.
            if (Document.UAManager != null)
                Document.Events.OnPageAdded(Document, new PageEventArgs(Document) { EventType = PageEventType.Imported });
        }

        /// <summary>
        /// Inserts all pages of the specified document into this document.
        /// </summary>
        /// <param name="index">The index in this document where to insert the page .</param>
        /// <param name="document">The document to be inserted.</param>
        public void InsertRange(int index, PdfDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            InsertRange(index, document, 0, document.PageCount);
        }

        /// <summary>
        /// Inserts all pages of the specified document into this document.
        /// </summary>
        /// <param name="index">The index in this document where to insert the page .</param>
        /// <param name="document">The document to be inserted.</param>
        /// <param name="startIndex">The index of the first page to be inserted.</param>
        public void InsertRange(int index, PdfDocument document, int startIndex)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            InsertRange(index, document, startIndex, document.PageCount - startIndex);
        }

        /// <summary>
        /// Removes the specified page from the document.
        /// </summary>
        public void Remove(PdfPage page)
        {
            int index = PagesArray.Elements.IndexOf(page);
            if (index >= 0)
                RemoveFromTree(index);

            // #PDF-UA: Page was removed.
            if (Document.UAManager != null)
                Document.Events.OnPageRemoved(Document, new PageEventArgs(Document) { Page = page, PageIndex = -1, EventType = PageEventType.Removed });
        }

        /// <summary>
        /// Removes the specified page from the document.
        /// </summary>
        public void RemoveAt(int index)
        {
            // The list holds references, so this is null and, as before, no PDF/UA event follows (UAManager does not
            // support removing pages).
            var page = PagesArray.Elements[index] as PdfPage;
            RemoveFromTree(index);

            // #PDF-UA
            if (Document.UAManager != null && page != null)
                Document.Events.OnPageRemoved(Document, new PageEventArgs(Document) { Page = page, PageIndex = index });
        }

        /// <summary>
        /// Moves a page within the page sequence.
        /// </summary>
        /// <param name="oldIndex">The page index before this operation.</param>
        /// <param name="newIndex">The page index after this operation.</param>
        public void MovePage(int oldIndex, int newIndex)
        {
            // #PDF-UA: Not implemented.
            if (Document.UAManager != null)
                throw new InvalidOperationException("Cannot move a page in a PDF/UA document.");

            if (oldIndex < 0 || oldIndex >= Count)
                throw new ArgumentOutOfRangeException(nameof(oldIndex));
            if (newIndex < 0 || newIndex >= Count)
                throw new ArgumentOutOfRangeException(nameof(newIndex));
            if (oldIndex == newIndex)
                return;

            InsertIntoTree(newIndex, RemoveFromTree(oldIndex));
        }

        /// <summary>
        /// Imports a page of an import-mode document without its annotations and without adding it to the pages, for
        /// PdfPageImporter, which copies the annotations itself and appends the page with AppendImportedPage. Also
        /// keeps the page's transparency group, tab order and user unit, which ImportExternalPage leaves out.
        /// </summary>
        internal PdfPage ImportPageWithoutAnnotations(PdfPage importPage)
        {
            var page = ImportExternalPage(importPage, false);
            CloneElement(page, importPage, PdfPage.Keys.Group, false);
            CloneElement(page, importPage, PdfPage.Keys.Tabs, true);
            CloneElement(page, importPage, PdfPage.Keys.UserUnit, true);
            return page;
        }

        /// <summary>
        /// Appends a page made by ImportPageWithoutAnnotations.
        /// </summary>
        internal void AppendImportedPage(PdfPage page, PdfPage importPage)
        {
            Owner.FormTable.GetImportedObjectTable(importPage).Add(importPage.ObjectID, page.RequiredReference);
            InsertIntoTree(Count, page);
            if (Owner.Settings.TrimMargins.AreSet)
                page.TrimMargins = Owner.Settings.TrimMargins;
        }

        /// <summary>
        /// Imports an external page. The elements of the imported page are cloned and added to this document.
        /// Important: In contrast to PdfFormXObject adding an external page always make a deep copy
        /// of their transitive closure. Any reuse of already imported objects is not intended because
        /// any modification of an imported page must not change another page.
        /// </summary>
        PdfPage ImportExternalPage(PdfPage importPage, bool withAnnotations = true)
        {
            if (importPage.Owner.OpenMode != PdfDocumentOpenMode.Import)
                throw new InvalidOperationException("A PDF document must be opened with PdfDocumentOpenMode.Import to import pages from it.");

            var page = new PdfPage(Document);

            // ReSharper disable AccessToStaticMemberViaDerivedType for a better code readability.
            CloneElement(page, importPage, PdfPage.Keys.Resources, false);
            CloneElement(page, importPage, PdfPage.Keys.Contents, false);
            CloneElement(page, importPage, PdfPage.Keys.MediaBox, true);
            CloneElement(page, importPage, PdfPage.Keys.CropBox, true);
            CloneElement(page, importPage, PdfPage.Keys.Rotate, true);
            CloneElement(page, importPage, PdfPage.Keys.BleedBox, true);
            CloneElement(page, importPage, PdfPage.Keys.TrimBox, true);
            CloneElement(page, importPage, PdfPage.Keys.ArtBox, true);

            // Do not deep copy annotations.
            if (withAnnotations)
                CloneElement(page, importPage, PdfPage.Keys.Annots, false);

            page.Initialize(true);

            // ReSharper restore AccessToStaticMemberViaDerivedType
            return page;
        }

        /// <summary>
        /// Helper function for ImportExternalPage.
        /// </summary>
        void CloneElement(PdfPage page, PdfPage importPage, string key, bool deepCopy)
        {
            Debug.Assert(page != null);
            Debug.Assert(page.Owner == Document);
            Debug.Assert(importPage.Owner != null);
            Debug.Assert(importPage.Owner != Document);

            //PdfItem? item = importPage.Elements[key];
            PdfItem? item;
            if (deepCopy && importPage.Elements[key] is PdfReference reference)
                item = DirectValue(reference.Value);
            else
                item = importPage.Elements.GetValue(key); // #US373
            if (item != null)
            {
                PdfImportedObjectTable? importedObjectTable = null;
                if (!deepCopy)
                    importedObjectTable = Owner.FormTable.GetImportedObjectTable(importPage);

                // TODO #US373 begin
                //// The item can be indirect. If so, replace it by its value.
                ////if (item is PdfReference reference)
                ////    item = reference.Value;
                //PdfReference.Dereference(ref item);
                // TODO #US373 end

                if (item is PdfObject root)
                {
                    if (deepCopy)
                    {
                        Debug.Assert(root.Owner != null, "See 'else' case for details");
                        root = DeepCopyClosure(Document, root);
                    }
                    else
                    {
                        // The owner can be null if the item is not a reference.
                        if (root.Owner == null!)
                            root.Document = importPage.Owner;
                        root = ImportClosure(importedObjectTable ?? NRT.ThrowOnNull<PdfImportedObjectTable>(), page.Owner, root);
                    }

                    if (root.Reference == null)
                        page.Elements[key] = root;
                    else
                        page.Elements[key] = root.Reference;
                }
                else
                {
                    // Simple items are just cloned.
                    page.Elements[key] = item.Clone();
                }
            }
        }

        // The value of a page box or rotation held as an indirect object, as pdf-lib and others write them, to copy as
        // a direct value. Reading an indirect box through GetValue threw, and an indirect rotation crashed the copy.
        static PdfItem DirectValue(PdfObject value)
        {
            if (value is PdfIntegerObject integer)
                return new PdfInteger(integer.Value);
            if (value is PdfArray { Elements.Count: 4 } array && array.Elements.All(element => element is PdfInteger or PdfReal or PdfLongInteger))
                return new PdfRectangle(array);
            return value;
        }

        static PdfReference? RemapReference(PdfPage[] newPages, PdfPage[] impPages, PdfReference iref)  // TODO review
        {
            // Directs the iref to a one of the imported pages?
            for (int idx = 0; idx < newPages.Length; idx++)
            {
                if (impPages[idx].Reference == iref)
                    return newPages[idx].Reference;
            }
            return null;
        }

        /// <summary>
        /// Gets a PdfArray containing all pages of this document. The array must not be modified.
        /// </summary>
        //public PdfArray PagesArray
        public PdfPageTreeNodes PagesArray
        {
            get
            {
                //return _pagesArray ??= (PdfArray?)Elements.GetValue(Keys.Kids, VCF.Create) ?? NRT.ThrowOnNull<PdfArray>();
                return _pageTreeNodes ??= _flattenedPages ?? Elements.GetRequiredArray<PdfPageTreeNodes>(Keys.Kids, VCF.Create);
            }
        }
        //PdfArray? _pagesArray;
        PdfPageTreeNodes? _pageTreeNodes;

        /// <summary>
        /// Replaces the page tree by a flat array of indirect references to the pages objects.
        /// </summary>
        internal void FlattenPageTree()
        {
            // Acrobat creates a balanced tree if the number of pages is roughly more than ten. This is
            // not difficult but obviously also not necessary. I created a document with 50,000 pages with
            // PDF4NET and Acrobat opened it in less than 2 seconds.

            // Promote inheritable values down the page tree to each single page.
            var inheritedValues = new PdfPageTreeNode.InheritedValues();
            // Get the root inheritable values.
            GetInheritableValues(ref inheritedValues);

            // Flat list of pages.
            // Used to replace the page tree (if one exists).
            List<PdfPage> pages = [];

            // Iterate the page tree in pre-order.
            TraversePageTree(pages, this, inheritedValues);

            // Put a flat list to the root node.
            var array = Elements.GetRequiredArray(Keys.Kids);
            array.Elements.Clear();
            foreach (var page in pages)
            {
                // Fix the parent. In Append mode the page keeps the /Parent of the file, also when it is wrong: an update
                // that changes it is a change (a viewer may take what the page inherits from the node it names), and
                // signature checks compare what the file says.
                if (Document.OpenMode != PdfDocumentOpenMode.Append)
                    page.Elements[PdfPage.Keys.Parent] = this;
                array.Elements.Add(page);
            }

            Debug.Assert(Elements.GetName(Keys.Type) == "/Pages");
            Elements.SetName(Keys.Type, "/Pages");  // TODO DELETE

            // Direct array.
            Debug.Assert(ReferenceEquals(Elements.GetRequiredObject(Keys.Kids).Reference, array.Reference));
            Elements.SetValue(Keys.Kids, array);  // TODO DELETE

            // In Append mode the root keeps the /Count of the file, also when it is wrong: an update that changes it is
            // a change (a viewer may show that many pages), and signature checks compare what the file says.
            if (Document.OpenMode != PdfDocumentOpenMode.Append)
                Elements.SetInteger(Keys.Count, array.Elements.Count);

            // Every page now has the values it inherits from the root, so the root does not need them, and pages added
            // later would inherit them although they do not have them (a new page came out rotated, for example). Only
            // the values that were read, and so copied onto the pages, go. In Append mode the root stays as it is in the
            // file; the update gives new pages what they need instead.
            if (Document.OpenMode != PdfDocumentOpenMode.Append)
            {
                if (inheritedValues.Resources != null)
                    Elements.Remove(PdfPage.InheritablePageKeys.Resources);
                if (inheritedValues.MediaBox != null)
                    Elements.Remove(PdfPage.InheritablePageKeys.MediaBox);
                if (inheritedValues.CropBox != null)
                    Elements.Remove(PdfPage.InheritablePageKeys.CropBox);
                if (inheritedValues.Rotate != null)
                    Elements.Remove(PdfPage.InheritablePageKeys.Rotate);
            }

            return;

            // DELETE
            void CollectKids__(PdfDictionary treeNode, PdfPageTreeNode.InheritedValues values)
            {
                // TODO_OLD: inherit inheritable keys...
                //var kid = (PdfDictionary)iref.Value;

                //string type = dict.Elements.GetName(Keys.Type);
                //if (type == "/Page")
                //{
                //    PdfPage.InheritValues(dict, values);
                //    pages.Add(dict);
                //    return;
                //}
                //Debug.Assert(type == "/Pages");

                var kids = treeNode.Elements.GetArray(Keys.Kids);
                if (kids != null)
                {
                    foreach (var item in kids)
                    {
                        if (((PdfReference)item).Value is PdfDictionary kid)
                        {
                            string type = kid.Elements.GetName(Keys.Type);
                            if (type == "/Page")
                            {
                                var oldKid = kid;
                                var oldRef = kid.RequiredReference;
                                var page = (PdfPage)kid.Elements.CreateContainer(typeof(PdfPage), kid, true);

                                Debug.Assert(oldKid.IsDead);
                                Debug.Assert(ReferenceEquals(oldRef, page.RequiredReference));

                                pages.Add(page);
                            }
                            else
                            {
                                Debug.Assert(type == "/Pages");

                                //var oldKid = kid;
                                //var oldRef = kid.RequiredReference;
                                //var node = (PdfPageTreeNode)kid.Elements.CreateContainer(typeof(PdfPageTreeNode), kid, true);

                                //Debug.Assert(oldKid.IsDead);
                                //Debug.Assert(ReferenceEquals(oldRef, node.RequiredReference));

                                CollectKids__(kid, values);
                            }
                        }
                    }
                    return;
                }
                // A page tree node with no kids?
                Debug.Assert(false, $"Page tree node () has no /Kids entry.");
            }
        }

        /// <summary>
        /// Gets a value indicating whether every kid of this root node is a page, so that flattening the tree
        /// does not change its structure.
        /// </summary>
        internal bool HasOnlyPageKids()
        {
            var kids = Elements.GetValue(Keys.Kids, VCF.NoTransform) is PdfReference kidsReference
                ? kidsReference.Value as PdfArray
                : Elements.GetValue(Keys.Kids, VCF.NoTransform) as PdfArray;
            if (kids == null)
                return false;
            foreach (var item in kids.Elements)
            {
                var kid = item is PdfReference reference ? reference.Value as PdfDictionary : null;
                if (kid == null || kid.Elements.GetName(Keys.Type) != "/Page" || kid.Elements.ContainsKey(Keys.Kids))
                    return false;
            }
            return true;
        }

        // In Append mode the page tree is changed where it is, so that the update writes only the nodes between the
        // root and the pages that come or go. A nested tree keeps its nodes; _flattenedPages, the list of its pages
        // that this[index] reads, is kept up to date beside it.
        bool ChangesTreeInPlace => Document.OpenMode == PdfDocumentOpenMode.Append;

        /// <summary>
        /// Puts a page into the page tree at the specified page index.
        /// </summary>
        void InsertIntoTree(int index, PdfPage page)
        {
            if (!ChangesTreeInPlace)
            {
                PagesArray.Elements.Insert(index, page.RequiredReference);
                Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);
                return;
            }
            if (index < 0 || index > Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, PsMsgs.PageIndexOutOfRange);

            var (node, position) = PlaceFor(index, HomeNode(page));
            KidsOf(node).Elements.Insert(position, page.RequiredReference);
            page.Elements[PdfPage.Keys.Parent] = node.RequiredReference;
            Parents[page.ObjectID] = node;
            for (var above = node; !ReferenceEquals(above, this); above = Parents[above.ObjectID])
                above.Elements.SetInteger(Keys.Count, above.Count + 1);
            _flattenedPages?.Elements.Insert(index, page.RequiredReference);
            Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);
        }

        // Where a page inserted at a page index goes in Append mode: behind the page before it or in front of the page
        // after it, in that page's node. The node the page was in when the file was opened comes first, so that a page
        // moved within its node stays in it, and moving it back leaves the tree as it was (unless it was the only page
        // of its node, which then left the tree). Otherwise a page at either end goes into the root, which changes no
        // other node, and any other page in front of the page after it.
        (PdfPageTreeNode Node, int Position) PlaceFor(int index, PdfPageTreeNode? home)
        {
            if (index > 0)
            {
                var previous = this[index - 1];
                var node = Parents[previous.ObjectID];
                if (ReferenceEquals(node, home))
                    return (node, PositionOf(node, previous) + 1);
            }
            if (index < Count)
            {
                var next = this[index];
                var node = Parents[next.ObjectID];
                if (index > 0 || ReferenceEquals(node, home))
                    return (node, PositionOf(node, next));
                return (this, 0);
            }
            return (this, KidsOf(this).Elements.Count);
        }

        int PositionOf(PdfPageTreeNode node, PdfPage page)
        {
            int position = KidsOf(node).Elements.IndexOf(page.RequiredReference);
            if (position < 0)
                throw new InvalidOperationException("The page tree node of a page does not list the page.");
            return position;
        }

        // The node a page was in when the file was opened; null for a page that was not in the file's tree.
        PdfPageTreeNode? HomeNode(PdfPage page)
        {
            if (_homes == null)
                BuildParents();
            _homes!.TryGetValue(page.ObjectID, out var home);
            return home;
        }

        /// <summary>
        /// Takes the page at the specified page index out of the page tree. In Append mode, the highest node that has
        /// no other pages goes with it, unchanged, so that the update does not write it.
        /// </summary>
        PdfPage RemoveFromTree(int index)
        {
            var page = this[index];
            if (!ChangesTreeInPlace)
            {
                PagesArray.Elements.RemoveAt(index);
                Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);
                return page;
            }

            PdfDictionary detached = page;
            var node = Parents[page.ObjectID];
            Parents.Remove(page.ObjectID);
            while (!ReferenceEquals(node, this) && KidsOf(node).Elements.Count == 1)
            {
                detached = node;
                node = Parents[node.ObjectID];
                Parents.Remove(detached.ObjectID);
            }
            KidsOf(node).Elements.Remove(detached.RequiredReference);
            for (var above = node; !ReferenceEquals(above, this); above = Parents[above.ObjectID])
                above.Elements.SetInteger(Keys.Count, above.Count - 1);
            _flattenedPages?.Elements.RemoveAt(index);
            Elements.SetInteger(Keys.Count, PagesArray.Elements.Count);
            return page;
        }

        /// <summary>
        /// Gets the values that a page takes from the nodes above it in the page tree (ISO 32000-1, 7.7.3.4), as
        /// reading the document puts them onto the page; null for a page that is not in the tree.
        /// </summary>
        internal PdfPageTreeNode.InheritedValues? InheritedValuesAt(PdfPage page)
        {
            if (page.Reference == null || !Parents.TryGetValue(page.ObjectID, out var node))
                return null;
            var path = new Stack<PdfPageTreeNode>();
            path.Push(node);
            while (!ReferenceEquals(node, this))
                path.Push(node = Parents[node.ObjectID]);
            var values = new PdfPageTreeNode.InheritedValues();
            while (path.Count > 0)
                path.Pop().GetInheritableValues(ref values);
            return values;
        }

        // The /Kids array of a node. The root's is the page list when the tree is flat.
        PdfArray KidsOf(PdfPageTreeNode node)
        {
            if (ReferenceEquals(node, this) && _flattenedPages == null)
                return PagesArray;
            return node.Elements.GetArray(Keys.Kids) ?? throw new InvalidOperationException("A page tree node has no /Kids array.");
        }

        // The node above each page and node of the tree, as the /Kids arrays give it; the root has none. Built when
        // first needed, then kept up to date by the changes to the tree.
        Dictionary<PdfObjectID, PdfPageTreeNode> Parents
        {
            get
            {
                if (_parents == null)
                    BuildParents();
                return _parents!;
            }
        }

        // Builds the parent map, and a copy of it that keeps the tree as the file has it: every change to the tree
        // reads the map first.
        void BuildParents()
        {
            var parents = new Dictionary<PdfObjectID, PdfPageTreeNode>();
            var pending = new Stack<PdfPageTreeNode>();
            pending.Push(this);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                foreach (var item in KidsOf(node).Elements)
                {
                    if (item is not PdfReference child)
                        continue;
                    parents[child.ObjectID] = node;
                    if (child.Value is PdfPageTreeNode kid)
                        pending.Push(kid);
                }
            }
            _parents = parents;
            _homes = new Dictionary<PdfObjectID, PdfPageTreeNode>(parents);
        }
        Dictionary<PdfObjectID, PdfPageTreeNode>? _parents;
        Dictionary<PdfObjectID, PdfPageTreeNode>? _homes;

        /// <summary>
        /// Preserves the page tree of an imported document.
        /// </summary>
        internal void PreservePageTree()
        {
            // Although the page tree is not changed, we promote inheritable values down the page tree
            // to each single page. This makes it much more simple to import a page because every page
            // already knows it closure.
            var inheritedValues = new PdfPageTreeNode.InheritedValues();
            // Get the root inheritable values.
            GetInheritableValues(ref inheritedValues);

            // Flat list of pages.
            List<PdfPage> pages = [];

            TraversePageTree(pages, this, inheritedValues);

            // Save the pages array.
            _flattenedPages = new PdfPageTreeNodes(pages);

            // In Append mode the root keeps the /Count of the file (see FlattenPageTree).
            if (Document.OpenMode != PdfDocumentOpenMode.Append)
                Elements.SetInteger(Keys.Count, pages.Count);

            return;

            // DELETE
            void TraverseKids__(PdfPageTreeNode treeNode, PdfPageTreeNode.InheritedValues values)
            {
                // TODO_OLD: inherit inheritable keys...
                //var kid = (PdfDictionary)iref.Value;

                var kids = treeNode.Elements.GetArray<PdfPageTreeNodes>(Keys.Kids);
                if (kids != null)
                {
                    foreach (var item in kids /*.Elements.AsEnumerable<PdfDictionary>()*/)
                    {
                        if (((PdfReference)item).Value is PdfDictionary kid)
                        {
                            string type = kid.Elements.GetName(Keys.Type);
                            if (type == "/Page")
                            {
                                var oldKid = kid;
                                var oldRef = kid.RequiredReference;
                                var page = (PdfPage)kid.Elements.CreateContainer(typeof(PdfPage), kid, true);

                                Debug.Assert(oldKid.IsDead);
                                Debug.Assert(ReferenceEquals(oldRef, page.RequiredReference));

                                pages.Add(page);
                            }
                            else
                            {
                                Debug.Assert(type == "/Pages");

                                var oldKid = kid;
                                var oldRef = kid.RequiredReference;
                                var node = (PdfPageTreeNode)kid.Elements.CreateContainer(typeof(PdfPageTreeNode), kid, true);

                                Debug.Assert(oldKid.IsDead);
                                Debug.Assert(ReferenceEquals(oldRef, node.RequiredReference));

                                TraverseKids__(node, values);
                            }
                        }
                    }
                    return;
                }
                // A page tree node with no kids?
                Debug.Assert(false, $"Page tree node () has no /Kids entry.");
            }
        }

        /// <summary>
        /// Traverses the page tree in pre-order and collects its leaf pages.
        /// </summary>
        /// <param name="pages">The pages.</param>
        /// <param name="treeNode">The tree node.</param>
        /// <param name="inheritedValues">The values.</param>
        void TraversePageTree(List<PdfPage> pages, PdfPageTreeNode treeNode, PdfPageTreeNode.InheritedValues inheritedValues)
        {
            // Iterative traversal handles deep trees without consuming the call stack.
            // Each stack entry owns its inherited values, so overrides cannot leak to siblings.
            var pending = new Stack<(PdfReference Reference, PdfPageTreeNode.InheritedValues Values)>();
            var visited = new HashSet<PdfReference>();
            pending.Push((treeNode.RequiredReference, inheritedValues));
            while (pending.Count > 0)
            {
                var (reference, values) = pending.Pop();
                if (!visited.Add(reference))
                    throw new PdfReaderException("Cyclic or repeated node in the page tree.");
                if (reference.Value is not PdfDictionary dictionary)
                    throw new PdfReaderException("Invalid dictionary in the page tree.");

                var type = dictionary.Elements.GetName(Keys.Type);
                if (type == "/Page")
                {
                    var page = dictionary as PdfPage ?? new PdfPage(dictionary, in values);
                    page.ApplyInheritedValues(in values);
                    pages.Add(page);
                }
                else if (type == "/Pages")
                {
                    var node = dictionary as PdfPageTreeNode ?? new PdfPageTreeNode(dictionary);
                    node.GetInheritableValues(ref values);
                    var kids = node.Elements.GetArray(Keys.Kids)
                        ?? throw new PdfReaderException("Missing children in the page tree.");
                    for (var index = kids.Elements.Count - 1; index >= 0; index--)
                    {
                        if (kids.Elements[index] is not PdfReference child)
                            throw new PdfReaderException("Invalid child reference in the page tree.");
                        pending.Push((child, values));
                    }
                }
                else
                    throw new PdfReaderException("Invalid node type in the page tree.");
            }
        }

        /// <summary>
        /// Prepares the document for saving.
        /// </summary>
        internal override void PrepareForSave()
        {
            // TODO_OLD: Close all open content streams

            // We do not create the page tree.
            // Arrays have a limit of 8192 entries, but I successfully tested documents
            // with 50000 pages and no page tree.
            // ==> wait for bug report.
            int count = PagesArray.Elements.Count;
            for (int idx = 0; idx < count; idx++)
            {
                var page = this[idx];
                page.PrepareForSave();
            }
        }

        /// <summary>
        /// Gets the enumerator.
        /// </summary>
        public new IEnumerator<PdfPage> GetEnumerator()
            => new PdfPagesEnumerator(this);

        class PdfPagesEnumerator : IEnumerator<PdfPage>
        {
            internal PdfPagesEnumerator(PdfPages list)
            {
                _list = list;
                _index = -1;
            }

            public bool MoveNext()
            {
                if (_index < _list.Count - 1)
                {
                    _index++;
                    _currentElement = _list[_index];
                    return true;
                }
                _index = _list.Count;
                return false;
            }

            public void Reset()
            {
                _currentElement = null;
                _index = -1;
            }

            object IEnumerator.Current => Current;

            public PdfPage Current
            {
                get
                {
                    if (_index == -1 || _index >= _list.Count)
                        throw new InvalidOperationException(PsMsgs.ListEnumCurrentOutOfRange);
                    return _currentElement ?? throw new InvalidOperationException("Current called before MoveNext.");
                }
            }

            public void Dispose()
            {
                // Nothing to do.
            }

            PdfPage? _currentElement;
            int _index;
            readonly PdfPages _list;
        }

        /// <summary>
        /// If the page tree is not flattened, it contains all pages of an imported document;
        /// null otherwise.
        /// </summary>
        PdfPageTreeNodes? _flattenedPages;

        void EnsureNotFrozen()
        {
            // TODO
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        public new sealed class Keys : PdfPage.InheritablePageKeys
        {
            /// <summary>
            /// (Required) The type of PDF object that this dictionary describes; 
            /// must be Pages for a page tree node.
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "Pages")]
            public const string Type = "/Type";

            /// <summary>
            /// (Required except in root node; must be an indirect reference)
            /// The page tree node that is the immediate parent of this one.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string Parent = "/Parent";

            /// <summary>
            /// (Required) An array of indirect references to the immediate children of this node.
            /// The children may be page objects or other page tree nodes.
            /// </summary>
            [KeyInfo(KeyType.Array | KeyType.Required)]
            public const string Kids = "/Kids";

            /// <summary>
            /// (Required) The number of leaf nodes (page objects) that are descendants of this node 
            /// within the page tree.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string Count = "/Count";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            internal static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta? _meta;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
