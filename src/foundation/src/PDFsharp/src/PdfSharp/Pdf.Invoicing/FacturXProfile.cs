// FreePDF - MIT licence. See the LICENSE file in the solution root.

namespace PdfSharp.Pdf.Invoicing
{
    /// <summary>
    /// The Factur-X / ZUGFeRD profile of the embedded invoice XML. It sets the XMP conformance level, the default
    /// associated-file relationship and, for XRechnung, the embedded file name.
    /// </summary>
    public enum FacturXProfile
    {
        /// <summary>MINIMUM: invoice header data only. Not a valid invoice under the German B2B rules.</summary>
        Minimum,

        /// <summary>BASIC WL: header data without lines. Not a valid invoice under the German B2B rules.</summary>
        BasicWL,

        /// <summary>BASIC: a subset of EN 16931 with invoice lines.</summary>
        Basic,

        /// <summary>EN 16931 (COMFORT): the European semantic data model for electronic invoices.</summary>
        EN16931,

        /// <summary>EXTENDED: EN 16931 plus additional data.</summary>
        Extended,

        /// <summary>XRECHNUNG: the German CIUS of EN 16931, embedded as xrechnung.xml.</summary>
        XRechnung,
    }
}
