// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Extracts the straight segments of the paths a page paints (m, l, re, h), for example table rules.
    /// Curves are left out; their end point only moves the current point.
    /// </summary>
    public static class PdfLineExtractor
    {
        /// <summary>
        /// Gets the painted straight segments of the page, in painting order, in the page's default user space.
        /// </summary>
        public static IReadOnlyList<PdfLineSegment> GetLineSegments(PdfPage page, PdfTextExtractionOptions? options = null)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            var reader = new TextContentReader(PdfTextExtractor.Checked(options), collectLines: true);
            reader.Read(page);
            return reader.Lines;
        }
    }
}
