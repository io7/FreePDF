// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Pdf.Forms
{
    public partial class PdfFormCheckBoxField
    {
        /// <summary>
        /// Gets or sets whether the check box is checked. Setting it writes the field value (/V) and switches every
        /// widget to its on or off appearance (/AS), using the on-state name the widget defines.
        /// </summary>
        public bool Checked
        {
            get
            {
                var value = GetElementsValue<PdfName>(Keys.V);
                return value != null && value.Value != "/Off";
            }
            set
            {
                ThrowIfReadOnly();
                var state = "/Off";
                foreach (var widget in GetWidgets())
                {
                    var widgetState = "/Off";
                    if (value)
                        widgetState = GetOnState(widget) ?? "/Yes";
                    widget.Elements.SetName(PdfSharp.Pdf.Annotations.PdfAnnotation.Keys.AS, widgetState);
                    if (widgetState != "/Off")
                        state = widgetState;
                }
                if (value && state == "/Off")
                    state = "/Yes";
                Elements.SetName(Keys.V, state);
            }
        }
    }
}
