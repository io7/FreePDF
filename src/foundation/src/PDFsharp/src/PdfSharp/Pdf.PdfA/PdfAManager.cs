// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

// v7.0.0 review

using PdfSharp.Pdf.Internal;

namespace PdfSharp.Pdf.PdfA
{
    /// <summary>
    /// The PdfAManager bundles PDF/A specific functionality of a PDF document.
    /// </summary>
    public class PdfAManager : ManagerBase
    {
        // TODOs:
        // Handle PDF/A for read documents.
        // What else is missing?

        /// <summary>
        /// Initialized a new instance of this class for the specified document
        /// </summary>
        /// <param name="document"></param>
        PdfAManager(PdfDocument document) : base(document)
        {
            Initialize();
        }

        /// <summary>
        /// Sets PDF/A part and conformance level for the document.
        /// After once set it cannot be changed.
        /// </summary>
        public void SetFormat(PdfAFormat format)
        {
            if (_formatSet)
            {
                throw new InvalidOperationException(
                    "PDF/A format of this document is already set and cannot be changed anymore.");
            }

            if (!Document.IsImported)
            {
                // PDF document is newly created.
                if (Document.PageCount > 0)
                {
                    throw new InvalidOperationException(
                        "For a newly created document PDF/A settings must be done before any PDF content is created " +
                        "to ensure that all pages are PDF/A compatible.");
                }
            }
            // PDF/a requires document metadata.
            Document.GetMetadataManager().AdjustStrategyForPdfA();

            Part = format.Part;
            Level = format.ConformanceLevel;
            _formatSet = true;
        }
        bool _formatSet;

        /// <summary>
        /// Return true if the PDF document is a PDF/A document, false otherwise.
        /// </summary>
        public bool IsPdfADocument => Part != 0;

        /// <summary>
        /// Gets the part number of a PDF/A document, or 0, if the document is
        /// not a PDF/A document.
        /// </summary>
        public int Part { get; private set; }

        /// <summary>
        /// Gets the conformance level of a PDF/A document, or ' ' (blank), if the document is
        /// not a PDF/A document.
        /// </summary>
        public char Level { get; private set; } = ' ';

        /// <summary>
        /// Gets the current PdfAFormat of a PDF/A document.
        /// </summary>
        public PdfAFormat Format => new(Part, Level);

        /// <summary>
        /// Prepares the pages of a PDF/A document of the given part (0: none) for a full save. PDF/A requires every
        /// annotation to print (ISO 19005-1 6.5.3, ISO 19005-2 6.3.2); annotations of an opened file often lack the flag
        /// (upstream #356). An annotation that is hidden cannot conform and stays hidden. PDF/A-1 forbids page
        /// transparency groups (ISO 19005-1 6.4), which pages of an opened file may have. An incremental update leaves
        /// existing pages alone.
        /// </summary>
        internal static void PrepareForSave(PdfDocument document, int part)
        {
            if (part == 0 || document.IncrementalUpdate != null)
                return;
            const int invisible = 1, hidden = 2, print = 4, noView = 32;
            foreach (var page in document.Pages)
            {
                if (part == 1 && page.Elements.GetDictionary(PdfPage.Keys.Group) is { } group
                    && group.Elements.GetName("/S") == "/Transparency")
                {
                    page.Elements.Remove(PdfPage.Keys.Group);
                }
                if (page.Elements.GetArray(PdfPage.Keys.Annots) is not { } annotations)
                    continue;
                for (int index = 0; index < annotations.Elements.Count; index++)
                {
                    if (annotations.Elements.GetDictionary(index) is not { } annotation)
                        continue;
                    int flags = annotation.Elements.GetInteger("/F");
                    if ((flags & (invisible | hidden | noView)) == 0 && (flags & print) == 0)
                        annotation.Elements.SetInteger("/F", flags | print);
                }
            }
        }

        /// <summary>
        /// Gets or creates the PdfAManager for the specified document.
        /// </summary>
        public static PdfAManager ForDocument(PdfDocument document)
            => document.PdfAManager ??= new(document);

        void Initialize()
        {
            Document.EnsureNotDisposed();
            if (!_initialized)
            {
                _initialized = true;
                Document.EnsureNotYetSaved();
                if (Document.IsImported)
                {
                    // TODO: Get PDF/A conformance
                }
            }
        }
        bool _initialized;
    }
}
