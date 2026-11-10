// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Metadata;
using PdfSharp.Pdf.PdfA;

namespace PdfSharp.Pdf.Forms
{
    /// <summary>
    /// The document's interactive form (AcroForm): its fields, and the Add… methods that create new ones. Each Add…
    /// method makes a field with its widget on the page, registers it in the form and on the page, and builds its
    /// appearance, so the field shows in every viewer and survives flattening and merging.
    /// </summary>
    public sealed partial class PdfForm
    {
        const string DefaultFontName = "/Helv";

        /// <summary>
        /// Adds a text field. The rectangle is in points from the top left corner of the visible page (its crop box, or
        /// its media box if it has none), with y growing downwards as for <see cref="XGraphics"/>. Set flags such as
        /// Multiline or Password on the returned field, and its text with <see cref="PdfFormTextField.Text"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The document is PDF/A (the field's Helvetica is not embedded) or
        /// PDF/UA (fields are not tagged yet).</exception>
        public PdfFormTextField AddTextField(string name, PdfPage page, XRect rectangle, PdfFormFieldStyle? style = null)
        {
            Check(name, page, style, rectangle);
            RefuseTextFieldsInPdfA(name);
            var field = new PdfFormTextField(Owner);
            Place(field, name, page, rectangle, style);
            SetDefaultAppearance(field, style);
            BuildTextAppearance(field);
            return field;
        }

        /// <summary>
        /// Adds a check box, unchecked. Its on state is /Yes; set <see cref="PdfFormCheckBoxField.Checked"/> to check it.
        /// </summary>
        public PdfFormCheckBoxField AddCheckBox(string name, PdfPage page, XRect rectangle, PdfFormFieldStyle? style = null)
        {
            Check(name, page, style, rectangle);
            var field = new PdfFormCheckBoxField(Owner);
            Place(field, name, page, rectangle, style);
            field.Elements.SetName(PdfFormField.Keys.V, "/Off");
            ButtonAppearance.Build(field, Owner, ButtonAppearance.Kind.CheckBox, "/Yes", ColorOperands(style?.TextColor ?? XColors.Black));
            return field;
        }

        /// <summary>
        /// Adds a group of radio buttons on one page, none selected. Each button's value is its export value and the
        /// name of its on state; select one with <see cref="PdfFormRadioButtonField.SelectedValue"/>. "Off" is the
        /// name of the off state and cannot be a value, nor can a value start with a slash.
        /// </summary>
        public PdfFormRadioButtonField AddRadioButtonGroup(string name, PdfPage page,
            IReadOnlyList<(string Value, XRect Rectangle)> buttons, PdfFormFieldStyle? style = null)
        {
            if (buttons == null || buttons.Count == 0)
                throw new ArgumentException("A radio button group needs at least one button.", nameof(buttons));
            if (buttons.Any(b => String.IsNullOrEmpty(b.Value)) || buttons.Select(b => b.Value).Distinct(StringComparer.Ordinal).Count() != buttons.Count)
                throw new ArgumentException("The radio buttons need distinct, non-empty values.", nameof(buttons));
            if (buttons.Any(b => b.Value == "Off" || b.Value.StartsWith("/", StringComparison.Ordinal)))
                throw new ArgumentException("A radio button value cannot be \"Off\" (the off state) or start with a slash.", nameof(buttons));
            Check(name, page, style, buttons.Select(b => b.Rectangle).ToArray());

            var field = new PdfFormRadioButtonField(Owner);
            field.Elements[PdfFormField.Keys.T] = TextString(name);
            field.Elements.SetName(PdfFormField.Keys.V, "/Off");
            if (!String.IsNullOrEmpty(style?.ToolTip))
                field.Elements[PdfFormField.Keys.TU] = TextString(style!.ToolTip!);

            var mark = ColorOperands(style?.TextColor ?? XColors.Black);
            foreach (var (value, rectangle) in buttons)
            {
                var kid = new PdfFormFieldWidget(Owner);
                SetWidgetEntries(kid, page, rectangle, style);
                kid.Elements[PdfFormField.Keys.Parent] = field.Reference;
                field.Kids.Elements.Add(kid);
                ButtonAppearance.Build(kid, Owner, ButtonAppearance.Kind.RadioButton, new PdfName("/" + value).Value, mark);
                AddToPage(page, kid.GetAsWidgetAnnotation());
            }
            Fields.Elements.Add(field);
            return field;
        }

        /// <summary>
        /// Adds a combo box (a drop-down list) with the given options, none selected.
        /// </summary>
        /// <exception cref="InvalidOperationException">The document is PDF/A: the field's Helvetica is not embedded.</exception>
        public PdfFormComboBoxField AddComboBox(string name, PdfPage page, XRect rectangle, IEnumerable<string> options,
            PdfFormFieldStyle? style = null)
            => AddChoiceField(() => new PdfFormComboBoxField(Owner), name, page, rectangle, options, style);

        /// <summary>
        /// Adds a list box with the given options, none selected. Set the MultiSelect flag on the returned field to
        /// allow several selections.
        /// </summary>
        /// <exception cref="InvalidOperationException">The document is PDF/A: the field's Helvetica is not embedded.</exception>
        public PdfFormListBoxField AddListBox(string name, PdfPage page, XRect rectangle, IEnumerable<string> options,
            PdfFormFieldStyle? style = null)
            => AddChoiceField(() => new PdfFormListBoxField(Owner), name, page, rectangle, options, style);

        /// <summary>
        /// Adds an empty signature field, for example a "sign here" box. Sign into it with
        /// <c>DigitalSignatureOptions.FieldName</c>, or leave it for the recipient to sign.
        /// </summary>
        public PdfFormSignatureField AddSignatureField(string name, PdfPage page, XRect rectangle, PdfFormFieldStyle? style = null)
        {
            Check(name, page, style, rectangle);
            var field = new PdfFormSignatureField(Owner);
            field.Elements.SetName(PdfFormField.Keys.FT, PdfFormFieldType.Signature.Value);
            Place(field, name, page, rectangle, style);
            ButtonAppearance.Build(field, Owner, ButtonAppearance.Kind.Box, "", "");
            return field;
        }

        T AddChoiceField<T>(Func<T> create, string name, PdfPage page, XRect rectangle, IEnumerable<string> options, PdfFormFieldStyle? style)
            where T : PdfFormChoiceField
        {
            var list = options?.ToList() ?? throw new ArgumentNullException(nameof(options));
            if (list.Count == 0 || list.Any(o => o == null))
                throw new ArgumentException("A choice field needs at least one option, and no option may be null.", nameof(options));
            Check(name, page, style, rectangle);
            RefuseTextFieldsInPdfA(name);
            var field = create();
            Place(field, name, page, rectangle, style);
            var opt = new PdfArray(Owner);
            foreach (var option in list)
                opt.Elements.Add(TextString(option));
            field.Elements[PdfFormChoiceField.Keys.Opt] = opt;
            SetDefaultAppearance(field, style);
            BuildTextAppearance(field);
            return field;
        }

        void Place(PdfFormField field, string name, PdfPage page, XRect rectangle, PdfFormFieldStyle? style)
        {
            field.Elements[PdfFormField.Keys.T] = TextString(name);
            if (!String.IsNullOrEmpty(style?.ToolTip))
                field.Elements[PdfFormField.Keys.TU] = TextString(style!.ToolTip!);
            SetWidgetEntries(field, page, rectangle, style);
            Fields.Elements.Add(field);
            AddToPage(page, field.GetAsWidgetAnnotation());
        }

        /// <summary>
        /// Checks everything before any object is created, so a refused call leaves the document unchanged.
        /// </summary>
        void Check(string name, PdfPage page, PdfFormFieldStyle? style, params XRect[] rectangles)
        {
            CheckNameAndPage(name, page);
            RefuseInPdfUA(name);
            foreach (var rectangle in rectangles)
            {
                if (!IsFinite(rectangle.X) || !IsFinite(rectangle.Y) || !IsFinite(rectangle.Width) || !IsFinite(rectangle.Height)
                    || rectangle.Width <= 0 || rectangle.Height <= 0)
                    throw new ArgumentException("A field's rectangle must have finite coordinates and a positive width and height.", nameof(rectangles));
            }
            if (style != null && (!IsFinite(style.FontSize) || style.FontSize < 0))
                throw new ArgumentOutOfRangeException(nameof(style), "The font size must be 0 (fit to the field) or a positive number of points.");
            if (style != null && (!IsFinite(style.BorderWidth) || style.BorderWidth < 0))
                throw new ArgumentOutOfRangeException(nameof(style), "The border width must be 0 or a positive number of points.");

            static bool IsFinite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        void CheckNameAndPage(string name, PdfPage page)
        {
            if (String.IsNullOrEmpty(name))
                throw new ArgumentException("A form field needs a name.", nameof(name));
            if (name.Contains('.'))
                throw new ArgumentException($"The field name '{name}' contains a period, which separates the names of nested fields.", nameof(name));
            if (Fields.TryGetField(name, out _))
                throw new ArgumentException($"The form already has a field named '{name}'.", nameof(name));
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            if (!ReferenceEquals(page.Owner, Owner))
                throw new ArgumentException("The page belongs to another document.", nameof(page));
            if (page.Rotate % 360 != 0)
                throw new NotSupportedException("Form fields can only be added to pages that are not rotated.");
        }

        /// <summary>
        /// The widget annotation entries: printable, on the page, its rectangle converted from drawing coordinates
        /// (top left origin) to PDF coordinates, and the border and background as /MK and /BS.
        /// </summary>
        void SetWidgetEntries(PdfDictionary widget, PdfPage page, XRect rectangle, PdfFormFieldStyle? style)
        {
            // The visible page: the crop box if the page has one, otherwise the media box. Both may start anywhere,
            // and any two opposite corners may be given.
            var box = page.EffectiveCropBoxReadOnly;
            if (box.X1 == box.X2 || box.Y1 == box.Y2)
                box = page.MediaBox;
            double left = Math.Min(box.X1, box.X2) + rectangle.X;
            double top = Math.Max(box.Y1, box.Y2) - rectangle.Y;

            widget.Elements.SetName(PdfAnnotation.Keys.Type, "/Annot");
            widget.Elements.SetName(PdfAnnotation.Keys.Subtype, "/Widget");
            widget.Elements.SetInteger(PdfAnnotation.Keys.F, (int)PdfAnnotationFlags.Print);
            widget.Elements[PdfAnnotation.Keys.P] = page.Reference;
            widget.Elements[PdfAnnotation.Keys.Rect] = new PdfRectangle(left, top - rectangle.Height, left + rectangle.Width, top);

            // Field colors are opaque (/MK has no transparency): a fully transparent color means no border or
            // background, any other alpha is ignored.
            var borderColor = Visible(style?.BorderColor ?? XColors.Black);
            var background = Visible(style?.BackgroundColor ?? XColor.Empty);
            double borderWidth = style?.BorderWidth ?? 1;
            var mk = new PdfDictionary(Owner);
            if (!borderColor.IsEmpty && borderWidth > 0)
                mk.Elements["/BC"] = ColorArray(borderColor);
            if (!background.IsEmpty)
                mk.Elements["/BG"] = ColorArray(background);
            if (mk.Elements.Count > 0)
                widget.Elements["/MK"] = mk;
            var bs = new PdfDictionary(Owner);
            bs.Elements.SetReal("/W", borderColor.IsEmpty ? 0 : borderWidth);
            bs.Elements.SetName("/S", "/S");
            widget.Elements["/BS"] = bs;
        }

        static void AddToPage(PdfPage page, PdfWidgetAnnotation widget)
        {
            var annotations = page.Elements.GetArray(PdfPage.Keys.Annots);
            if (annotations == null)
                page.Elements.Add(PdfPage.Keys.Annots, new PdfArray(page.Owner, widget));
            else
                annotations.Elements.Add(widget);
        }

        /// <summary>
        /// The default appearance: Helvetica (WinAnsi) from the form's default resources, the size and the color.
        /// </summary>
        void SetDefaultAppearance(PdfFormTextFieldBase field, PdfFormFieldStyle? style)
        {
            var fonts = DefaultResourceFonts();
            if (!fonts.Elements.ContainsKey(DefaultFontName))
            {
                var font = new PdfDictionary(Owner);
                font.Elements.SetName("/Type", "/Font");
                font.Elements.SetName("/Subtype", "/Type1");
                font.Elements.SetName("/BaseFont", "/Helvetica");
                font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
                Owner.Internals.AddObject(font);
                fonts.Elements[DefaultFontName] = font.Reference;
            }
            if (!Elements.ContainsKey(Keys.DA))
                Elements.SetString(Keys.DA, DefaultFontName + " 0 Tf 0 g");

            double size = Math.Max(0, style?.FontSize ?? 0);
            var color = ColorOperands(style?.TextColor ?? XColors.Black);
            field.Elements.SetString(PdfFormTextFieldBase.Keys.DA,
                String.Format(CultureInfo.InvariantCulture, "{0} {1} Tf {2} rg", DefaultFontName, size, color));
            int quadding = (style?.Alignment ?? XParagraphAlignment.Left) switch
            {
                XParagraphAlignment.Center => 1,
                XParagraphAlignment.Right => 2,
                _ => 0,
            };
            if (quadding != 0)
                field.Elements.SetInteger(PdfFormTextFieldBase.Keys.Q, quadding);
        }

        PdfDictionary DefaultResourceFonts()
        {
            var resources = Elements.GetDictionary(Keys.DR);
            if (resources == null)
            {
                resources = new PdfDictionary(Owner);
                Elements[Keys.DR] = resources;
            }
            var fonts = resources.Elements.GetDictionary("/Font");
            if (fonts == null)
            {
                fonts = new PdfDictionary(Owner);
                resources.Elements["/Font"] = fonts;
            }
            return fonts;
        }

        /// <summary>
        /// Builds the appearance of an empty text or choice field (its border, background and options). If the form's
        /// /Helv is another font that cannot be used, viewers are asked to build it.
        /// </summary>
        void BuildTextAppearance(PdfFormTextFieldBase field)
        {
            if (!FieldAppearance.TryUpdate(field, field.CurrentContent() ?? new FieldContent()))
                NeedAppearances = true;
        }

        void RefuseTextFieldsInPdfA(string name)
        {
            // An opened file is not marked as PDF/A, but its XMP may claim it.
            bool pdfA = PdfAManager.ForDocument(Owner).IsPdfADocument || XmpPacket() is { } packet && XmpPacketUpdater.ClaimedPdfAPart(packet) > 0;
            if (pdfA)
                throw new InvalidOperationException(
                    $"The field '{name}' cannot be added to this PDF/A document: text and choice fields use Helvetica, which is not embedded, and PDF/A requires embedded fonts.");
        }

        /// <summary>
        /// PDF/UA needs every widget tagged (a Form structure element, /StructParent, tab order by structure), which
        /// the Add… methods do not write yet; a field would make the document non-conforming without notice.
        /// </summary>
        void RefuseInPdfUA(string name)
        {
            bool pdfUA = Owner.UAManager != null || XmpPacket() is { } packet && packet.Contains("pdfuaid:part");
            if (pdfUA)
                throw new InvalidOperationException(
                    $"The field '{name}' cannot be added to this tagged (PDF/UA) document: form fields are not tagged yet.");
        }

        string? XmpPacket() => Owner.Catalog.GetMetadata() is { } metadata ? MetadataManager.ReadPacket(metadata) : null;

        static XColor Visible(XColor color) => color.IsEmpty || color.A <= 0 ? XColor.Empty : color;

        static PdfString TextString(string text)
            => text.All(ch => ch < 128) ? new PdfString(text) : new PdfString(text, PdfStringEncoding.Unicode);

        PdfArray ColorArray(XColor color)
            => new(Owner, new PdfReal(color.R / 255.0), new PdfReal(color.G / 255.0), new PdfReal(color.B / 255.0));

        static string ColorOperands(XColor color)
            => String.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###}", color.R / 255.0, color.G / 255.0, color.B / 255.0);
    }
}
