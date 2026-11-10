// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

#if !NET8_0_OR_GREATER
using System.Text;
#endif
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using System.Text;

// v7.0.0 TODO review

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// PdfDocument signature handler.
    /// Attaches a PKCS#7 signature digest to PdfDocument.
    /// </summary>
    public class DigitalSignatureHandler
    {
        /// <summary>
        /// Large enough space reserved by PdfPlaceholder to be replaced by the actual computed value of the byte range to sign.
        /// Worst case: signature dictionary is near the end of an 10 GB PDF file. So we reserve 10 digits.
        /// However, the current implementation can only support 2 GB files.
        /// </summary>
        const int ByteRangePlaceholderLength = 36; // = "[0 9999999999 9999999999 9999999999]".Length

        DigitalSignatureHandler(PdfDocument document, IDigitalSigner signer, DigitalSignatureOptions options)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Signer = signer ?? throw new ArgumentNullException(nameof(signer));
            Options = options ?? throw new ArgumentNullException(nameof(options));

            if (options.PageIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.PageIndex),
                    "Signature page index cannot be negative.");
            }

            if (IsDocumentTimeStamp && (options.FieldName != null || options.Certification != null || options.Rectangle != default))
            {
                // ISO 32000-2 12.8.5: a document time-stamp is not a signature of a person; it gets its own invisible field.
                throw new ArgumentException("A document time-stamp gets its own invisible field: FieldName, Certification and " +
                    "Rectangle must be left unset.", nameof(options));
            }

            if (options.FieldName != null)
            {
                if (options.Rectangle != default || options.PageIndex != 0)
                {
                    throw new ArgumentException($"The signature goes into the widget of the field '{options.FieldName}', " +
                        "so Rectangle and PageIndex must be left unset.", nameof(options));
                }
                // Mistakes surface here rather than when the document is saved.
                CheckField(FindUnsignedSignatureField());
            }
            if (options.Certification is { } level)
                CheckCertification(level);

            // TODO_OLD in document: Set document version depending on digest type from options.  // TODO: what?
        }

        /// <summary>
        /// Gets or creates the digital signature handler for the specified document.
        /// </summary>
        public static DigitalSignatureHandler ForDocument(PdfDocument document, IDigitalSigner signer, DigitalSignatureOptions options)
        {
            return document.DigitalSignatureHandler ??= new(document, signer, options);
        }

        /// <summary>
        /// Gets the PDF document the signature will be attached to.
        /// </summary>
        public PdfDocument Document { get; init; }

        /// <summary>
        /// Gets the options for the digital signature.
        /// </summary>
        public DigitalSignatureOptions Options { get; init; }

        IDigitalSigner Signer { get; init; }

        // FreePDF: the signer's format (IDigitalSignerFormat) decides the /SubFilter: ETSI.CAdES.detached for PAdES
        // (ISO 32000-2 12.8.3.4), ETSI.RFC3161 for a document time-stamp (12.8.5), adbe.pkcs7.detached otherwise.
        PdfSignatureFormat Format => (Signer as IDigitalSignerFormat)?.Format ?? PdfSignatureFormat.Pkcs7Detached;

        string SubFilter => Format switch
        {
            PdfSignatureFormat.Pades => "/ETSI.CAdES.detached",
            PdfSignatureFormat.DocumentTimeStamp => "/ETSI.RFC3161",
            _ => "/adbe.pkcs7.detached",
        };

        bool IsDocumentTimeStamp => Format == PdfSignatureFormat.DocumentTimeStamp;

        /// <summary>
        /// Computes the signature and byte range after the document stream was written.
        /// </summary>
        internal async Task ComputeSignatureAndRange(PdfWriter writer)
        {
            var (rangedStreamToSign, byteRangeArray) = GetRangeToSignAndByteRangeArray(writer.Stream);

            // Write the /ByteRange entry '[...2 times offset, length...]'.
            Debug.Assert(_byteRangePlaceholder != null);
            var sb = new StringBuilder(4096);
            sb.Append(byteRangeArray);
            // Length must match placeholder.
            sb.Append(new String(' ', _byteRangePlaceholder.Length - sb.Length));
            _byteRangePlaceholder.SetValue(sb.ToString());
            _byteRangePlaceholder.WriteEffectiveValue(writer);

            // Computing signature from document’s digest.
            var signature = await Signer.GetSignatureAsync(rangedStreamToSign).ConfigureAwait(false);

            Debug.Assert(_contentsPlaceholder != null);
            //int expectedLength = (_contentsPlaceholder.Length - 2) / 2;
            int contentsHexLength = 2 * signature.Length + 2;
            if (contentsHexLength > _contentsPlaceholder.Length)
            {
                // This should not happen.
                throw new InvalidOperationException(
                    $"The actual digest length '{contentsHexLength}' is larger than the approximation made '{_contentsPlaceholder.Length}'. " +
                    "Not enough space in the placeholder to fit the hex-encoded signature.");
            }

            // Write the /Contents entry '<...signature hex string...>'.
            // When the signature includes a timestamp, the exact length is unknown until the signature is definitely calculated.
            // Therefore, we write the angle brackets here and override the placeholder white-spaces.
            // According to the PDF reference, the Contents key of a signature dictionary shall not be encrypted.
            sb.Clear();
            sb.Append('<');
            sb.Append(FormatHex(signature));
#if true
            // Filler bytes must be part of the hex string. Trailing blanks are invalid PDF.
            //sb.Append(new String('0', 2 * (expectedLength - signature.Length)));
            sb.Append(new String('0', _contentsPlaceholder.Length - sb.Length - 1));
            sb.Append('>');
#else
            sb.Append('>');
            sb.Append(new String(' ', 2 * (expectedLength - signature.Length)));
#endif
            _contentsPlaceholder.SetValue(sb.ToString());
            _contentsPlaceholder.WriteEffectiveValue(writer);
        }

        string FormatHex(byte[] bytes)  // ...use RawEncoder
        {
#if NET8_0_OR_GREATER
            return Convert.ToHexString(bytes);
#else
            var result = new StringBuilder();

            for (int idx = 0; idx < bytes.Length; idx++)
                result.AppendFormat("{0:X2}", bytes[idx]);

            return result.ToString();
#endif
        }

        /// <summary>
        /// Get the bytes ranges to sign.
        /// As recommended in PDF specs, whole document will be signed, except for the hexadecimal signature token value in the /Contents entry.
        /// Example: '/Contents &lt;aaaaa111111&gt;' => '&lt;aaaaa111111&gt;' will be excluded from the bytes to sign.
        /// </summary>
        /// <param name="stream"></param>
        (RangedStream rangedStream, PdfArray byteRangeArray) GetRangeToSignAndByteRangeArray(Stream stream)
        {
            Debug.Assert(_contentsPlaceholder != null, nameof(_contentsPlaceholder) + " must not be null here.");

            var firstRangeOffset = 0L;
            var firstRangeLength = _contentsPlaceholder.StartPosition;
            var secondRangeOffset = _contentsPlaceholder.EndPosition;
            var secondRangeLength = stream.Length - _contentsPlaceholder.EndPosition;

            var byteRangeArray = new PdfArray();
#if USE_LONG_SIZE
            byteRangeArray.Elements.Add(new PdfLongInteger(firstRangeOffset));
            byteRangeArray.Elements.Add(new PdfLongInteger(firstRangeLength));
            byteRangeArray.Elements.Add(new PdfLongInteger(secondRangeOffset));
            byteRangeArray.Elements.Add(new PdfLongInteger(secondRangeLength));
#else
            byteRangeArray.Elements.Add(new PdfInteger(firstRangeOffset));
            byteRangeArray.Elements.Add(new PdfInteger(firstRangeLength));
            byteRangeArray.Elements.Add(new PdfInteger(secondRangeOffset));
            byteRangeArray.Elements.Add(new PdfInteger(secondRangeLength));
#endif
            var rangedStream = new RangedStream(stream,
            [
                new(firstRangeOffset, firstRangeLength),
                new(secondRangeOffset, secondRangeLength)
            ]);

            return (rangedStream, byteRangeArray);
        }

        /// <summary>
        /// Adds the PDF objects required for a digital signature as placeholders.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal async Task AddSignatureComponentsAsync() // #US321 TODO Use appropriate classes.
        {
            if (Options.FieldName == null && Options.PageIndex >= Document.PageCount)
                throw new ArgumentOutOfRangeException($"Signature page doesn't exist, specified page was {Options.PageIndex + 1} but document has only {Document.PageCount} page(s).");

            var signatureSize = await Signer.GetSignatureSizeAsync().ConfigureAwait(false);
            _contentsPlaceholder = new(2 * signatureSize + 2);
            _byteRangePlaceholder = new(ByteRangePlaceholderLength);
            if (Format != PdfSignatureFormat.Pkcs7Detached)
                DeclareEsicExtension();

            if (Options.FieldName != null)
            {
                SignExistingField(GetSignatureDictionary(_contentsPlaceholder, _byteRangePlaceholder));
                return;
            }

            var signatureDictionary = GetSignatureDictionary(_contentsPlaceholder, _byteRangePlaceholder);
            var signatureField = GetSignatureField(signatureDictionary);

            var page = Document.Pages[Options.PageIndex];
            var annotations = page.Elements.GetArray(PdfPage.Keys.Annots);
            if (annotations == null)
                page.Elements.Add(PdfPage.Keys.Annots, new PdfArray(Document, signatureField));
            else
                annotations.Elements.Add(signatureField);

            var acroForm = Document.Catalog.GetOrCreateAcroForm();
            SetSignatureFlags(acroForm);
            acroForm.Fields.Elements.Add(signatureField);
        }

        /// <summary>
        /// ETSI EN 319 142-1 5.6: a document below PDF 2.0 that uses an ETSI /SubFilter should declare the ESIC developer
        /// extension (BaseVersion 1.7, ExtensionLevel 2) in the catalog's /Extensions (ISO 32000-1 7.12).
        /// </summary>
        void DeclareEsicExtension()
        {
            if (Document.Version >= 20)
                return;
            var existing = Resolve(Document.Catalog.Elements["/Extensions"]);
            // A malformed entry is left as it is rather than replaced.
            if (existing is not (null or PdfDictionary))
                return;
            var oldExtensions = (PdfDictionary?)existing;
            var oldEsic = Resolve(oldExtensions?.Elements["/ESIC"]);
            // One extension dictionary, or an array of them for one prefix (ISO 32000-2 7.12.3).
            var declared = oldEsic switch
            {
                null => [],
                PdfDictionary dictionary => [dictionary],
                PdfArray array => array.Elements.Select(Resolve).OfType<PdfDictionary>().ToList(),
                _ => (List<PdfDictionary>?)null,
            };
            if (declared == null || declared.Any(d => d.Elements.GetInteger("/ExtensionLevel") >= 2))
                return;

            var esic = new PdfDictionary(Document);
            esic.Elements.SetName("/BaseVersion", "/1.7");
            esic.Elements.SetInteger("/ExtensionLevel", 2);

            // The declaration is written as a new direct dictionary in the catalog, with the other entries carried over:
            // under a certification the catalog's /Extensions may change, the objects it refers to may not.
            var extensions = new PdfDictionary(Document);
            if (oldExtensions != null)
            {
                foreach (var key in oldExtensions.Elements.Keys)
                {
                    if (key != "/ESIC" && oldExtensions.Elements[key] is { } value)
                        extensions.Elements[key] = Copy(value);
                }
            }
            if (oldEsic is PdfArray oldArray)
            {
                var all = new PdfArray(Document);
                foreach (var item in oldArray.Elements)
                    all.Elements.Add(Copy(item));
                all.Elements.Add(esic);
                extensions.Elements["/ESIC"] = all;
            }
            else
            {
                extensions.Elements["/ESIC"] = esic;
            }
            Document.Catalog.Elements["/Extensions"] = extensions;

            static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

            // A direct dictionary or array belongs to one container, so it is copied; references, names and numbers are
            // shared.
            PdfItem Copy(PdfItem item)
            {
                switch (item)
                {
                    case PdfDictionary { Reference: null } dictionary:
                        var dictionaryCopy = new PdfDictionary(Document);
                        foreach (var key in dictionary.Elements.Keys)
                        {
                            if (dictionary.Elements[key] is { } value)
                                dictionaryCopy.Elements[key] = Copy(value);
                        }
                        return dictionaryCopy;
                    case PdfArray { Reference: null } array:
                        var arrayCopy = new PdfArray(Document);
                        foreach (var element in array.Elements)
                            arrayCopy.Elements.Add(Copy(element));
                        return arrayCopy;
                    default:
                        return item;
                }
            }
        }

        // SignaturesExist and AppendOnly (ISO 32000-1 Table 219).
        static void SetSignatureFlags(PdfForm acroForm)
        {
            if (!acroForm.Elements.ContainsKey(PdfForm.Keys.SigFlags))
                acroForm.Elements.Add(PdfForm.Keys.SigFlags, new PdfInteger(3, true));
            else
            {
                var sigFlagVersion = acroForm.Elements.GetInteger(PdfForm.Keys.SigFlags);
                if (sigFlagVersion < 3)
                    acroForm.Elements.SetIntegerFlag(PdfForm.Keys.SigFlags, 3);
            }
        }

        /// <summary>
        /// Signs the existing field named by <see cref="DigitalSignatureOptions.FieldName"/>: its value becomes the
        /// signature, its widgets show it, and its lock is recorded in the signature. The page and the form's field
        /// list stay as they are.
        /// </summary>
        void SignExistingField(PdfSignature signatureDictionary)
        {
            var field = FindUnsignedSignatureField();
            CheckField(field);
            if (field.Elements.GetDictionary(PdfFormSignatureField.Keys.SV) is { } seedValues && SignatureSeedValues.ReasonMustBeOmitted(seedValues))
                signatureDictionary.Elements.Remove(PdfSignature.Keys.Reason);

            field.Elements.SetObject(PdfFormField.Keys.V, signatureDictionary);
            var appearance = Options.AppearanceHandler ?? DefaultAppearance();
            foreach (var widget in field.GetWidgets().ToList())
                PdfFormSignatureField.RenderSignedAppearance(widget, appearance);

            if (field.Elements.GetDictionary(PdfFormSignatureField.Keys.Lock) is { } fieldLock)
                AddFieldLockReference(signatureDictionary, fieldLock);

            SetSignatureFlags(Document.Catalog.GetOrCreateAcroForm());
        }

        DefaultSignatureAppearanceHandler DefaultAppearance() => new()
        {
            Location = Options.Location,
            Reason = Options.Reason,
            Signer = Signer.CertificateName
        };

        PdfFormSignatureField FindUnsignedSignatureField()
        {
            var name = Options.FieldName!;
            var form = Document.Catalog.GetAcroForm();
            if (form == null || !form.Fields.TryGetField(name, out var field))
                throw new ArgumentException($"The document has no form field named '{name}'.", nameof(DigitalSignatureOptions.FieldName));
            if (field is not PdfFormSignatureField signatureField)
                throw new ArgumentException($"The form field '{name}' is not a signature field.", nameof(DigitalSignatureOptions.FieldName));
            if (signatureField.GetKids(onlyFullyQualifiedFields: true).Any())
            {
                throw new ArgumentException($"The form field '{name}' has fields below it; name the signature field to sign.",
                    nameof(DigitalSignatureOptions.FieldName));
            }
            if (signatureField.Elements.GetValue(PdfFormField.Keys.V) is not (null or PdfNull or PdfNullObject))
                throw new InvalidOperationException($"The signature field '{name}' is already signed.");
            return signatureField;
        }

        // The field's required seed values must be met, and its lock must say which fields it locks.
        void CheckField(PdfFormSignatureField field)
        {
            if (field.Elements.GetDictionary(PdfFormSignatureField.Keys.SV) is { } seedValues)
                SignatureSeedValues.Check(seedValues, Options, SubFilter);
            if (field.Elements.GetDictionary(PdfFormSignatureField.Keys.Lock) is { } fieldLock
                && fieldLock.Elements.GetName("/Action") is not ("/All" or "/Include" or "/Exclude"))
            {
                throw new InvalidOperationException($"The lock of the signature field '{Options.FieldName}' has no valid /Action, " +
                    "so the fields it locks are unknown.");
            }
        }

        /// <summary>
        /// Records the lock of the signed field in the signature as a FieldMDP signature reference (ISO 32000-1
        /// 12.8.2.4), as Acrobat and pyHanko do: validators read the locked fields from there.
        /// </summary>
        void AddFieldLockReference(PdfDictionary signatureDictionary, PdfDictionary fieldLock)
        {
            var action = fieldLock.Elements.GetName("/Action");
            var parameters = new PdfDictionary(Document);
            parameters.Elements.SetName("/Type", "/TransformParams");
            parameters.Elements.SetName("/Action", action);
            if (action != "/All")
            {
                var fields = new PdfArray(Document);
                var lockedFields = fieldLock.Elements.GetArray("/Fields");
                for (var index = 0; index < (lockedFields?.Elements.Count ?? 0); index++)
                    fields.Elements.Add(new PdfString(lockedFields!.Elements.GetString(index)));
                parameters.Elements["/Fields"] = fields;
            }
            // PDF 2.0 lets a lock carry a document permission; pyHanko and Acrobat repeat it here.
            if (fieldLock.Elements.ContainsKey("/P"))
                parameters.Elements.SetInteger("/P", fieldLock.Elements.GetInteger("/P"));
            parameters.Elements.SetName("/V", "/1.2");

            var reference = new PdfDictionary(Document);
            reference.Elements.SetName("/Type", "/SigRef");
            reference.Elements.SetName("/TransformMethod", "/FieldMDP");
            reference.Elements["/TransformParams"] = parameters;
            reference.Elements.SetObject("/Data", Document.Catalog);
            AddSignatureReference(signatureDictionary, reference);
        }

        /// <summary>
        /// Appends a signature reference: a certification signature that signs a locked field carries both DocMDP and
        /// FieldMDP references.
        /// </summary>
        void AddSignatureReference(PdfDictionary signatureDictionary, PdfDictionary reference)
        {
            var references = signatureDictionary.Elements.GetArray("/Reference");
            if (references == null)
                signatureDictionary.Elements["/Reference"] = new PdfArray(Document, reference);
            else
                references.Elements.Add(reference);
        }

        /// <summary>
        /// Makes the signature a certification signature (ISO 32000-1 12.8.2.2): a DocMDP signature reference with the
        /// permission level, and the catalog's /Perms /DocMDP pointing to the signature dictionary. Only the first
        /// signature of a document can certify it.
        /// </summary>
        void AddCertification(PdfSignature signatureDictionary, PdfCertificationLevel level)
        {
            CheckCertification(level);

            var parameters = new PdfDictionary(Document);
            parameters.Elements.SetName("/Type", "/TransformParams");
            parameters.Elements.SetInteger("/P", (int)level);
            parameters.Elements.SetName("/V", "/1.2");
            var reference = new PdfDictionary(Document);
            reference.Elements.SetName("/Type", "/SigRef");
            reference.Elements.SetName("/TransformMethod", "/DocMDP");
            reference.Elements["/TransformParams"] = parameters;
            AddSignatureReference(signatureDictionary, reference);

            var perms = Document.Catalog.Elements.GetDictionary(PdfCatalog.Keys.Perms);
            if (perms == null)
            {
                perms = new PdfDictionary(Document);
                Document.Catalog.Elements[PdfCatalog.Keys.Perms] = perms;
            }
            perms.Elements["/DocMDP"] = signatureDictionary.Reference;
        }

        // Runs when the handler is made, so mistakes surface early, and again when the signature is written.
        void CheckCertification(PdfCertificationLevel level)
        {
            if (level is < PdfCertificationLevel.NoChanges or > PdfCertificationLevel.FormFillingAndAnnotations)
                throw new ArgumentOutOfRangeException(nameof(DigitalSignatureOptions.Certification), "The certification level must be 1, 2 or 3.");
            var perms = Document.Catalog.Elements.GetDictionary(PdfCatalog.Keys.Perms);
            if (perms?.Elements.ContainsKey("/DocMDP") == true || HasSignedField())
                throw new InvalidOperationException("Only the first signature can certify a document; this document is already signed.");
        }

        /// <summary>
        /// Tells whether any signature field of the form has a signature. Walks the field dictionaries themselves:
        /// /FT and /V are inheritable (ISO 32000-1 Table 220), and only a dictionary value is a signature.
        /// </summary>
        bool HasSignedField()
        {
            var visited = new HashSet<PdfDictionary>();
            var pending = new Stack<(PdfDictionary Field, string Type, bool Signed)>();
            Push(Document.Catalog.Elements.GetDictionary(PdfCatalog.Keys.AcroForm)?.Elements.GetArray(PdfForm.Keys.Fields), "", false);
            while (pending.Count != 0)
            {
                var (field, inheritedType, inheritedSigned) = pending.Pop();
                if (!visited.Add(field))
                    continue;
                var type = field.Elements.ContainsKey(PdfFormField.Keys.FT) ? field.Elements.GetName(PdfFormField.Keys.FT) : inheritedType;
                var signed = field.Elements.ContainsKey(PdfFormField.Keys.V)
                    ? field.Elements.GetDictionary(PdfFormField.Keys.V) != null
                    : inheritedSigned;
                if (type == "/Sig" && signed)
                    return true;
                Push(field.Elements.GetArray(PdfFormField.Keys.Kids), type, signed);
            }
            return false;

            void Push(PdfArray? fields, string type, bool signed)
            {
                for (var index = 0; index < (fields?.Elements.Count ?? 0); index++)
                {
                    if (fields!.Elements.GetDictionary(index) is { } field)
                        pending.Push((field, type, signed));
                }
            }
        }

        PdfFormSignatureField GetSignatureField(PdfSignature signatureDic) // #US321 TODO Use appropriate classes.
        {
            var signatureField = new PdfFormSignatureField(Document);

            signatureField.Elements.Add(PdfFormField.Keys.V, signatureDic);

            // #AcroForms
            // Annotation keys.
            signatureField.Elements.Add(PdfFormField.Keys.FT, new PdfName(PdfFormFieldType.Signature));
            signatureField.Elements.Add(PdfFormField.Keys.T, new PdfString(GetUniqueSignatureFieldName()));
            // Annotation flags Print (4) and Locked (128); this was written as field flags (/Ff), which made the
            // appearance unprinted and gave the field meaningless flags.
            signatureField.Elements.Add(PdfAnnotation.Keys.F, new PdfInteger(132));
            // signatureField.Elements.Add(PdfFormField.Keys.DR, new PdfDictionary());  TODO COMPILE
            signatureField.Elements.Add(PdfAnnotation.Keys.Type, new PdfName("/Annot"));
            signatureField.Elements.Add(PdfAnnotation.Keys.Subtype, new PdfName("/Widget"));
            signatureField.Elements.Add(PdfAnnotation.Keys.P, Document.Pages[Options.PageIndex]);

            signatureField.Elements.Add(PdfAnnotation.Keys.Rect, new PdfRectangle(Options.Rectangle));

            // TODO COMPILE
            if (IsDocumentTimeStamp)
            {
                // A document time-stamp's field is invisible: its rectangle is empty and its appearance draws nothing.
                signatureField.CustomAppearanceHandler = new EmptyAppearanceHandler();
            }
            else
            {
                signatureField.CustomAppearanceHandler = Options.AppearanceHandler ?? new DefaultSignatureAppearanceHandler()
                {
                    Location = Options.Location,
                    Reason = Options.Reason,
                    Signer = Signer.CertificateName
                };
            }

            // Call RenderCustomAppearance(); here.
            signatureField.RenderAppearance();
            // Rendering the signature in PrepareForSave is too late and leads to inconsistent embedded fonts.

            //Document.Internals.AddObject(signatureField); AcroFields are already indirect.

            return signatureField;
        }

        string GetUniqueSignatureFieldName()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<PdfDictionary>();
            var pending = new Stack<PdfDictionary>();
            AddFields(Document.Catalog.Elements.GetDictionary("/AcroForm")?.Elements.GetArray("/Fields"));
            while (pending.Count != 0)
            {
                var field = pending.Pop();
                if (!visited.Add(field))
                    continue;
                names.Add(field.Elements.GetString("/T"));
                AddFields(field.Elements.GetArray("/Kids"));
            }

            for (var suffix = 1; ; suffix++)
            {
                var candidate = "Signature" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!names.Contains(candidate))
                    return candidate;
            }

            void AddFields(PdfArray? fields)
            {
                if (fields == null)
                    return;
                for (var index = 0; index < fields.Elements.Count; index++)
                {
                    var field = fields.Elements.GetDictionary(index);
                    if (field != null)
                        pending.Push(field);
                }
            }
        }

        PdfSignature GetSignatureDictionary(PdfPlaceholder contents, PdfPlaceholder byteRange) // #US321 TODO Use appropriate classes.
        {
            PdfSignature signatureDic = new(Document);

            if (IsDocumentTimeStamp)
            {
                // ISO 32000-2 Table 260: a document time-stamp dictionary has no signing time, reason or location; the
                // time is the token's.
                signatureDic.Elements.Add(PdfSignature.Keys.Type, new PdfName("/DocTimeStamp"));
                signatureDic.Elements.Add(PdfSignature.Keys.Filter, new PdfName("/Adobe.PPKLite"));
                signatureDic.Elements.Add(PdfSignature.Keys.SubFilter, new PdfName(SubFilter));
                signatureDic.Elements.Add(PdfSignature.Keys.Contents, contents);
                signatureDic.Elements.Add(PdfSignature.Keys.ByteRange, byteRange);
                Document.Internals.AddObject(signatureDic);
                return signatureDic;
            }

            signatureDic.Elements.Add(PdfSignature.Keys.Type, new PdfName(PdfFormFieldType.Signature));
            signatureDic.Elements.Add(PdfSignature.Keys.Filter, new PdfName("/Adobe.PPKLite"));
            signatureDic.Elements.Add(PdfSignature.Keys.SubFilter, new PdfName(SubFilter));
            signatureDic.Elements.Add(PdfSignature.Keys.M, new PdfDate(DateTimeOffset.Now));

            signatureDic.Elements.Add(PdfSignature.Keys.Contents, contents);
            signatureDic.Elements.Add(PdfSignature.Keys.ByteRange, byteRange);
            signatureDic.Elements.Add(PdfSignature.Keys.Reason, new PdfString(Options.Reason));
            signatureDic.Elements.Add(PdfSignature.Keys.Location, new PdfString(Options.Location));
            // FreePDF: ContactInfo was an option that was never written.
            if (!String.IsNullOrEmpty(Options.ContactInfo))
                signatureDic.Elements["/ContactInfo"] = new PdfString(Options.ContactInfo);

            var properties = new PdfDictionary(Document);
            signatureDic.Elements.Add("/Prop_Build", properties);
            var propertyItems = new PdfDictionary(Document);
            properties.Elements.Add("/App", propertyItems);
            var appName = String.IsNullOrWhiteSpace(Options.AppName) ? DigitalSignatureOptions.DefaultAppName : Options.AppName;
            propertyItems.Elements.Add("/Name", PdfName.FromString(appName));

            Document.Internals.AddObject(signatureDic);

            if (Options.Certification is { } level)
                AddCertification(signatureDic, level);

            return signatureDic;
        }

        PdfPlaceholder? _contentsPlaceholder;
        PdfPlaceholder? _byteRangePlaceholder;

        sealed class EmptyAppearanceHandler : IAnnotationAppearanceHandler
        {
            public void DrawAppearance(PdfSharp.Drawing.XGraphics gfx, PdfSharp.Drawing.XRect rect) { }
        }
    }
}
