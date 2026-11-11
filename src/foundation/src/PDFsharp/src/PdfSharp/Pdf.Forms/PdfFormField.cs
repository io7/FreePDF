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
        /// What a text or choice field shows for its current value, to draw its appearance; null for other fields.
        /// </summary>
        internal virtual FieldContent? CurrentContent() => null;

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
        /// Writes a new value through <paramref name="setValue"/> and rebuilds the appearance of every widget. If the
        /// appearance cannot be built (see <see cref="FieldAppearance.CanBuild"/>), viewers are asked to build it
        /// (NeedAppearances); a PDF/A document, which does not allow that, refuses the value instead.
        /// </summary>
        internal void SetValueWithAppearance(FieldContent content, Action setValue)
        {
            var field = (PdfFormTextFieldBase)this;
            bool canBuild = FieldAppearance.CanBuild(field, content);
            if (!canBuild && PdfSharp.Pdf.PdfA.PdfAManager.ForDocument(Owner).IsPdfADocument)
            {
                throw new InvalidOperationException(
                    $"The value of form field '{ShortFullName()}' cannot be shown in this PDF/A document: its default appearance " +
                    "needs a font in the form's resources that can encode the text, and PDF/A does not allow NeedAppearances.");
            }
            setValue();
            if (canBuild)
                FieldAppearance.TryUpdate(field, content);
            else
                Owner.Catalog.GetOrCreateAcroForm().NeedAppearances = true;
        }

        /// <summary>
        /// Throws if the field is read only.
        /// </summary>
        internal void ThrowIfReadOnly()
        {
            if (IsReadOnly)
                throw new InvalidOperationException($"The form field '{ShortFullName()}' is read only.");
        }
    }
}
