// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Pdf.Forms
{
    public partial class PdfFormTextField
    {
        /// <summary>
        /// Gets or sets the text of the field (its /V value). Setting it updates the value and the field's
        /// appearance in every widget.
        /// </summary>
        public string Text
        {
            get => GetElementsValue<PdfString>(Keys.V)?.Value ?? "";
            set
            {
                if (value == null!)
                    throw new ArgumentNullException(nameof(value));
                ThrowIfReadOnly();
                // A password field shows one asterisk per character.
                var shown = value;
                if (TestFlag(PdfFormFieldFlags.Password))
                    shown = new string('*', value.Length);
                SetValueWithAppearance(new FieldContent { Text = shown }, () => Elements[Keys.V] = new PdfString(value));
            }
        }
    }
}
