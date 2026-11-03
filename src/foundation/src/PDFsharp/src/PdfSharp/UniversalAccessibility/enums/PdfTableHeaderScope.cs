// FreePDF - MIT licence. See the LICENSE file in the solution root.

namespace PdfSharp.UniversalAccessibility
{
    /// <summary>
    /// The cells a table header cell (TH) applies to (ISO 32000-1 Table 349, Scope).
    /// </summary>
    public enum PdfTableHeaderScope
    {
        /// <summary>The header applies to the rest of the cells in its row.</summary>
        Row,

        /// <summary>The header applies to the rest of the cells in its column.</summary>
        Column,

        /// <summary>The header applies to both its row and its column.</summary>
        Both,
    }
}
