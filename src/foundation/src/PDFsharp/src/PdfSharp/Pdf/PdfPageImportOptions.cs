// FreePDF - MIT licence. See the LICENSE file in the solution root.

namespace PdfSharp.Pdf
{
    /// <summary>
    /// Options for <see cref="PdfDocument.AppendPages(PdfDocument, int, int, PdfPageImportOptions)"/> and
    /// <see cref="PdfDocument.ExtractPages(int, int, PdfPageImportOptions)"/>.
    /// </summary>
    public sealed class PdfPageImportOptions
    {
        /// <summary>
        /// Gets or sets whether the bookmarks of the source that lead to the imported pages are added to the
        /// document's bookmarks. The default is true.
        /// </summary>
        public bool KeepBookmarks { get; set; } = true;

        /// <summary>
        /// Gets or sets the title of a bookmark that opens the first imported page and holds the imported bookmarks,
        /// for example the source's file name. With null, the default, the imported bookmarks are added at the top
        /// level.
        /// </summary>
        public string? BookmarkTitle { get; set; }
    }
}
