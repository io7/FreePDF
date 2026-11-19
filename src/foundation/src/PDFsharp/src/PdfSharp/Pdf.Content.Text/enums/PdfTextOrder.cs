// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// The order in which extracted text is joined.
    /// </summary>
    public enum PdfTextOrder
    {
        /// <summary>
        /// Lines from the top of the page down, and the runs of a line from left to right. Assumes horizontal text.
        /// </summary>
        Reading,

        /// <summary>
        /// The order in which the content stream draws the text. A new line starts where the baseline moves.
        /// </summary>
        Content,
    }
}
