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
        // No certification: only signed signature fields and locked fields are protected.
        const int Unrestricted = 4;

        PdfModificationPermissions(int permission, List<FieldLock> locks, bool certified)
        {
            _permission = permission;
            _certified = certified;
            _locks = locks;
        }

        /// <summary>
        /// Reads the permissions of a document that was just opened. Returns null if it has no signed signature field,
        /// so that nothing is protected.
        /// </summary>
        internal static PdfModificationPermissions? Read(PdfDocument document)
        {
            int permission = Unrestricted;
            var locks = new List<FieldLock>();
            bool signed = false;
            bool certified = false;

            if (Resolve(document.Catalog.Elements[PdfCatalog.Keys.Perms]) is PdfDictionary perms
                && Resolve(perms.Elements["/DocMDP"]) is PdfDictionary certification)
            {
                permission = Math.Min(permission, CertificationLevel(certification) ?? 2);
                certified = true;
            }

            // Signed signature fields: the FieldMDP and DocMDP references of their signatures, and their locks.
            foreach (var field in Fields(document, names: false))
            {
                if (!IsSigned(field, out var signature))
                    continue;
                signed = true;
                if (Resolve(signature.Elements["/Reference"]) is PdfArray references)
                {
                    foreach (var item in references.Elements)
                    {
                        if (Resolve(item) is not PdfDictionary reference)
                            continue;
                        var parameters = Resolve(reference.Elements["/TransformParams"]) as PdfDictionary;
                        var method = reference.Elements.GetName("/TransformMethod");
                        if (method == "/DocMDP")
                        {
                            permission = Math.Min(permission, Level(parameters) ?? 2);
                            certified = true;
                        }
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

            if (permission == Unrestricted && locks.Count == 0 && !signed)
                return null;
            return new PdfModificationPermissions(permission, locks, certified);
        }

        /// <summary>
        /// Gets a value indicating whether a certification or a lock limits the changes (P=1 to 3); otherwise only the
        /// signed and locked fields themselves are protected.
        /// </summary>
        internal bool LimitsChanges => _permission != Unrestricted;

        /// <summary>
        /// The same permissions, but no more than the given level: for the signature verifier, which checks the changes
        /// after an approval signature against P=3 (form filling, signing, annotating, validation data).
        /// </summary>
        internal PdfModificationPermissions AtMost(int level) => new(Math.Min(_permission, level), _locks, _certified);

        // A signature field whose /V is a signature dictionary.
        static bool IsSigned(FieldNode field, out PdfDictionary signature)
        {
            signature = null!;
            if (field.Type != "/Sig" || Resolve(field.Dictionary.Elements["/V"]) is not PdfDictionary value)
                return false;
            signature = value;
            return true;
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

        // /P of transform parameters or a lock: 1, 2 or 3. Any other value, also one that is no integer, is taken as
        // 1, the strictest.
        static int? Level(PdfDictionary? parameters)
        {
            var item = Resolve(parameters?.Elements["/P"]);
            if (item is null or PdfNull or PdfNullObject)
                return null;
            int value = item switch
            {
                PdfInteger integer => integer.Value,
                PdfIntegerObject integer => integer.Value,
                _ => 0,
            };
            if (value is >= 1 and <= 3)
                return value;
            return 1;
        }

        /// <summary>
        /// Throws InvalidOperationException if the update would change more than the signatures permit.
        /// </summary>
        /// <param name="document">The document being saved.</param>
        /// <param name="original">The bytes the document was opened from.</param>
        /// <param name="written">The objects the update writes.</param>
        /// <param name="password">The password the document was opened with, if it is encrypted.</param>
        internal void Check(PdfDocument document, byte[] original, IReadOnlyCollection<PdfReference> written, string? password)
        {
            // FreePDF 7.10: at P=1 an update may still add validation data (DSS) and document time-stamps, which
            // ISO 32000-2 12.8.4.3 and 12.8.5 and ETSI EN 319 142-1 (B-LT, B-LTA) add after the signatures; the rules
            // below allow nothing else at P=1.
            using var before = PdfReader.Open(new MemoryStream(original, false), password, PdfDocumentOpenMode.Import);
            var old = new Snapshot(before);
            var now = new Snapshot(document);

            // Signed signature fields and locked fields cannot change at all: the field, the fields above it, its
            // widgets and their appearances. Nor can they be removed from the form or their pages.
            var protectedObjects = ProtectedObjects(old, out var protectedNodes);
            // The trailer is reached by no object: a new or replaced document information dictionary is checked here.
            if (Restricted && now.Info != old.Info)
                Need(2, "adding or replacing the document information");
            foreach (var iref in written)
            {
                // A new object is reached only through changes to objects of the file, which are checked here.
                if (before.Internals.GetObject(iref.ObjectID) is not { } oldObject)
                    continue;
                if (protectedObjects.TryGetValue(iref.ObjectID, out var reason))
                    throw ProtectionRefusal($"changing {reason}");
                CheckChange(iref.ObjectID, oldObject, iref.Value, old, now);
            }
            // They also keep their place: the same name in the field tree and their widgets on the same page.
            foreach (var pair in protectedNodes)
            {
                if (!now.Fields.TryGetValue(pair.Key, out var field))
                    throw ProtectionRefusal($"removing {pair.Value} from the form");
                if (field.Name != old.Fields[pair.Key].Name)
                    throw ProtectionRefusal($"moving {pair.Value} in the field tree, which renames it '{field.Name}'");
                if (!old.Annotations.TryGetValue(pair.Key, out var oldWidget))
                    continue;
                if (!now.Annotations.TryGetValue(pair.Key, out var newWidget))
                    throw ProtectionRefusal($"removing the widget of {pair.Value} from its page");
                if (newWidget.PageID != oldWidget.PageID)
                    throw ProtectionRefusal($"moving the widget of {pair.Value} to another page");
            }
        }

        // The objects of signed signature fields and locked fields, each with a description of its field: the
        // field, the fields above it, and everything the field refers to (its signature, lock, appearances and their
        // resources), except pages, the catalog, the form and other fields. The field nodes (fields and widgets) are
        // also returned on their own.
        Dictionary<PdfObjectID, string> ProtectedObjects(Snapshot old, out Dictionary<PdfObjectID, string> nodes)
        {
            var result = new Dictionary<PdfObjectID, string>();
            nodes = new Dictionary<PdfObjectID, string>();
            var signedNames = new HashSet<string>(old.Fields.Values.Where(field => IsSigned(field, out _)).Select(field => field.Name), StringComparer.Ordinal);
            foreach (var pair in old.Fields)
            {
                var field = pair.Value;
                string reason;
                if (signedNames.Contains(field.Name))
                    reason = $"the signed signature field '{field.Name}'";
                else if (IsLocked(field.Name))
                    reason = $"the form field '{field.Name}' (locked by a signature)";
                else
                    continue;
                nodes[pair.Key] = reason;
                for (var current = field; current != null; current = Parent(old, current))
                {
                    if (current.Reference != null && !result.ContainsKey(current.Reference.ObjectID))
                        result[current.Reference.ObjectID] = reason;
                }
                foreach (var id in ReferencedObjects(old, field.Dictionary))
                {
                    if (!result.ContainsKey(id))
                        result[id] = reason;
                }
            }
            return result;

            static FieldNode? Parent(Snapshot snapshot, FieldNode field)
            {
                if (field.Parent is { } parent && snapshot.Fields.TryGetValue(parent, out var node))
                    return node;
                return null;
            }
        }

        // The indirect objects a field node refers to, directly or through others. It does not follow /Parent, /P
        // (the page), /Kids or /Data (a signature reference's document), and stops at pages, the page tree, the
        // catalog, the form, other field nodes and other annotations, which have rules of their own.
        static HashSet<PdfObjectID> ReferencedObjects(Snapshot old, PdfDictionary field)
        {
            var result = new HashSet<PdfObjectID>();
            var pending = new Stack<PdfItem>();
            pending.Push(field);
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (item is PdfReference reference)
                {
                    if (old.IsBoundary(reference.ObjectID) || !result.Add(reference.ObjectID) || reference.Value is not { } value)
                        continue;
                    pending.Push(value);
                }
                else if (item is PdfDictionary dictionary)
                {
                    foreach (var key in dictionary.Elements.Keys)
                    {
                        if (key is "/Parent" or "/P" or "/Kids" or "/Data")
                            continue;
                        if (dictionary.Elements[key] is { } child)
                            pending.Push(child);
                    }
                }
                else if (item is PdfArray array)
                {
                    foreach (var child in array.Elements)
                        pending.Push(child);
                }
            }
            return result;
        }

        void CheckChange(PdfObjectID id, PdfObject oldObject, PdfObject newObject, Snapshot old, Snapshot now)
        {
            // Without a certification, only the protected objects are restricted.
            if (!Restricted)
                return;
            // The document security store's own objects (ISO 32000-2 12.8.4.3) may change at every level.
            if (old.DssObjects.Contains(id))
                return;

            if (old.Fields.ContainsKey(id))
            {
                Need(2, $"filling in the form field '{old.Fields[id].Name}'");
                CheckKeys(old, now, oldObject, newObject, $"the form field '{old.Fields[id].Name}'", FieldKeys);
                return;
            }
            // Appearance streams that a page also draws change the page, which falls under the last rule.
            bool drawnByPage = old.PageXObjects.Contains(id);
            if (old.Appearances.Contains(id) && !drawnByPage)
            {
                Need(2, $"changing the appearance of a form field (object {id.ObjectNumber})");
                return;
            }
            if (old.AnnotationAppearances.TryGetValue(id, out var annotationPage) && !drawnByPage)
            {
                Need(3, $"changing the appearance of an annotation on page {annotationPage + 1} (object {id.ObjectNumber})");
                return;
            }

            // The document information and metadata may change at P=2 and P=3; at P=1 an update that only adds
            // validation data leaves them as they are (PdfIncrementalUpdate does not write /ModDate then).
            if (id == old.Info)
            {
                Need(2, "changing the document information");
                return;
            }
            if (id == old.Metadata)
            {
                Need(2, "changing the document metadata (XMP)");
                return;
            }
            if (id == old.Catalog)
            {
                CheckKeys(old, now, oldObject, newObject, "the document catalog", _permission == 1 ? CatalogKeysAtP1 : CatalogKeys);
                if (oldObject is PdfDictionary oldCatalog && newObject is PdfDictionary newCatalog)
                    CheckFormEntry(old, now, oldCatalog, newCatalog);
                return;
            }
            if (id == old.Form)
            {
                if (oldObject is not PdfDictionary oldForm || newObject is not PdfDictionary newForm)
                    throw Refusal($"changing the interactive form (AcroForm, object {id.ObjectNumber})");
                CheckForm(old, now, oldForm, newForm);
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
                {
                    Need(2, $"changing a form widget on page {annotation.Page + 1}");
                    CheckKeys(old, now, oldObject, newObject, $"a form widget on page {annotation.Page + 1}", WidgetKeys);
                }
                else
                    Need(3, $"changing a {annotation.Subtype} annotation on page {annotation.Page + 1} (object {id.ObjectNumber})");
                return;
            }
            throw Refusal($"changing {Describe(oldObject, id)}: {string.Join(", ", ChangedKeys(old, now, oldObject, newObject))}");
        }

        // The catalog's /AcroForm: the same indirect form is checked when it is written; a form that is removed is
        // refused; a new or direct form is compared with the old one, and an added form may hold only new signature
        // fields.
        void CheckFormEntry(Snapshot old, Snapshot now, PdfDictionary oldCatalog, PdfDictionary newCatalog)
        {
            var oldItem = oldCatalog.Elements[PdfCatalog.Keys.AcroForm];
            var newItem = newCatalog.Elements[PdfCatalog.Keys.AcroForm];
            if (oldItem is PdfReference oldReference && newItem is PdfReference newReference && oldReference.ObjectID == newReference.ObjectID)
                return;
            var oldForm = Resolve(oldItem) as PdfDictionary;
            var newForm = Resolve(newItem) as PdfDictionary;
            if (oldForm == null && newForm == null)
                return;
            if (newForm == null)
                throw Refusal("removing the interactive form (AcroForm)");
            if (oldForm == null)
            {
                CheckFieldArray(old, now, null, Resolve(newForm.Elements[PdfAcroFormKeys.Fields]) as PdfArray);
                return;
            }
            CheckForm(old, now, oldForm, newForm);
        }

        void CheckForm(Snapshot old, Snapshot now, PdfDictionary oldForm, PdfDictionary newForm)
        {
            CheckKeys(old, now, oldForm, newForm, "the interactive form (AcroForm)", _permission == 1 ? FormKeysAtP1 : FormKeys);
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
                if (_permission == 1 && !IsDocumentTimeStamp(now, item))
                    throw Refusal($"adding the signature field '{FieldName(item)}'; only document time-stamps may be added");
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
                    if (_permission == 1 && !IsDocumentTimeStamp(now, item))
                        throw Refusal($"adding a signature widget to {page}; only document time-stamps may be added");
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

        // A new signature field holding a document time-stamp (ISO 32000-2 12.8.5): /Type /DocTimeStamp with
        // /SubFilter /ETSI.RFC3161, a /ByteRange and /Contents, nothing that changes permissions or acts (no transform
        // /Reference, field lock or action), and no visible widget, so that it cannot cover the page.
        static bool IsDocumentTimeStamp(Snapshot now, PdfItem item)
        {
            if (item is not PdfReference reference || !now.Fields.TryGetValue(reference.ObjectID, out var field))
                return false;
            if (field.Type != "/Sig" || Resolve(field.Dictionary.Elements["/V"]) is not PdfDictionary value)
                return false;
            return NameOf(value, "/Type") == "/DocTimeStamp"
                && NameOf(value, "/SubFilter") == "/ETSI.RFC3161"
                // While FreePDF saves its own, these are still placeholders for the values.
                && value.Elements.ContainsKey("/ByteRange")
                && value.Elements.ContainsKey("/Contents")
                && !value.Elements.ContainsKey("/Reference")
                && !field.Dictionary.Elements.ContainsKey("/Lock")
                && !field.Dictionary.Elements.ContainsKey("/A")
                && !field.Dictionary.Elements.ContainsKey("/AA")
                && Widgets(field.Dictionary).All(IsInvisible);
        }

        static string NameOf(PdfDictionary dictionary, string key) => Resolve(dictionary.Elements[key]) is PdfName name ? name.Value : "";

        // The field itself when it is its own widget, and its kids.
        static IEnumerable<PdfDictionary> Widgets(PdfDictionary field)
        {
            if (NameOf(field, "/Subtype") == "/Widget")
                yield return field;
            if (Resolve(field.Elements["/Kids"]) is PdfArray kids)
            {
                foreach (var kid in kids.Elements)
                {
                    if (Resolve(kid) is PdfDictionary widget)
                        yield return widget;
                }
            }
        }

        // A widget that shows nothing on screen or on paper: Hidden (ISO 32000-1 Table 165; NoView alone still prints and
        // can be toggled), or a rectangle without area.
        static bool IsInvisible(PdfDictionary widget)
        {
            int flags = Resolve(widget.Elements["/F"]) is PdfInteger f ? f.Value : 0;
            if ((flags & 2) != 0)
                return true;
            if (Resolve(widget.Elements["/Rect"]) is not PdfArray rect || rect.Elements.Count != 4)
                return true;
            // Numbers may be indirect objects; anything that is not a number counts as visible, to be safe.
            var numbers = rect.Elements.Select(e => Resolve(e) switch
            {
                PdfInteger i => (double?)i.Value,
                PdfIntegerObject i => i.Value,
                PdfReal r => r.Value,
                PdfRealObject r => r.Value,
                PdfLongInteger l => l.Value,
                _ => null,
            }).ToArray();
            if (numbers.Any(n => n == null))
                return false;
            return numbers[0] == numbers[2] || numbers[1] == numbers[3];
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

        /// <summary>
        /// Gets a value indicating whether the signatures permit no changes but validation data and document
        /// time-stamps (P=1), so that a save must not update the document information by itself.
        /// </summary>
        internal bool PermitsOnlyValidationData => _permission == 1;

        bool IsLocked(string fieldName) => _locks.Any(fieldLock => fieldLock.Locks(fieldName));

        InvalidOperationException Refusal(string change)
        {
            var permits = _permission switch
            {
                1 => "no changes but validation data and document time-stamps",
                2 => "only filling in forms and signing",
                _ => "only filling in forms, signing and annotating",
            };
            return Refused(
                $"The signatures of this document permit {permits} ({(_certified ? "DocMDP" : "signature field lock")} P={_permission}), so saving would make " +
                $"validators report them as broken: {change}. Nothing was written.",
                change);
        }

        static InvalidOperationException ProtectionRefusal(string change)
        {
            var exception = Refused($"A signature of this document protects what this save changes, so saving would make validators " +
                   $"report that signature as broken: {change}. Nothing was written.", $"{change} (protected by a signature)");
            exception.Data[ProtectedKey] = true;
            return exception;
        }

        /// <summary>
        /// The key under which a refusal's <see cref="Exception.Data"/> holds true when the change touches a signed or
        /// locked field itself, which no signature permits.
        /// </summary>
        internal const string ProtectedKey = "FreePDF.Protected";

        /// <summary>
        /// The key under which a refusal's <see cref="Exception.Data"/> holds the description of the change alone, for the
        /// signature verifier.
        /// </summary>
        internal const string RefusedChangeKey = "FreePDF.RefusedChange";

        static InvalidOperationException Refused(string message, string change)
        {
            var exception = new InvalidOperationException(message);
            exception.Data[RefusedChangeKey] = change;
            return exception;
        }

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

        const int MaxQualifiedName = 4096;

        // Every node of the form's field tree with its fully qualified name and its field type, which kids inherit. Without
        // names, every name is "" and none is refused for its length: reading the rules needs no names, and every file opened
        // in Append mode reads them, signed or not.
        static IEnumerable<FieldNode> Fields(PdfDocument document, bool names = true)
        {
            var form = Resolve(document.Catalog.Elements[PdfCatalog.Keys.AcroForm]) as PdfDictionary;
            if (Resolve(form?.Elements[PdfAcroFormKeys.Fields]) is not PdfArray fields)
                yield break;
            var visited = new HashSet<PdfObjectID>();
            var pending = new Stack<(PdfItem Item, string Parent, string? Type, PdfObjectID? ParentID)>();
            foreach (var item in Enumerable.Reverse(fields.Elements))
                pending.Push((item, "", null, null));
            while (pending.Count > 0)
            {
                var (item, parent, inheritedType, parentID) = pending.Pop();
                if (Resolve(item) is not PdfDictionary dictionary)
                    continue;
                if (item is PdfReference reference && !visited.Add(reference.ObjectID))
                    continue;
                // A node without /T is a widget of its parent field; /FT is inherited (ISO 32000-1 12.7.3.1).
                var partial = names ? dictionary.Elements.GetString("/T") : "";
                var name = parent;
                if (!String.IsNullOrEmpty(partial))
                    name = parent.Length == 0 ? partial : parent + "." + partial;
                // Names are built along the tree from names the file chooses; locks match them in full, so a longer one
                // is not cut but refused (thousands of kids under one huge name would otherwise cost gigabytes).
                if (name.Length > MaxQualifiedName)
                    throw new InvalidOperationException($"A form field's fully qualified name is longer than {MaxQualifiedName} characters.");
                // GetName gives "/" for a missing entry, which would hide the inherited type: a signed field whose /FT is
                // on its parent would not count as signed, and its lock would be lost.
                var type = Resolve(dictionary.Elements["/FT"]) is PdfName { Value.Length: > 1 } ownType ? ownType.Value : inheritedType;
                var node = new FieldNode(dictionary, item as PdfReference, name, type, parentID);
                yield return node;
                if (Resolve(dictionary.Elements["/Kids"]) is PdfArray kids)
                {
                    foreach (var kid in Enumerable.Reverse(kids.Elements))
                        pending.Push((kid, name, type, node.Reference?.ObjectID ?? parentID));
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
                CollectDss(document.Catalog.Elements["/DSS"]);
                // An object the document also reaches without the DSS (a page's content, a field) is not validation
                // data, whatever the DSS refers to.
                if (DssObjects.Count > 0)
                    DssObjects.ExceptWith(ReachableWithoutDss(document));
                var formItem = document.Catalog.Elements[PdfCatalog.Keys.AcroForm];
                Form = (formItem as PdfReference)?.ObjectID;
                FieldArray = ((Resolve(formItem) as PdfDictionary)?.Elements[PdfAcroFormKeys.Fields] as PdfReference)?.ObjectID;

                foreach (var field in Fields(document))
                {
                    if (field.Reference != null)
                        Fields[field.Reference.ObjectID] = field;
                    if (field.Dictionary.Elements.GetName("/Subtype") == "/Widget")
                        Appearances.UnionWith(AppearanceObjects(field.Dictionary));
                }

                for (int index = 0; index < document.PageCount; index++)
                {
                    var page = document.Pages[index];
                    if (page.Reference != null)
                        Pages[page.ObjectID] = index;
                    if (Resolve(page.Elements["/Resources"]) is PdfDictionary resources
                        && Resolve(resources.Elements["/XObject"]) is PdfDictionary xObjects)
                    {
                        foreach (var value in xObjects.Elements.Values)
                        {
                            if (value is PdfReference xObject)
                                PageXObjects.Add(xObject.ObjectID);
                        }
                    }
                    var annotationsItem = page.Elements["/Annots"];
                    if (annotationsItem is PdfReference annotationsReference)
                        AnnotationArrays[annotationsReference.ObjectID] = index;
                    if (Resolve(annotationsItem) is not PdfArray annotations)
                        continue;
                    foreach (var item in annotations.Elements)
                    {
                        if (item is not PdfReference annotationReference || annotationReference.Value is not PdfDictionary annotation)
                            continue;
                        var subtype = annotation.Elements.GetName("/Subtype");
                        Annotations[annotationReference.ObjectID] = new AnnotationNode(index, page.Reference?.ObjectID, subtype.TrimStart('/'), subtype == "/Widget");
                        if (subtype != "/Widget")
                        {
                            foreach (var appearance in AppearanceObjects(annotation))
                                AnnotationAppearances[appearance] = index;
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
            internal HashSet<PdfObjectID> Appearances { get; } = new();
            internal Dictionary<PdfObjectID, int> AnnotationAppearances { get; } = new();
            internal HashSet<PdfObjectID> PageXObjects { get; } = new();
            internal Dictionary<PdfObjectID, int> Pages { get; } = new();
            internal Dictionary<PdfObjectID, int> AnnotationArrays { get; } = new();
            internal Dictionary<PdfObjectID, AnnotationNode> Annotations { get; } = new();
            internal HashSet<PdfObjectID> DssObjects { get; } = new();

            // The indirect objects reachable from the trailer when the catalog's /DSS is left out.
            static HashSet<PdfObjectID> ReachableWithoutDss(PdfDocument document)
            {
                var result = new HashSet<PdfObjectID>();
                var catalog = document.Catalog.ObjectID;
                var pending = new Stack<PdfItem>();
                foreach (var item in document.Trailer.Elements.Values)
                {
                    if (item != null)
                        pending.Push(item);
                }
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    if (current is PdfReference reference)
                    {
                        if (!result.Add(reference.ObjectID) || reference.Value is not { } value)
                            continue;
                        current = value;
                    }
                    if (current is PdfDictionary dictionary)
                    {
                        bool isCatalog = dictionary.Reference?.ObjectID == catalog;
                        foreach (var key in dictionary.Elements.Keys)
                        {
                            if (isCatalog && key == "/DSS")
                                continue;
                            if (dictionary.Elements[key] is { } child)
                                pending.Push(child);
                        }
                    }
                    else if (current is PdfArray array)
                    {
                        foreach (var child in array.Elements)
                            pending.Push(child);
                    }
                }
                return result;
            }

            // The indirect objects of the document security store: the DSS, its arrays and VRI dictionaries, and the
            // streams of certificates, OCSP responses and CRLs.
            void CollectDss(PdfItem? item)
            {
                var pending = new Stack<PdfItem>();
                if (item != null)
                    pending.Push(item);
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    if (current is PdfReference reference)
                    {
                        if (!DssObjects.Add(reference.ObjectID) || reference.Value is not { } value)
                            continue;
                        current = value;
                    }
                    if (current is PdfDictionary dictionary)
                    {
                        foreach (var child in dictionary.Elements.Values)
                            pending.Push(child);
                    }
                    else if (current is PdfArray array)
                    {
                        foreach (var child in array.Elements)
                            pending.Push(child);
                    }
                }
            }

            // Objects with rules of their own, at which the objects of a protected field end.
            internal bool IsBoundary(PdfObjectID id)
            {
                if (id == Catalog || id == Form || id == FieldArray || Pages.ContainsKey(id) || AnnotationArrays.ContainsKey(id)
                    || Fields.ContainsKey(id) || Annotations.ContainsKey(id))
                {
                    return true;
                }
                var type = (Document.Internals.GetObject(id) as PdfDictionary)?.Elements.GetName("/Type");
                return type is "/Page" or "/Pages" or "/Catalog";
            }
        }

        // The indirect objects of an annotation's appearance (ISO 32000-1 12.5.5): the /AP dictionary, its /N, /R and
        // /D entries, and the streams of a dictionary of states.
        static IEnumerable<PdfObjectID> AppearanceObjects(PdfDictionary annotation)
        {
            var apItem = annotation.Elements["/AP"];
            if (apItem is PdfReference apReference)
                yield return apReference.ObjectID;
            if (Resolve(apItem) is not PdfDictionary appearances)
                yield break;
            foreach (var key in new[] { "/N", "/R", "/D" })
            {
                var item = appearances.Elements[key];
                if (item is PdfReference reference)
                    yield return reference.ObjectID;
                if (Resolve(item) is PdfDictionary { Stream: null } states)
                {
                    foreach (var state in states.Elements.Values)
                    {
                        if (state is PdfReference stateReference)
                            yield return stateReference.ObjectID;
                    }
                }
            }
        }

        sealed class FieldNode(PdfDictionary dictionary, PdfReference? reference, string name, string? type, PdfObjectID? parent)
        {
            public PdfDictionary Dictionary { get; } = dictionary;
            public PdfReference? Reference { get; } = reference;
            public string Name { get; } = name;
            public string? Type { get; } = type;
            public PdfObjectID? Parent { get; } = parent;
        }

        sealed class AnnotationNode(int page, PdfObjectID? pageID, string subtype, bool isWidget)
        {
            public int Page { get; } = page;
            public PdfObjectID? PageID { get; } = pageID;
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
                        // A name can also be an indirect string object.
                        if (Resolve(item) is PdfString name)
                            names.Add(name.Value);
                        else if (Resolve(item) is PdfStringObject nameObject)
                            names.Add(nameObject.Value);
                    }
                }
                return new FieldLock(parameters.Elements.GetName("/Action"), names);
            }

            // A name also covers the fields below it. An unknown action locks everything, the safe reading.
            internal bool Locks(string fieldName)
            {
                // Without building name + "." for every field: a lock may name a field with a very long name.
                bool listed = _names.Any(name => fieldName == name
                                                 || (fieldName.Length > name.Length && fieldName[name.Length] == '.'
                                                     && fieldName.StartsWith(name, StringComparison.Ordinal)));
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
        static readonly HashSet<string> CatalogKeys = new(StringComparer.Ordinal) { "/AcroForm", "/Version", "/Metadata", "/Extensions", "/DSS" };
        // At P=1 the catalog may only gain or change its document security store and the ESIC extension declaration
        // (EN 319 142-1 5.6, also for a document time-stamp), and the form only new document time-stamp fields.
        static readonly HashSet<string> CatalogKeysAtP1 = new(StringComparer.Ordinal) { "/DSS", "/Extensions" };
        static readonly HashSet<string> FormKeysAtP1 = new(StringComparer.Ordinal) { "/Fields", "/SigFlags" };
        static readonly HashSet<string> FormKeys = new(StringComparer.Ordinal) { "/Fields", "/SigFlags", "/DR", "/NeedAppearances" };
        static readonly HashSet<string> PageKeys = new(StringComparer.Ordinal) { "/Annots" };

        static class PdfAcroFormKeys
        {
            internal const string Fields = "/Fields";
        }

        readonly int _permission;
        readonly bool _certified;
        readonly List<FieldLock> _locks;
    }
}
