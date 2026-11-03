// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Pdf.Forms
{
    // Form filling: helpers shared by the typed value members of the field classes.
    public abstract partial class PdfFormField
    {
        /// <summary>
        /// Gets the widget annotations that show this field: the field itself when field and widget share one
        /// dictionary, and its kids that are widgets only (kids without a name of their own).
        /// </summary>
        internal IEnumerable<PdfDictionary> GetWidgets()
        {
            if (Elements.GetName(PdfSharp.Pdf.Annotations.PdfAnnotation.Keys.Subtype) == "/Widget")
                yield return this;
            foreach (var kid in GetKids())
            {
                if (!kid.IsFullyQualifiedField() && kid.Elements.GetName(PdfSharp.Pdf.Annotations.PdfAnnotation.Keys.Subtype) == "/Widget")
                    yield return kid;
            }
        }

        /// <summary>
        /// Gets the name of a check box or radio button widget's on state: the key of its normal appearance other
        /// than /Off (ISO 32000-1 12.7.4.2.3). Producers choose it freely, so it is not assumed to be /Yes.
        /// </summary>
        internal static string? GetOnState(PdfDictionary widget)
        {
            var normal = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
            if (normal == null)
                return null;
            return normal.Elements.Keys.FirstOrDefault(key => key != "/Off");
        }

        /// <summary>
        /// Throws if the field is read only.
        /// </summary>
        internal void ThrowIfReadOnly()
        {
            if (IsReadOnly)
                throw new InvalidOperationException($"The form field '{FullName}' is read only.");
        }
    }
}
