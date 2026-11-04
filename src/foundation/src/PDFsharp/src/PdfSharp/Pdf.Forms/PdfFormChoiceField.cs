// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Pdf.Forms
{
    public abstract partial class PdfFormChoiceField
    {
        /// <summary>
        /// Gets the options shown to the user, in /Opt order.
        /// </summary>
        public IReadOnlyList<string> Options => ReadOptions().Select(option => option.Display).ToList();

        /// <summary>
        /// Gets the export values of the options, in /Opt order. An option without a separate export value exports
        /// its display text.
        /// </summary>
        public IReadOnlyList<string> ExportValues => ReadOptions().Select(option => option.Export).ToList();

        /// <summary>
        /// Gets or sets the index of the selected option, or -1 if none is selected; of several selected options in a
        /// list box that allows multiple selection, the first. Setting it selects that option alone: it writes the
        /// export value (/V), and the selected index (/I) when the field allows multiple selection, when another
        /// option has the same export value, or when the field already has one (ISO 32000-1 Table 231).
        /// </summary>
        public int SelectedIndex
        {
            get
            {
                var selected = SelectedIndices(ReadOptions());
                return selected.Count > 0 ? selected[0] : -1;
            }
            set
            {
                ThrowIfReadOnly();
                var options = ReadOptions();
                if (value < -1 || value >= options.Count)
                    throw new ArgumentOutOfRangeException(nameof(value), $"The choice field '{FullName}' has {options.Count} options.");
                if (value == -1)
                {
                    Elements.Remove(Keys.V);
                    Elements.Remove("/I");
                    return;
                }
                SetValueWithAppearance(Content(options, [value], options[value].Display), () =>
                {
                    Elements[Keys.V] = new PdfString(options[value].Export);
                    if (NeedsSelectedIndices(options, value))
                        Elements["/I"] = new PdfArray(Owner, new PdfInteger(value));
                    else
                        Elements.Remove("/I");
                });
            }
        }

        // ISO 32000-1 Table 231: /I is for multiple selection and for options that share an export value, and
        // "should not be used" otherwise; validators treat an added /I on a signed form as an unexplained change.
        bool NeedsSelectedIndices(List<(string Export, string Display)> options, int index)
            => TestFlag(PdfFormFieldFlags.MultiSelect)
               || GetElementsValue<PdfArray>("/I") != null
               || options.Count(option => option.Export == options[index].Export) > 1;

        /// <summary>
        /// Gets or sets the export value of the selected option, or null if none is selected; of several selected
        /// options in a list box that allows multiple selection, the first. Setting a value selects that option alone;
        /// a value that is not an option is allowed only for an editable combo box.
        /// </summary>
        public string? SelectedValue
        {
            get
            {
                var values = SelectedValues();
                return values.Count > 0 ? values[0] : null;
            }
            set
            {
                if (value == null)
                {
                    SelectedIndex = -1;
                    return;
                }
                // Of options that share the export value, the one already selected stays selected.
                var exports = ExportValues.ToList();
                var index = SelectedIndex;
                if (index < 0 || exports[index] != value)
                    index = exports.IndexOf(value);
                if (index >= 0)
                {
                    SelectedIndex = index;
                    return;
                }
                if (!TestFlag(PdfFormFieldFlags.Edit))
                    throw new ArgumentException($"The choice field '{FullName}' has no option '{value}'.", nameof(value));
                ThrowIfReadOnly();
                SetValueWithAppearance(Content(ReadOptions(), [], value), () =>
                {
                    Elements[Keys.V] = new PdfString(value);
                    Elements.Remove("/I");
                });
            }
        }

        internal override FieldContent? CurrentContent()
        {
            var options = ReadOptions();
            var selected = SelectedIndices(options);
            if (selected.Count > 0)
                return Content(options, selected, options[selected[0]].Display);
            return Content(options, [], SelectedValue ?? "");
        }

        /// <summary>
        /// What the field shows: a list box shows its options with the selected ones, a combo box the selected text.
        /// </summary>
        FieldContent Content(List<(string Export, string Display)> options, IReadOnlyList<int> selected, string shownText)
        {
            if (this is PdfFormListBoxField)
                return new FieldContent { Options = options.Select(option => option.Display).ToList(), SelectedIndices = selected };
            return new FieldContent { Text = shownText };
        }

        /// <summary>
        /// The export values in /V: one text string, or an array of them when several options of a list box are
        /// selected (ISO 32000-1 Table 231).
        /// </summary>
        List<string> SelectedValues()
        {
            var values = new List<string>();
            var value = GetElementsValue<PdfItem>(Keys.V);
            if (value is PdfSharp.Pdf.Advanced.PdfReference reference)
                value = reference.Value;
            if (value is PdfString text)
                values.Add(text.Value);
            else if (value is PdfArray array)
            {
                foreach (var item in array.Elements)
                {
                    var entry = item;
                    PdfSharp.Pdf.Advanced.PdfReference.Dereference(ref entry);
                    if (entry is PdfString element)
                        values.Add(element.Value);
                }
            }
            return values;
        }

        /// <summary>
        /// The indices of the selected options, in ascending order. /I tells apart options that share an export
        /// value, so its indices count where they agree with /V; values it does not cover are looked up by export value.
        /// </summary>
        List<int> SelectedIndices(List<(string Export, string Display)> options)
        {
            var values = SelectedValues();
            var selected = new List<int>();
            if (values.Count == 0)
                return selected;
            if (GetElementsValue<PdfArray>("/I") is { } indices)
            {
                for (var position = 0; position < indices.Elements.Count; position++)
                {
                    if (indices.Elements.TryGetInteger(position, out var index) && index >= 0 && index < options.Count
                        && values.Contains(options[index].Export) && !selected.Contains(index))
                    {
                        selected.Add(index);
                    }
                }
            }
            foreach (var value in values)
            {
                if (selected.Any(index => options[index].Export == value))
                    continue;
                var index = options.FindIndex(option => option.Export == value);
                if (index >= 0)
                    selected.Add(index);
            }
            selected.Sort();
            return selected;
        }

        List<(string Export, string Display)> ReadOptions()
        {
            var options = new List<(string, string)>();
            var opt = GetElementsValue<PdfArray>("/Opt");
            if (opt == null)
                return options;
            foreach (var item in opt.Elements)
            {
                var entry = item;
                PdfSharp.Pdf.Advanced.PdfReference.Dereference(ref entry);
                if (entry is PdfString text)
                    options.Add((text.Value, text.Value));
                else if (entry is PdfArray { Elements.Count: 2 } pair && pair.Elements[0] is PdfString export && pair.Elements[1] is PdfString display)
                    options.Add((export.Value, display.Value));
            }
            return options;
        }
    }
}
