// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PdfSharp.Pdf.PdfA;

// v7.0.0 Ready

namespace PdfSharp.Pdf.Metadata
{
    /// <summary>
    /// A collection of essential metadata from the PDF document.
    /// All data is formatted as string and can directly be used in the XMP metadata stream.
    /// </summary>
    public class DocumentMetadataInfo  // #Metadata
    {
        internal DocumentMetadataInfo(PdfDocument document)
        {
            var info = document.Info;

            // Files from other writers may hold a number, an array or a dangling reference where text belongs; such an
            // entry counts as missing here, as saving with generated metadata threw otherwise.
            Title = Text(() => info.Title);
            Author = Text(() => info.Author);
            Subject = Text(() => info.Subject);
            Keywords = Text(() => info.Keywords);
            Creator = Text(() => info.Creator);
            Producer = Text(() => info.Producer);

#if DEBUG && true_
            // Check from where Acrobat takes the text.
            Title = "(xmp) " + info.Title;
            Author = "(xmp) " + info.Author;
            Subject = "(xmp) " + info.Subject;
            Keywords = "(xmp) " + info.Keywords;
            Creator = "(xmp) " + info.Creator;
            Producer = "(xmp) " + info.Producer;
#endif
            CreationDate = MetadataManager.ToXmpDateString(Date(() => info.CreationDate));
            ModificationDate = MetadataManager.ToXmpDateString(Date(() => info.ModificationDate));
            DocumentID = document.Internals.FirstDocumentID;
            InstanceID = document.Internals.SecondDocumentID;
            var pdfaManager = PdfAManager.ForDocument(document);
            PdfAFormat = pdfaManager.IsPdfADocument ? pdfaManager.Format : null;
            IsPdfUA = document.UAManager != null;
        }

        static string Text(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException)
            {
                return "";
            }
        }

        static DateTimeOffset? Date(Func<DateTimeOffset?> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the document is tagged for PDF/UA-1 (it has a UAManager).
        /// Corresponds to the “pdfuaid:part” entry in the document’s metadata stream.
        /// </summary>
        public bool IsPdfUA { get; }

        // Reference 2.0: Table 349 — Entries in the document information dictionary / Page 716
        // The names of the properties comes from the document information dictionary.
        // See summary for the entries in the document’s metadata.

        /// <summary>
        /// Gets the document’s title.<br/>
        /// Corresponds to “dc:title” entry in the document’s metadata stream.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the name of the person who created the document.<br/>
        /// Corresponds to “dc:creator” entry in the document’s metadata stream.
        /// </summary>
        public string Author { get; }

        /// <summary>
        /// Gets the name of the subject of the document.<br/>
        /// Corresponds to “dc:description” entry in the document’s metadata stream.
        /// </summary>
        public string Subject { get; }

        /// <summary>
        /// Gets keywords associated with the document.<br/>
        /// Corresponds to “pdf:Keywords” entry in the document’s metadata stream.
        /// </summary>
        public string Keywords { get; }

        /// <summary>
        /// If the document was converted to PDF from another format, gets the name of the PDF processor
        /// that created the original document from which it was converted.<br/>
        /// Corresponds to “xmp:CreatorTool” entry in the document’s metadata stream.
        /// </summary>
        public string Creator { get; }

        /// <summary>
        /// If the document was converted to PDF from another format, gets the name of the PDF processor
        /// that converted it to PDF.<br/>
        /// Corresponds to “pdf:Producer” entry in the document’s metadata stream.
        /// </summary>
        public string Producer { get; }

        /// <summary>
        /// Gets the creation date of the document.
        /// Can be an empty string.<br/>
        /// Corresponds to “xmp:CreateDate” entry in the document’s metadata stream.
        /// </summary>
        public string CreationDate { get; }

        /// <summary>
        /// Gets the modification date of the document.
        /// Can be an empty string.<br/>
        /// Corresponds to “xmp:ModifyDate” entry in the document’s metadata stream.
        /// </summary>
        public string ModificationDate { get; }

        // Information from trailer.

        /// <summary>
        /// Gets the document ID of the document.<br/>
        /// Corresponds to “xapMM:DocumentID” entry in the document’s metadata stream.
        /// </summary>
        public string DocumentID { get; }

        /// <summary>
        /// Gets the instance ID of the document.<br/>
        /// Corresponds to “xmpMM:InstanceID” entry in the document’s metadata stream.
        /// </summary>
        public string InstanceID { get; }

        /// <summary>
        /// Gets the PDF/A part and conformance level, or null,
        /// if the document is not a PDF/A document.
        /// </summary>
        public PdfAFormat? PdfAFormat { get; }
    }
}
