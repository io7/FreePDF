// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Pdf.Forms
{
    public partial class PdfFormRadioButtonField
    {
        /// <summary>
        /// Gets the export values of the buttons in widget order: the /Opt entries if the field has them, otherwise
        /// the on-state names of the buttons' appearances, without the leading slash.
        /// </summary>
        public IReadOnlyList<string> Options
        {
            get
            {
                var widgets = GetWidgets().ToList();
                var options = new List<string>(widgets.Count);
                var opt = GetElementsValue<PdfArray>(Keys.Opt);
                for (int index = 0; index < widgets.Count; index++)
                {
                    if (opt != null && index < opt.Elements.Count && opt.Elements[index] is PdfString text)
                        options.Add(text.Value);
                    else
                        options.Add((GetOnState(widgets[index]) ?? "").TrimStart('/'));
                }
                return options;
            }
        }

        /// <summary>
        /// Gets or sets the index of the selected button in widget order, or -1 if none is selected. Setting it writes
        /// the field value (/V) and switches the selected button on and the others off (/AS).
        /// </summary>
        public int SelectedIndex
        {
            get
            {
                var value = GetElementsValue<PdfName>(Keys.V)?.Value;
                if (value == null || value == "/Off")
                    return -1;
                var widgets = GetWidgets().ToList();
                for (int index = 0; index < widgets.Count; index++)
                {
                    if (GetOnState(widgets[index]) == value)
                        return index;
                }
                return -1;
            }
            set
            {
                ThrowIfReadOnly();
                var widgets = GetWidgets().ToList();
                if (value < -1 || value >= widgets.Count)
                    throw new ArgumentOutOfRangeException(nameof(value), $"The radio button group '{FullName}' has {widgets.Count} buttons.");
                var state = "/Off";
                for (int index = 0; index < widgets.Count; index++)
                {
                    var widgetState = "/Off";
                    if (index == value)
                        widgetState = GetOnState(widgets[index]) ?? "/" + index;
                    widgets[index].Elements.SetName(PdfSharp.Pdf.Annotations.PdfAnnotation.Keys.AS, widgetState);
                    if (index == value)
                        state = widgetState;
                }
                Elements.SetName(Keys.V, state);
            }
        }

        /// <summary>
        /// Gets or sets the export value of the selected button (see <see cref="Options"/>), or null if none is
        /// selected.
        /// </summary>
        public string? SelectedValue
        {
            get
            {
                var index = SelectedIndex;
                if (index < 0)
                    return null;
                return Options[index];
            }
            set
            {
                if (value == null)
                {
                    SelectedIndex = -1;
                    return;
                }
                var index = Options.ToList().IndexOf(value);
                if (index < 0)
                    throw new ArgumentException($"The radio button group '{FullName}' has no option '{value}'.", nameof(value));
                SelectedIndex = index;
            }
        }
    }
}
