// FreePDF - MIT licence. See the LICENSE file in the solution root.

namespace PdfSharp.Pdf
{
    /// <summary>
    /// What <see cref="PdfDocument.AppendPages(PdfDocument, int, int, PdfPageImportOptions)"/> added.
    /// </summary>
    public sealed class PdfPageImportResult
    {
        internal PdfPageImportResult(IReadOnlyList<PdfPage> pages, IReadOnlyDictionary<string, string> renamedFields)
        {
            Pages = pages;
            RenamedFields = renamedFields;
        }

        /// <summary>
        /// Gets the appended pages, in order.
        /// </summary>
        public IReadOnlyList<PdfPage> Pages { get; }

        /// <summary>
        /// Gets the form fields that were renamed because the document already had a field of that name: the name in
        /// the source maps to the name in the document, for example "name" to "name_2". Only top-level names are
        /// listed; the fields below a renamed field keep their own names.
        /// </summary>
        public IReadOnlyDictionary<string, string> RenamedFields { get; }
    }
}
