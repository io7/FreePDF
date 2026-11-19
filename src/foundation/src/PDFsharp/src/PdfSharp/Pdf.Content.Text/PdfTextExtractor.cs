// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Text;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Extracts the text that a page's content draws (ISO 32000-1 9.10). The text of annotations and form fields is
    /// not included: flatten the form first to extract it. See docs/text-extraction.md for what is decoded.
    /// </summary>
    public static class PdfTextExtractor
    {
        /// <summary>
        /// Gets the text runs of the page in the order the content draws them.
        /// </summary>
        public static IReadOnlyList<PdfTextRun> GetTextRuns(PdfPage page, PdfTextExtractionOptions? options = null)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            var reader = new TextContentReader(Checked(options), collectLines: false);
            reader.Read(page);
            return reader.Runs;
        }

        /// <summary>
        /// Gets the page's text: lines separated by "\n", the runs of a line separated by a space where there is a gap.
        /// </summary>
        public static string GetText(PdfPage page, PdfTextOrder order = PdfTextOrder.Reading, PdfTextExtractionOptions? options = null)
            => TextLayout.Compose(GetTextRuns(page, options), order);

        /// <summary>
        /// Gets the text of every page, each page followed by a form feed ("\f"), as pdftotext writes it.
        /// </summary>
        public static string GetText(PdfDocument document, PdfTextOrder order = PdfTextOrder.Reading, PdfTextExtractionOptions? options = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            var text = new StringBuilder();
            foreach (var page in document.Pages)
                text.Append(GetText(page, order, options)).Append('\f');
            return text.ToString();
        }

        internal static PdfTextExtractionOptions Checked(PdfTextExtractionOptions? options)
        {
            options ??= new PdfTextExtractionOptions();
            if (options.MaxDecodedBytes < 1 || options.MaxResults < 1 || options.MaxFormDepth < 1 || options.MaxFormDepth > 256)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxDecodedBytes and MaxResults must be positive; MaxFormDepth must be 1 to 256.");
            return options;
        }
    }
}
