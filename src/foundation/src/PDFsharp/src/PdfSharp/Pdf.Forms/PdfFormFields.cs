// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using Microsoft.Extensions.Logging;
using PdfSharp.Logging;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;

// v7.0.0 TODO review

namespace PdfSharp.Pdf.Forms
{
    /// <summary>
    /// Holds a collection of interactive fields (Acro fields).
    /// </summary>
    public sealed class PdfFormFields : PdfArray, IEnumerable<PdfFormField>
    {
        internal PdfFormFields(PdfDocument document)
            : base(document)
        { }

        internal PdfFormFields(PdfArray array)
            : base(array)
        { }
        
        //internal void GetDescendantNames(ref List<string> names, string? partialName) // TODO: FormsCleanUp: Remove. Use FullName for each field instead.
        //{
        //    int count = Elements.Count;
        //    for (int idx = 0; idx < count; idx++)
        //    {
        //        var field = this[idx];
        //        if (field != null!)
        //            field.GetDescendantNames(ref names, partialName);
        //    }
        //}

        /// <summary>
        /// Gets a field from the collection. For your convenience an instance of a derived class like
        /// PdfTextField or PdfCheckBox is returned if PDFsharp can guess the actual type of the dictionary.
        /// If the actual type cannot be guessed by PDFsharp the function returns an instance
        /// of PdfGenericField.
        /// </summary>
        public PdfFormField this[int index]
        {
            get
            {
#if true
                var item = Elements.GetRequiredValue<PdfFormField>(index);
                return item;
#else
                PdfItem item = Elements[index];
                Debug.Assert(item is PdfReference);
                PdfDictionary? dict = ((PdfReference)item).Value as PdfDictionary;
                Debug.Assert(dict != null);
                PdfFormField? field = dict as PdfFormField;
                if (field == null && dict != null!)
                {
                    // Do type transformation
                    field = null; // CreateAcroField(dict); MAKE CODE COMPILE
                    //Elements[index] = field.XRef;
                }

                return field!; // NRT
#endif
            }
        }

        /// <summary>
        /// Gets the number of top-level fields in this collection.
        /// </summary>
        public int Count => Elements.Count;

        /// <summary>
        /// Gets the field with the specified fully qualified name, such as "name" or "address.city" for the kid
        /// "city" of the field "address", or null if the form has no such field.
        /// </summary>
        public PdfFormField? this[string fullName]
        {
            get
            {
                TryGetField(fullName, out var field);
                return field;
            }
        }

        /// <summary>
        /// Tries to get the field with the specified fully qualified name, such as "address.city".
        /// </summary>
        public bool TryGetField(string fullName, [MaybeNullWhen(false)] out PdfFormField field)
        {
            if (fullName == null!)
                throw new ArgumentNullException(nameof(fullName));
            field = GetAllFields().FirstOrDefault(f => f.FullName == fullName);
            return field != null;
        }

        /// <summary>
        /// Returns an enumerator that iterates through an array of Acro fields.
        /// </summary>
        public new IEnumerator<PdfFormField> GetEnumerator()
        {
            foreach (var item in Elements)
            {
                var field = item;
                PdfReference.Dereference(ref field);
                yield return (PdfFormField)field;
            }
        }

        /// <summary>
        /// Gets all fields recursively.
        /// </summary>
        /// <param name="onlyFullyQualifiedFields">Returns only fields that shall not be considered as widget annotations only.</param>
        public IEnumerable<PdfFormField> GetAllFields(bool onlyFullyQualifiedFields = true)
        {
            foreach (var field in this)
            {
                if (onlyFullyQualifiedFields && !field.IsFullyQualifiedField())
                    continue;
                
                yield return field;

                foreach (var descendant in field.GetDescendants(onlyFullyQualifiedFields))
                    yield return descendant;
            }
        }

        internal static class AcroFieldPreparer
        {
            public static void PrepareDocument(PdfDocument doc)
            {
                CreateAcroFieldObjects(doc);
                CreateAnnotationObjects(doc);
            }

            /// <summary>
            /// Creates all acro field objects after reading a PDF document.
            /// </summary>
            /// <param name="doc">The document.</param>
            static void CreateAcroFieldObjects(PdfDocument doc)
            {
                var fields = doc.Catalog.GetAcroForm()?.Elements.GetArray(PdfForm.Keys.Fields);
                if (fields != null)
                    PrepareFields(fields, new HashSet<PdfReference>());
            }

            /// <summary>
            /// Types the fields of a /Fields or /Kids array and their descendants. Entries that cannot be fields
            /// (numbers, missing objects, objects that already have another type such as a page) and entries already
            /// seen (a loop through /Kids) are dropped: they carry no field, and converting them would destroy them
            /// or never end.
            /// </summary>
            static void PrepareFields(PdfArray fields, HashSet<PdfReference> seen)
            {
                for (int idx = 0; idx < fields.Elements.Count; idx++)
                {
                    var dict = fields.Elements.GetDictionary(idx);
                    bool usable = dict is PdfFormField || dict?.GetType() == typeof(PdfDictionary);
                    if (!usable || (dict!.Reference != null && !seen.Add(dict.Reference)))
                    {
                        PdfSharpLogHost.Logger.LogWarning("Dropped a form field array entry that is not a field or is listed twice.");
                        fields.Elements.RemoveAt(idx--);
                        continue;
                    }

                    CreateDerivedField(fields, idx);
                    var acroField = fields.Elements.GetRequiredDictionary<PdfFormField>(idx);
                    HandleAP(acroField);
                    var kids = acroField.Elements.GetArray(PdfFormField.Keys.Kids);
                    if (kids != null)
                        PrepareFields(kids, seen);
                }
            }

            static void CreateAnnotationObjects(PdfDocument doc)
            {
                var pages = doc.Catalog.Pages;
                foreach (var page in pages)
                {
                    var annots = page.Elements.GetArray(PdfPage.Keys.Annots);
                    if (annots != null)
                    {
                        for (int idx = 0; idx < annots.Elements.Count; idx++)
                        {
                            var annot = annots.Elements.GetDictionary(idx);
                            if (annot is PdfWidgetAnnotation widget)
                            {
                                throw new InvalidOperationException("WTF happened???");
                            }
                            else if (annot is PdfFormField field)
                            {
                                if (field.TryGetAsWidgetAnnotation(out var proxyWidget))
                                    annots.Elements[idx] = proxyWidget;
                                else
                                {
                                    PdfSharpLogHost.Logger.LogWarning("A PdfFormField was found in the /Annots array instead of a PdfWidgetAnnotation. The field is not valid here and will be removed to avoid exceptions.");
                                    annots.Elements.RemoveAt(idx--);
                                }
                            }
                            else
                            {

                            }
                        }
                    }
                }
            }

            static void CreateDerivedField(PdfArray fields, int index)
            {
                var field = fields.Elements.GetRequiredDictionary(index);

                var (type, isWidget) = PdfFormField.GetAcroFieldType(field);
                var newField = (PdfFormField)fields.Elements.GetRequiredDictionary(index, VCF.None, type);
                if (isWidget)
                    newField.GetAsWidgetAnnotation();
            }

            // ReSharper disable once InconsistentNaming
            static void HandleAP(PdfFormField field)
            {
                var ap = field.Elements.GetDictionary<PdfAnnotationAppearance>(PdfAnnotation.Keys.AP);
                if (ap != null)
                {
                    var valN = ap.Elements.GetDictionary(PdfAnnotationAppearance.Keys.N);
                    HandleAPEntry(valN);

                    var valR = ap.Elements.GetDictionary(PdfAnnotationAppearance.Keys.R);
                    HandleAPEntry(valR);

                    var valD = ap.Elements.GetDictionary(PdfAnnotationAppearance.Keys.D);
                    HandleAPEntry(valD);
                }

                // ReSharper disable once InconsistentNaming
                void HandleAPEntry(PdfDictionary? streamOrDict)
                {
                    if (streamOrDict == null)
                        return;

                    var stream = streamOrDict.Stream;
                    if (stream is null)
                    {
                        var keys = streamOrDict.Elements.Keys;
                        foreach (var key in keys)
                        {
                            var xo = streamOrDict.Elements.GetDictionary(key);
                            if (xo is PdfFormXObject)
                                continue;

                            if (xo != null)
                            {
                                Debug.Assert(xo.IsIndirect);
#if true
                                var xo2 = streamOrDict.Elements.GetDictionary(key, VCF.None, typeof(PdfFormXObject));
#else
                        TransformToXObject(xo);
#endif
                                Debug.Assert(xo.IsDead);
                            }
                            else
                            {
                                Debug.Assert(false);
                            }
                        }
                    }
                    else
                    {
                        Debug.Assert(streamOrDict.IsIndirect);
                        TransformToXObject(streamOrDict);
                    }

                    void TransformToXObject(PdfDictionary dict)
                    {
                        if (dict is PdfFormXObject alreadyFormXObject)
                        {
                            _ = typeof(int);
                        }
                        else
                        {
                            dict.Elements.CreateContainer(typeof(PdfFormXObject), dict, true);
                            Debug.Assert(dict.IsDead);
                        }
                    }
                }
            }
        }
    }
}
