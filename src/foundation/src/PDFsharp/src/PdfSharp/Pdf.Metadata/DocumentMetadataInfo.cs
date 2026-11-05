// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PdfSharp.Pdf.Advanced;
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
            Title = Text(info, PdfDocumentInformation.Keys.Title, () => info.Title);
            Author = Text(info, PdfDocumentInformation.Keys.Author, () => info.Author);
            Subject = Text(info, PdfDocumentInformation.Keys.Subject, () => info.Subject);
            Keywords = Text(info, PdfDocumentInformation.Keys.Keywords, () => info.Keywords);
            Creator = Text(info, PdfDocumentInformation.Keys.Creator, () => info.Creator);
            Producer = Text(info, PdfDocumentInformation.Keys.Producer, () => info.Producer);

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

        static string Text(PdfDocumentInformation info, string key, Func<string> read)
        {
            string value;
            try
            {
                value = read();
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException)
            {
                return "";
            }
            // A text string without a byte order mark is in PDFDocEncoding (ISO 32000-1 7.9.2.2). PDFsharp keeps such a
            // string byte for byte, and writes a string set in memory with only characters below 256 the same way, so
            // readers and validators decode it as PDFDocEncoding; XMP must hold what they read, or curly quotes and
            // dashes became control characters.
            var item = info.Elements[key];
            if (item is PdfReference reference)
                item = reference.Value;
            var encoding = item switch
            {
                PdfString text => text.Encoding,
                PdfStringObject text => text.Encoding,
                _ => PdfStringEncoding.Unicode
            };
            return encoding == PdfStringEncoding.RawEncoding ? DecodePdfDocEncoding(value) : value;
        }

        // The characters where PDFDocEncoding differs from Latin-1 (ISO 32000-1 Annex D): 0x18-0x1F, 0x80-0x9E and 0xA0.
        static string DecodePdfDocEncoding(string bytes)
        {
            char[]? result = null;
            for (int index = 0; index < bytes.Length; index++)
            {
                char ch = bytes[index];
                char mapped = ch switch
                {
                    >= '\u0018' and <= '\u001F' => DocEncodingLow[ch - 0x18],
                    >= '\u0080' and <= '\u009E' => DocEncodingHigh[ch - 0x80],
                    '\u00A0' => '\u20AC',
                    _ => ch
                };
                if (mapped != ch)
                    (result ??= bytes.ToCharArray())[index] = mapped;
            }
            return result == null ? bytes : new string(result);
        }

        static readonly char[] DocEncodingLow = ['\u02D8', '\u02C7', '\u02C6', '\u02D9', '\u02DD', '\u02DB', '\u02DA', '\u02DC'];

        static readonly char[] DocEncodingHigh =
        [
            '\u2022', '\u2020', '\u2021', '\u2026', '\u2014', '\u2013', '\u0192', '\u2044',
            '\u2039', '\u203A', '\u2212', '\u2030', '\u201E', '\u201C', '\u201D', '\u2018',
            '\u2019', '\u201A', '\u2122', '\uFB01', '\uFB02', '\u0141', '\u0152', '\u0160',
            '\u0178', '\u017D', '\u0131', '\u0142', '\u0153', '\u0161', '\u017E'
        ];

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
