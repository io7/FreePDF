// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Pdf.Annotations
{
    /// <summary>
    /// Represents an annotation whose subtype FreePDF does not model: a private or misspelled subtype, or none.
    /// ISO 32000 asks readers to tolerate annotation types they do not recognize, so the annotation is kept with
    /// its entries as read, and saving writes them back unchanged.
    /// </summary>
    public sealed class PdfUnknownAnnotation : PdfAnnotation
    {
        /// <summary>
        /// Initializes a new instance of this class using the elements of the specified dictionary.
        /// After this type transformation the specified dictionary is dead and cannot be used anymore.
        /// </summary>
        internal PdfUnknownAnnotation(PdfDictionary dict)
            : base(dict)
        { }

        /// <summary>
        /// Gets the subtype name as read, for example '/SomePrivateType', or an empty string if there is none or it is
        /// not a name.
        /// </summary>
        public string Subtype
        {
            get
            {
                if (Elements.TryGetName(Keys.Subtype, out var subtype))
                    return subtype;
                return "";
            }
        }
    }
}
