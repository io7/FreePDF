// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Limits for reading one page. Reaching one throws a <see cref="PdfSharp.Pdf.IO.PdfReaderException"/>.
    /// </summary>
    public sealed class PdfTextExtractionOptions
    {
        /// <summary>
        /// The bytes that decoding the page's content, its forms and its CMaps may produce. Default 64 MiB.
        /// </summary>
        public long MaxDecodedBytes { get; set; } = 64L * 1024 * 1024;

        /// <summary>
        /// How deep Form XObjects may be nested, 1 to 256. Default 32. A form that draws itself is skipped.
        /// </summary>
        public int MaxFormDepth { get; set; } = 32;

        /// <summary>
        /// The text runs, or line segments, one page may produce. Default 1,000,000.
        /// </summary>
        public int MaxResults { get; set; } = 1_000_000;
    }
}
