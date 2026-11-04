// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PdfSharp.Pdf.Advanced;

namespace PdfSharp.Pdf.IO
{
    /// <summary>
    /// The changes the signatures of a document permit after they were made: the certification signature's
    /// permission (DocMDP, ISO 32000-1 12.8.2.2) and the fields that signatures lock (FieldMDP, 12.8.2.4, and the
    /// signature field's /Lock dictionary, ISO 32000-2 Table 233). An Append-mode save that would change more is
    /// refused before anything is written, because validators would then report those signatures as broken.
    /// </summary>
    sealed class PdfModificationPermissions
    {
        // No certification: only locked fields are protected.
        const int Unrestricted = 4;

        PdfModificationPermissions(int permission, List<FieldLock> locks)
        {
            _permission = permission;
            _locks = locks;
        }

        /// <summary>
        /// Reads the permissions of a document that was just opened. Returns null if its signatures restrict nothing.
        /// </summary>
        internal static PdfModificationPermissions? Read(PdfDocument document)
        {
            int permission = Unrestricted;
            var locks = new List<FieldLock>();

            if (Resolve(document.Catalog.Elements[PdfCatalog.Keys.Perms]) is PdfDictionary perms
                && Resolve(perms.Elements["/DocMDP"]) is PdfDictionary certification)
            {
                permission = Math.Min(permission, CertificationLevel(certification) ?? 2);
            }

            // Signed signature fields: the FieldMDP and DocMDP references of their signatures, and their locks.
            foreach (var field in Fields(document))
            {
                if (field.Type != "/Sig" || Resolve(field.Dictionary.Elements["/V"]) is not PdfDictionary signature)
                    continue;
                if (Resolve(signature.Elements["/Reference"]) is PdfArray references)
                {
                    foreach (var item in references.Elements)
                    {
                        if (Resolve(item) is not PdfDictionary reference)
                            continue;
                        var parameters = Resolve(reference.Elements["/TransformParams"]) as PdfDictionary;
                        var method = reference.Elements.GetName("/TransformMethod");
                        if (method == "/DocMDP")
                            permission = Math.Min(permission, Level(parameters) ?? 2);
                        else if (method == "/FieldMDP" && parameters != null)
                            locks.Add(FieldLock.From(parameters));
                    }
                }
                if (Resolve(field.Dictionary.Elements["/Lock"]) is PdfDictionary fieldLock)
                {
                    locks.Add(FieldLock.From(fieldLock));
                    if (Level(fieldLock) is { } level)
                        permission = Math.Min(permission, level);
                }
            }

            if (permission == Unrestricted && locks.Count == 0)
                return null;
            return new PdfModificationPermissions(permission, locks);
        }

        // The DocMDP level of a certification signature: its /Reference entry with the DocMDP transform.
        static int? CertificationLevel(PdfDictionary signature)
        {
            if (Resolve(signature.Elements["/Reference"]) is not PdfArray references)
                return null;
            foreach (var item in references.Elements)
            {
                if (Resolve(item) is PdfDictionary reference && reference.Elements.GetName("/TransformMethod") == "/DocMDP")
                    return Level(Resolve(reference.Elements["/TransformParams"]) as PdfDictionary) ?? 2;
            }
            return null;
        }

        // /P of transform parameters or a lock: 1, 2 or 3. A value outside these is taken as 1, the strictest.
        static int? Level(PdfDictionary? parameters)
        {
            if (parameters == null || Resolve(parameters.Elements["/P"]) is not PdfInteger p)
                return null;
            return p.Value is >= 1 and <= 3 ? p.Value : 1;
        }

        /// <summary>
        /// Throws InvalidOperationException if the update would change more than the signatures permit.
        /// </summary>
        /// <param name="document">The document being saved.</param>
        /// <param name="original">The bytes the document was opened from.</param>
        /// <param name="written">The objects the update writes.</param>
        internal void Check(PdfDocument document, byte[] original, IReadOnlyCollection<PdfReference> written)
        {
            if (_permission == 1)
                throw Refusal("saving any change");

            using var before = PdfReader.Open(new MemoryStream(original, false), PdfDocumentOpenMode.Import);
            var old = new Snapshot(before);
            var now = new Snapshot(document);
            foreach (var iref in written)
            {
                // A new object is reached only through changes to objects of the file, which are checked here.
                if (before.Internals.GetObject(iref.ObjectID) is not { } oldObject)
                    continue;
                CheckChange(iref.ObjectID, oldObject, iref.Value, old, now);
            }
        }

        void CheckChange(PdfObjectID id, PdfObject oldObject, PdfObject newObject, Snapshot old, Snapshot now)
        {
            if (old.Fields.TryGetValue(id, out var field))
            {
                if (IsLocked(field.Name))
                    throw LockRefusal(field.Name);
                if (Restricted)
                    CheckKeys(old, now, oldObject, newObject, $"the form field '{field.Name}'", FieldKeys);
                return;
            }
            if (old.Appearances.TryGetValue(id, out var owner))
            {
                if (IsLocked(owner))
                    throw LockRefusal(owner);
                return;
            }
            if (!Restricted)
                return;

            if (id == old.Info || id == old.Metadata)
                return;
            if (id == old.Catalog)
            {
                CheckKeys(old, now, oldObject, newObject, "the document catalog", CatalogKeys);
                if (oldObject is PdfDictionary oldCatalog && newObject is PdfDictionary newCatalog
                    && oldCatalog.Elements[PdfCatalog.Keys.AcroForm] is PdfDictionary oldDirectForm
                    && newCatalog.Elements[PdfCatalog.Keys.AcroForm] is PdfDictionary newDirectForm)
                {
                    CheckForm(old, now, oldDirectForm, newDirectForm);
                }
                return;
            }
            if (id == old.Form)
            {
                CheckForm(old, now, (PdfDictionary)oldObject, (PdfDictionary)newObject);
                return;
            }
            if (id == old.FieldArray)
            {
                CheckFieldArray(old, now, oldObject as PdfArray, newObject as PdfArray);
                return;
            }
            if (old.Pages.TryGetValue(id, out var pageIndex))
            {
                var description = $"page {pageIndex + 1} (object {id.ObjectNumber})";
                CheckKeys(old, now, oldObject, newObject, description, PageKeys);
                // An indirect /Annots array that keeps its object is checked when that object is written.
                if (ChangedKeys(old, now, oldObject, newObject).Contains("/Annots"))
                {
                    CheckAnnotations(old, now, Resolve(((PdfDictionary)oldObject).Elements["/Annots"]) as PdfArray,
                        Resolve(((PdfDictionary)newObject).Elements["/Annots"]) as PdfArray, description);
                }
                return;
            }
            if (old.AnnotationArrays.TryGetValue(id, out pageIndex))
            {
                CheckAnnotations(old, now, oldObject as PdfArray, newObject as PdfArray, $"page {pageIndex + 1}");
                return;
            }
            if (old.Annotations.TryGetValue(id, out var annotation))
            {
                if (annotation.IsWidget)
                    CheckKeys(old, now, oldObject, newObject, $"a form widget on page {annotation.Page + 1}", WidgetKeys);
                else
                    Need(3, $"changing a {annotation.Subtype} annotation on page {annotation.Page + 1} (object {id.ObjectNumber})");
                return;
            }
            throw Refusal($"changing {Describe(oldObject, id)}: {string.Join(", ", ChangedKeys(old, now, oldObject, newObject))}");
        }

        void CheckForm(Snapshot old, Snapshot now, PdfDictionary oldForm, PdfDictionary newForm)
        {
            CheckKeys(old, now, oldForm, newForm, "the interactive form (AcroForm)", FormKeys);
            var oldFields = oldForm.Elements[PdfAcroFormKeys.Fields];
            if (oldFields is PdfReference { ObjectID: var fieldArray } && fieldArray == old.FieldArray
                && newForm.Elements[PdfAcroFormKeys.Fields] is PdfReference { ObjectID: var same } && same == fieldArray)
            {
                return; // The array object itself is checked when it is written.
            }
            CheckFieldArray(old, now, Resolve(oldFields) as PdfArray, Resolve(newForm.Elements[PdfAcroFormKeys.Fields]) as PdfArray);
        }

        // The form's fields may only gain new signature fields.
        void CheckFieldArray(Snapshot old, Snapshot now, PdfArray? oldFields, PdfArray? newFields)
        {
            var before = Entries(oldFields);
            var after = Entries(newFields);
            foreach (var entry in before.Where(entry => !after.Contains(entry)))
                throw Refusal($"removing a form field ({entry})");
            foreach (var item in newFields?.Elements ?? Enumerable.Empty<PdfItem>())
            {
                if (before.Contains(Key(item)))
                    continue;
                if (!IsNewSignatureField(old, now, item))
                    throw Refusal($"adding the form field '{FieldName(item)}'; only signature fields may be added");
            }
        }

        // Annotations of a page: removing one needs P=3 and never a widget; a new widget must be a new signature
        // field's, another new annotation needs P=3.
        void CheckAnnotations(Snapshot old, Snapshot now, PdfArray? oldAnnotations, PdfArray? newAnnotations, string page)
        {
            var before = Entries(oldAnnotations);
            var after = Entries(newAnnotations);
            foreach (var item in oldAnnotations?.Elements ?? Enumerable.Empty<PdfItem>())
            {
                if (after.Contains(Key(item)))
                    continue;
                if (Resolve(item) is PdfDictionary removed && removed.Elements.GetName("/Subtype") == "/Widget")
                    throw Refusal($"removing a form widget from {page}");
                Need(3, $"removing an annotation from {page}");
            }
            foreach (var item in newAnnotations?.Elements ?? Enumerable.Empty<PdfItem>())
            {
                if (before.Contains(Key(item)))
                    continue;
                if (Resolve(item) is not PdfDictionary added)
                    continue;
                var subtype = added.Elements.GetName("/Subtype");
                if (subtype == "/Widget")
                {
                    if (!IsNewSignatureField(old, now, item))
                        throw Refusal($"adding a form widget to {page}; only signature fields may be added");
                }
                else
                {
                    Need(3, $"adding a {subtype.TrimStart('/')} annotation to {page}");
                }
            }
        }

        // A widget or field that the file did not have and that belongs to a signature field.
        static bool IsNewSignatureField(Snapshot old, Snapshot now, PdfItem item)
        {
            if (item is PdfReference reference)
            {
                if (old.Document.Internals.GetObject(reference.ObjectID) != null)
                    return false;
                return now.Fields.TryGetValue(reference.ObjectID, out var field) && field.Type == "/Sig";
            }
            return false;
        }

        void CheckKeys(Snapshot old, Snapshot now, PdfObject oldObject, PdfObject newObject, string description, HashSet<string> allowed)
        {
            var changed = ChangedKeys(old, now, oldObject, newObject).Where(key => !allowed.Contains(key)).ToList();
            if (changed.Count > 0)
                throw Refusal($"changing {description}: {string.Join(", ", changed)}");
        }

        // The keys whose values differ, or "stream data" when a stream's data differs.
        static List<string> ChangedKeys(Snapshot old, Snapshot now, PdfObject oldObject, PdfObject newObject)
        {
            var result = new List<string>();
            if (oldObject is PdfDictionary oldDictionary && newObject is PdfDictionary newDictionary)
            {
                foreach (var key in oldDictionary.Elements.Keys.Union(newDictionary.Elements.Keys).OrderBy(key => key, StringComparer.Ordinal))
                {
                    var oldValue = oldDictionary.Elements[key];
                    var newValue = newDictionary.Elements[key];
                    if (oldValue == null || newValue == null || !Same(Serialize(old.Document, oldValue), Serialize(now.Document, newValue)))
                        result.Add(key);
                }
                var oldData = oldDictionary.Stream?.Value;
                var newData = newDictionary.Stream?.Value;
                if (oldData != null || newData != null)
                {
                    if (oldData == null || newData == null || !Same(oldData, newData))
                        result.Add("stream data");
                }
                return result;
            }
            if (!Same(Serialize(old.Document, oldObject), Serialize(now.Document, newObject)))
                result.Add("its content");
            return result;
        }

        void Need(int level, string change)
        {
            if (_permission < level)
                throw Refusal(change);
        }

        bool Restricted => _permission != Unrestricted;

        bool IsLocked(string fieldName) => _locks.Any(fieldLock => fieldLock.Locks(fieldName));

        InvalidOperationException Refusal(string change)
        {
            var permits = _permission switch
            {
                1 => "no changes",
                2 => "only filling in forms and signing",
                _ => "only filling in forms, signing and annotating",
            };
            return new InvalidOperationException(
                $"The certification signature of this document permits {permits} (DocMDP P={_permission}), so " +
                $"saving would make validators report it as broken: {change}. Nothing was written.");
        }

        static InvalidOperationException LockRefusal(string fieldName)
            => new($"The form field '{fieldName}' is locked by a signature (FieldMDP), so changing it would make " +
                   "validators report that signature as broken. Nothing was written.");

        static string Describe(PdfObject value, PdfObjectID id)
        {
            var type = (value as PdfDictionary)?.Elements.GetName("/Type");
            if (String.IsNullOrEmpty(type))
                return $"object {id.ObjectNumber}";
            return $"object {id.ObjectNumber} ({type})";
        }

        static string FieldName(PdfItem item)
        {
            var name = (Resolve(item) as PdfDictionary)?.Elements.GetString("/T");
            if (String.IsNullOrEmpty(name))
                return Key(item);
            return name!;
        }

        // Array entries compared across the original and the update: references by object number, other items as text.
        static HashSet<string> Entries(PdfArray? array)
            => new((array?.Elements ?? Enumerable.Empty<PdfItem>()).Select(Key), StringComparer.Ordinal);

        static string Key(PdfItem item)
        {
            if (item is PdfReference reference)
                return $"{reference.ObjectNumber} {reference.GenerationNumber} R";
            return item.ToString() ?? "";
        }

        static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

        // An item as it is written, in compact layout.
        static byte[] Serialize(PdfDocument document, PdfItem item)
        {
            using var buffer = new MemoryStream();
            var writer = new PdfWriter(buffer, document, null) { Layout = PdfWriterLayout.Compact };
            item.WriteObject(writer);
            return buffer.ToArray();
        }

        static bool Same(byte[] first, byte[] second) => first.Length == second.Length && first.SequenceEqual(second);

        // Every node of the form's field tree with its fully qualified name and its field type, which kids inherit.
        static IEnumerable<FieldNode> Fields(PdfDocument document)
        {
            var form = Resolve(document.Catalog.Elements[PdfCatalog.Keys.AcroForm]) as PdfDictionary;
            if (Resolve(form?.Elements[PdfAcroFormKeys.Fields]) is not PdfArray fields)
                yield break;
            var visited = new HashSet<PdfObjectID>();
            var pending = new Stack<(PdfItem Item, string Parent, string? Type)>();
            foreach (var item in Enumerable.Reverse(fields.Elements))
                pending.Push((item, "", null));
            while (pending.Count > 0)
            {
                var (item, parent, inheritedType) = pending.Pop();
                if (Resolve(item) is not PdfDictionary dictionary)
                    continue;
                if (item is PdfReference reference && !visited.Add(reference.ObjectID))
                    continue;
                // A node without /T is a widget of its parent field; /FT is inherited (ISO 32000-1 12.7.3.1).
                var partial = dictionary.Elements.GetString("/T");
                var name = parent;
                if (!String.IsNullOrEmpty(partial))
                    name = parent.Length == 0 ? partial : parent + "." + partial;
                var type = inheritedType;
                var ownType = dictionary.Elements.GetName("/FT");
                if (!String.IsNullOrEmpty(ownType))
                    type = ownType;
                yield return new FieldNode(dictionary, item as PdfReference, name, type);
                if (Resolve(dictionary.Elements["/Kids"]) is PdfArray kids)
                {
                    foreach (var kid in Enumerable.Reverse(kids.Elements))
                        pending.Push((kid, name, type));
                }
            }
        }

        // The objects of a document the rules tell apart, by object identifier.
        sealed class Snapshot
        {
            internal Snapshot(PdfDocument document)
            {
                Document = document;
                Catalog = document.Catalog.ObjectID;
                Info = (document.Trailer.Elements[PdfTrailer.Keys.Info] as PdfReference)?.ObjectID;
                Metadata = (document.Catalog.Elements[PdfCatalog.Keys.Metadata] as PdfReference)?.ObjectID;
                var formItem = document.Catalog.Elements[PdfCatalog.Keys.AcroForm];
                Form = (formItem as PdfReference)?.ObjectID;
                FieldArray = ((Resolve(formItem) as PdfDictionary)?.Elements[PdfAcroFormKeys.Fields] as PdfReference)?.ObjectID;

                foreach (var field in Fields(document))
                {
                    if (field.Reference != null)
                        Fields[field.Reference.ObjectID] = field;
                    if (field.Dictionary.Elements.GetName("/Subtype") == "/Widget")
                        AddAppearances(field.Dictionary, field.Name);
                }

                for (int index = 0; index < document.PageCount; index++)
                {
                    var page = document.Pages[index];
                    if (page.Reference != null)
                        Pages[page.ObjectID] = index;
                    var annotationsItem = page.Elements["/Annots"];
                    if (annotationsItem is PdfReference annotationsReference)
                        AnnotationArrays[annotationsReference.ObjectID] = index;
                    if (Resolve(annotationsItem) is not PdfArray annotations)
                        continue;
                    foreach (var item in annotations.Elements)
                    {
                        if (item is PdfReference annotationReference && annotationReference.Value is PdfDictionary annotation)
                        {
                            var subtype = annotation.Elements.GetName("/Subtype");
                            Annotations[annotationReference.ObjectID] = new AnnotationNode(index, subtype.TrimStart('/'), subtype == "/Widget");
                        }
                    }
                }
            }

            // The appearance streams of a widget: /AP /N, /R and /D, each a stream or a dictionary of states.
            void AddAppearances(PdfDictionary widget, string fieldName)
            {
                if (Resolve(widget.Elements["/AP"]) is not PdfDictionary appearances)
                    return;
                foreach (var key in new[] { "/N", "/R", "/D" })
                {
                    var item = appearances.Elements[key];
                    if (item is PdfReference { Value: PdfDictionary { Stream: not null } } stream)
                        Appearances[stream.ObjectID] = fieldName;
                    else if (Resolve(item) is PdfDictionary states)
                    {
                        foreach (var state in states.Elements.Values)
                        {
                            if (state is PdfReference stateStream)
                                Appearances[stateStream.ObjectID] = fieldName;
                        }
                    }
                }
            }

            internal PdfDocument Document { get; }
            internal PdfObjectID Catalog { get; }
            internal PdfObjectID? Info { get; }
            internal PdfObjectID? Metadata { get; }
            internal PdfObjectID? Form { get; }
            internal PdfObjectID? FieldArray { get; }
            internal Dictionary<PdfObjectID, FieldNode> Fields { get; } = new();
            internal Dictionary<PdfObjectID, string> Appearances { get; } = new();
            internal Dictionary<PdfObjectID, int> Pages { get; } = new();
            internal Dictionary<PdfObjectID, int> AnnotationArrays { get; } = new();
            internal Dictionary<PdfObjectID, AnnotationNode> Annotations { get; } = new();
        }

        sealed class FieldNode(PdfDictionary dictionary, PdfReference? reference, string name, string? type)
        {
            public PdfDictionary Dictionary { get; } = dictionary;
            public PdfReference? Reference { get; } = reference;
            public string Name { get; } = name;
            public string? Type { get; } = type;
        }

        sealed class AnnotationNode(int page, string subtype, bool isWidget)
        {
            public int Page { get; } = page;
            public string Subtype { get; } = subtype;
            public bool IsWidget { get; } = isWidget;
        }

        // The fields a FieldMDP transform or a /Lock dictionary locks (/Action /All, /Include or /Exclude).
        sealed class FieldLock
        {
            FieldLock(string action, List<string> names)
            {
                _action = action;
                _names = names;
            }

            internal static FieldLock From(PdfDictionary parameters)
            {
                var names = new List<string>();
                if (Resolve(parameters.Elements["/Fields"]) is PdfArray fields)
                {
                    foreach (var item in fields.Elements)
                    {
                        if (Resolve(item) is PdfString name)
                            names.Add(name.Value);
                    }
                }
                return new FieldLock(parameters.Elements.GetName("/Action"), names);
            }

            // A name also covers the fields below it. An unknown action locks everything, the safe reading.
            internal bool Locks(string fieldName)
            {
                bool listed = _names.Any(name => fieldName == name || fieldName.StartsWith(name + ".", StringComparison.Ordinal));
                return _action switch
                {
                    "/Include" => listed,
                    "/Exclude" => !listed,
                    _ => true,
                };
            }

            readonly string _action;
            readonly List<string> _names;
        }

        static readonly HashSet<string> FieldKeys = new(StringComparer.Ordinal) { "/V", "/AS", "/AP", "/I" };
        static readonly HashSet<string> WidgetKeys = new(StringComparer.Ordinal) { "/AS", "/AP" };
        static readonly HashSet<string> CatalogKeys = new(StringComparer.Ordinal) { "/AcroForm", "/Version", "/Metadata" };
        static readonly HashSet<string> FormKeys = new(StringComparer.Ordinal) { "/Fields", "/SigFlags", "/DR", "/NeedAppearances" };
        static readonly HashSet<string> PageKeys = new(StringComparer.Ordinal) { "/Annots" };

        static class PdfAcroFormKeys
        {
            internal const string Fields = "/Fields";
        }

        readonly int _permission;
        readonly List<FieldLock> _locks;
    }
}
