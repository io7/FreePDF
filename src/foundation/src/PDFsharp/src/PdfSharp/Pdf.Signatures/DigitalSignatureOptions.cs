// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PdfSharp.Drawing;
using PdfSharp.Pdf.Annotations;

// v7.0.0 Ready

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Sets the options of the DigitalSignatureHandler class.
    /// </summary>
    public class DigitalSignatureOptions()
    {
        /// <summary>
        /// Gets or sets the appearance handler that draws the visual representation of the signature in the PDF.
        /// </summary>
        public IAnnotationAppearanceHandler? AppearanceHandler { get; init; }

        /// <summary>
        /// Gets or sets a string associated with the signature.
        /// </summary>
        public string ContactInfo { get; init; } = "";

        /// <summary>
        /// Gets or sets a string associated with the signature.
        /// </summary>
        public string Location { get; init; } = "";

        /// <summary>
        /// Gets or sets a string associated with the signature.
        /// </summary>
        public string Reason { get; init; } = "";

        /// <summary>
        /// Gets or sets the name of the application used to sign the document.
        /// </summary>
        public string AppName { get; init; } = DefaultAppName;

        internal const string DefaultAppName = $"{PdfSharpProductVersionInformation.Title} {PdfSharpProductVersionInformation.Url}";

        /// <summary>
        /// The location of the visual representation on the selected page.
        /// </summary>
        public XRect Rectangle { get; init; }

        /// <summary>
        /// The page zero-based index of the page showing the signature.
        /// </summary>
        public int PageIndex { get; init; }

        /// <summary>
        /// Gets or sets the fully qualified name of an existing, unsigned signature field to sign, such as "Approval"
        /// or "approvals.manager". The signature is shown in that field's widget, so <see cref="Rectangle"/> and
        /// <see cref="PageIndex"/> must be left unset. The field keeps its name and flags. A field lock (/Lock) is
        /// recorded in the signature, so validators lock its fields, and the seed values (/SV) the field requires
        /// must be met. When null, a new signature field is added.
        /// </summary>
        public string? FieldName { get; init; }

        /// <summary>
        /// Gets or sets whether this signature certifies the document, and which later changes keep the certification
        /// valid (a certification or author signature with DocMDP, ISO 32000-1 12.8.2.2). Only the first signature of a
        /// document can certify it. When null, the default, the signature is an ordinary approval signature.
        /// </summary>
        public PdfCertificationLevel? Certification { get; init; }
    }

    /// <summary>
    /// The changes a certified document permits after its certification signature (the DocMDP permission /P).
    /// Signing empty signature fields is always permitted.
    /// </summary>
    public enum PdfCertificationLevel
    {
        /// <summary>
        /// No changes: any change invalidates the certification (P = 1).
        /// </summary>
        NoChanges = 1,

        /// <summary>
        /// Filling in forms and signing (P = 2).
        /// </summary>
        FormFilling = 2,

        /// <summary>
        /// Filling in forms, signing, and adding, changing or deleting comments (P = 3).
        /// </summary>
        FormFillingAndAnnotations = 3,
    }
}
