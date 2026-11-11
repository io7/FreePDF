// FreePDF - MIT licence. See the LICENSE file in the solution root.

using System.Globalization;
using System.Text;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Internal;

namespace PdfSharp.Pdf.Forms
{
    /// <summary>
    /// Builds the normal appearance (/AP /N) of text and choice field widgets from the field value, the default
    /// appearance (/DA), the alignment (/Q) and the widget's border and background (/MK), as ISO 32000-1 12.7.3.3
    /// describes for variable text. The text is drawn with the /DA font from the form's default resources (/DR).
    /// </summary>
    static class FieldAppearance
    {
        const double Padding = 2;

        /// <summary>
        /// True if the appearance can be built: the /DA names a font in /DR that FreePDF can write the text with.
        /// </summary>
        public static bool CanBuild(PdfFormTextFieldBase field, FieldContent content)
        {
            var form = field.Owner.Catalog.GetAcroForm();
            var da = Inherited(field, "/DA") ?? form?.Elements.GetString("/DA") ?? "";
            if (!TryParseDefaultAppearance(da, out var fontName, out _, out _))
                return false;
            return FindFont(field, form, fontName)?.Value is PdfDictionary font && CanEncode(font, content.AllText);
        }

        /// <summary>
        /// Updates every widget of the field. Returns false if the appearance cannot be built (no usable /DA font, or
        /// text the font cannot encode); the caller then decides between NeedAppearances and an error.
        /// </summary>
        public static bool TryUpdate(PdfFormTextFieldBase field, FieldContent content)
        {
            var form = field.Owner.Catalog.GetAcroForm();
            var da = Inherited(field, "/DA") ?? form?.Elements.GetString("/DA") ?? "";
            if (!TryParseDefaultAppearance(da, out var fontName, out var fontSize, out var color))
                return false;
            var fontRef = FindFont(field, form, fontName);
            if (fontRef?.Value is not PdfDictionary font || !CanEncode(font, content.AllText))
                return false;

            var alignment = (int)InheritedInteger(field, "/Q", form);
            foreach (var widget in field.GetWidgets())
                Build(widget, font, fontRef, fontName, fontSize, color, alignment, field, content);
            return true;
        }

        static void Build(PdfDictionary widget, PdfDictionary font, PdfReference fontRef, string fontName, double fontSize,
            string color, int alignment, PdfFormField field, FieldContent content)
        {
            if (!widget.Elements.TryGetRectangle(PdfAnnotation.Keys.Rect, out var rect))
                return;
            double width = Math.Abs(rect.X2 - rect.X1);
            double height = Math.Abs(rect.Y2 - rect.Y1);
            var widths = new FontWidths(font);
            var stream = new StringBuilder();

            var mk = widget.Elements.GetDictionary("/MK");
            var background = ColorOperator(mk?.Elements.GetArray("/BG"), fill: true);
            var border = ColorOperator(mk?.Elements.GetArray("/BC"), fill: false);
            double borderWidth = BorderWidth(widget, border != null);
            if (background != null)
                stream.Append(Invariant($"{background} 0 0 {width} {height} re f\n"));
            if (border != null && borderWidth > 0)
                stream.Append(Invariant($"{border} {borderWidth} w {borderWidth / 2} {borderWidth / 2} {width - borderWidth} {height - borderWidth} re S\n"));

            double inset = borderWidth + Padding;
            stream.Append("/Tx BMC\nq\n");
            stream.Append(Invariant($"{inset - 1} {inset - 1} {width - 2 * inset + 2} {height - 2 * inset + 2} re W n\n"));

            if (content.Options != null)
                AppendListBox(stream, widths, fontName, fontSize, color, width, height, inset, content);
            else if (field.TestFlag(PdfFormFieldFlags.Multiline))
                AppendLines(stream, widths, fontName, fontSize, color, alignment, width, height, inset, content.Text);
            else
                AppendSingleLine(stream, widths, fontName, fontSize, color, alignment, width, height, inset, content.Text);

            stream.Append("Q\nEMC\n");

            var appearance = new PdfDictionary(field.Owner);
            appearance.Elements.SetName("/Type", "/XObject");
            appearance.Elements.SetName("/Subtype", "/Form");
            appearance.Elements["/BBox"] = new PdfRectangle(0, 0, width, height);
            var fonts = new PdfDictionary(field.Owner);
            fonts.Elements[fontName] = fontRef;
            var resources = new PdfDictionary(field.Owner);
            resources.Elements["/Font"] = fonts;
            appearance.Elements["/Resources"] = resources;
            appearance.CreateStream(PdfEncoders.RawEncoding.GetBytes(stream.ToString()));
            field.Owner.Internals.AddObject(appearance);

            var ap = new PdfDictionary(field.Owner);
            ap.Elements["/N"] = appearance.Reference;
            widget.Elements["/AP"] = ap;
        }

        static void AppendSingleLine(StringBuilder stream, FontWidths widths, string fontName, double fontSize, string color,
            int alignment, double width, double height, double inset, string text)
        {
            double available = width - 2 * inset;
            double size = fontSize;
            if (size <= 0)
            {
                // Auto size (0): fit the height, then shrink to the width.
                size = Math.Min(12, (height - 2 * inset) * 0.8);
                double natural = widths.Measure(text, size);
                if (natural > available && natural > 0)
                    size *= available / natural;
            }
            double x = AlignedX(alignment, widths.Measure(text, size), width, inset);
            double y = (height - size * 0.7) / 2;
            stream.Append(Invariant($"BT\n{fontName} {size} Tf {color}\n{x} {y} Td\n"));
            stream.Append(Literal(text)).Append(" Tj\nET\n");
        }

        static void AppendLines(StringBuilder stream, FontWidths widths, string fontName, double fontSize, string color,
            int alignment, double width, double height, double inset, string text)
        {
            double size = fontSize;
            if (size <= 0)
                size = 12;
            double leading = size * 1.15;
            var lines = Wrap(text, widths, size, width - 2 * inset);
            double y = height - inset - size;
            stream.Append(Invariant($"BT\n{fontName} {size} Tf {color}\n"));
            foreach (var line in lines)
            {
                double x = AlignedX(alignment, widths.Measure(line, size), width, inset);
                stream.Append(Invariant($"1 0 0 1 {x} {y} Tm\n")).Append(Literal(line)).Append(" Tj\n");
                y -= leading;
            }
            stream.Append("ET\n");
        }

        static void AppendListBox(StringBuilder stream, FontWidths widths, string fontName, double fontSize, string color,
            double width, double height, double inset, FieldContent content)
        {
            double size = fontSize;
            if (size <= 0)
                size = 12;
            double leading = size * 1.15;
            double top = height - inset;
            var options = content.Options!;
            for (int index = 0; index < options.Count; index++)
            {
                double lineTop = top - index * leading;
                if (lineTop - leading < 0)
                    break;
                // Selected options are highlighted as Acrobat does.
                if (content.SelectedIndices.Contains(index))
                    stream.Append(Invariant($"0.6 0.75 0.86 rg {inset - 1} {lineTop - leading} {width - 2 * inset + 2} {leading} re f\n"));
                stream.Append(Invariant($"BT\n{fontName} {size} Tf {color}\n{inset} {lineTop - size} Td\n"));
                stream.Append(Literal(options[index])).Append(" Tj\nET\n");
            }
        }

        static double AlignedX(int alignment, double textWidth, double width, double inset)
        {
            if (alignment == 1)
                return (width - textWidth) / 2;
            if (alignment == 2)
                return width - inset - textWidth;
            return inset;
        }

        // Widths add up (no kerning), so each word is measured once and the line's width is kept as it grows: measuring
        // the whole line again for every word made a long value take time that grew with its square.
        static List<string> Wrap(string text, FontWidths widths, double size, double available)
        {
            var lines = new List<string>();
            double space = widths.Measure(" ", size);
            foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = new StringBuilder();
                double lineWidth = 0;
                foreach (var word in paragraph.Split(' '))
                {
                    double wordWidth = widths.Measure(word, size);
                    if (line.Length > 0 && lineWidth + space + wordWidth > available)
                    {
                        lines.Add(line.ToString());
                        line.Clear().Append(word);
                        lineWidth = wordWidth;
                        continue;
                    }
                    if (line.Length > 0)
                    {
                        line.Append(' ');
                        lineWidth += space;
                    }
                    line.Append(word);
                    lineWidth += wordWidth;
                }
                lines.Add(line.ToString());
            }
            return lines;
        }

        /// <summary>
        /// About how many characters the field's appearance takes for each widget: its text, and the operators written for
        /// each line or option, which are of bounded size (the default appearance's operands are written again as plain
        /// numbers, and a font name is at most 128 characters). Counted without joining or wrapping the text.
        /// </summary>
        internal static long SizePerWidget(PdfFormTextFieldBase field, FieldContent content)
        {
            const long perLine = 256;
            long lines = 1;
            if (content.Options != null)
                lines = Math.Max(1, content.Options.Count);
            else if (field.TestFlag(PdfFormFieldFlags.Multiline))
                lines += content.Text.Count(c => c is ' ' or '\n' or '\r');
            return content.TextLength + lines * perLine + 1024;
        }

        /// <summary>
        /// The characters read to build the field's appearances once, apart from its widgets: the default appearance,
        /// which is parsed when the field is checked and when it is drawn, whoever shares it.
        /// </summary>
        internal static long SizePerField(PdfFormTextFieldBase field)
            => 2L * (Inherited(field, "/DA") ?? field.Owner.Catalog.GetAcroForm()?.Elements.GetString("/DA") ?? "").Length;

        /// <summary>
        /// Writes the text as a PDF literal string in the font's single-byte (WinAnsi) encoding.
        /// </summary>
        static string Literal(string text)
        {
            var bytes = PdfEncoders.WinAnsiEncoding.GetBytes(text);
            var literal = new StringBuilder("(");
            foreach (var b in bytes)
            {
                if (b is (byte)'(' or (byte)')' or (byte)'\\')
                    literal.Append('\\');
                literal.Append((char)b);
            }
            return literal.Append(')').ToString();
        }

        /// <summary>
        /// True if the font is a simple font whose encoding FreePDF writes (WinAnsi, or the standard encoding of a
        /// standard font) and every character of the text is in WinAnsi.
        /// </summary>
        static bool CanEncode(PdfDictionary font, string text)
        {
            var subtype = font.Elements.GetName("/Subtype");
            if (subtype is not ("/Type1" or "/TrueType"))
                return false;
            var encoding = font.Elements.GetValue("/Encoding");
            if (encoding is PdfName name && name.Value != "/WinAnsiEncoding")
                return false;
            if (encoding is PdfDictionary)
                return false;
            var bytes = PdfEncoders.WinAnsiEncoding.GetBytes(text);
            return PdfEncoders.WinAnsiEncoding.GetString(bytes) == text;
        }

        static bool TryParseDefaultAppearance(string da, out string fontName, out double fontSize, out string color)
        {
            fontName = "";
            fontSize = 0;
            color = "0 g";
            var tokens = da.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (token == "Tf" && index >= 2 && tokens[index - 2].StartsWith("/", StringComparison.Ordinal))
                {
                    fontName = tokens[index - 2];
                    double.TryParse(tokens[index - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize);
                }
                else if (token is "g" or "rg" or "k")
                {
                    int operands = 1;
                    if (token == "rg")
                        operands = 3;
                    else if (token == "k")
                        operands = 4;
                    // The operands are written again as plain numbers: each appearance (and each option of a list box)
                    // repeats them, so one long operand, such as a number padded with zeros, would be copied many times.
                    if (index >= operands)
                    {
                        var values = tokens.Skip(index - operands).Take(operands)
                            .Select(operand => double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0)
                            .Select(value => value.ToString("0.#####", CultureInfo.InvariantCulture));
                        color = String.Join(" ", values) + " " + token;
                    }
                }
            }
            // A name is at most 127 bytes (ISO 32000-1 Annex C); a longer one would be repeated as long.
            return fontName.Length is > 1 and <= 128;
        }

        static PdfReference? FindFont(PdfFormField field, PdfForm? form, string fontName)
        {
            var resources = field.Elements.GetDictionary("/DR") ?? form?.Elements.GetDictionary("/DR");
            var fonts = resources?.Elements.GetDictionary("/Font");
            return fonts?.Elements.GetReference(fontName);
        }

        static string? Inherited(PdfFormField field, string key)
        {
            foreach (var current in field.SelfAndAncestors())
            {
                var value = current.Elements.GetString(key);
                if (!String.IsNullOrEmpty(value))
                    return value;
            }
            return null;
        }

        static int InheritedInteger(PdfFormField field, string key, PdfForm? form)
        {
            foreach (var current in field.SelfAndAncestors())
            {
                if (current.Elements.TryGetInteger(key, out var value))
                    return value;
            }
            if (form != null && form.Elements.TryGetInteger(key, out var formValue))
                return formValue;
            return 0;
        }

        internal static string? ColorOperator(PdfArray? components, bool fill)
        {
            if (components == null || components.Elements.Count == 0)
                return null;
            var values = new List<string>();
            foreach (var item in components.Elements)
            {
                if (item is PdfInteger integer)
                    values.Add(integer.Value.ToString(CultureInfo.InvariantCulture));
                else if (item is PdfReal real)
                    values.Add(real.Value.ToString(CultureInfo.InvariantCulture));
            }
            var op = values.Count switch
            {
                1 => "g",
                3 => "rg",
                4 => "k",
                _ => "",
            };
            if (op.Length == 0)
                return null;
            if (!fill)
                op = op.ToUpperInvariant();
            return String.Join(" ", values) + " " + op;
        }

        internal static double BorderWidth(PdfDictionary widget, bool hasBorderColor)
        {
            var bs = widget.Elements.GetDictionary("/BS");
            if (bs != null && bs.Elements.TryGetReal("/W", out var width))
                return width;
            if (hasBorderColor)
                return 1;
            return 0;
        }

        static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What a text or choice field shows: one text, or a list of options with the selected ones.
    /// </summary>
    sealed class FieldContent
    {
        public string Text { get; init; } = "";

        public IReadOnlyList<string>? Options { get; init; }

        public IReadOnlyList<int> SelectedIndices { get; init; } = [];

        public string AllText
        {
            get
            {
                if (Options == null)
                    return Text;
                return String.Join("\n", Options);
            }
        }

        /// <summary>The length of <see cref="AllText"/>, without joining the options.</summary>
        public long TextLength => Options == null ? Text.Length : Options.Sum(option => (long)option.Length) + Math.Max(0, Options.Count - 1);
    }

    /// <summary>
    /// Character widths of a simple font: from its /Widths array, or for a standard font that lists no widths (the
    /// usual /Helv of a form), from Adobe's metrics for the twelve Latin standard fonts. Other fonts without widths
    /// get an average of half the font size, which affects only centred and right-aligned text, wrapping and
    /// automatic font sizes.
    /// </summary>
    sealed class FontWidths
    {
        readonly int _firstChar;
        readonly double[]? _widths;
        readonly short[]? _standardWidths;

        public FontWidths(PdfDictionary font)
        {
            var widths = font.Elements.GetArray("/Widths");
            if (widths == null)
            {
                _standardWidths = StandardFontWidths.Get(StandardFontName(font.Elements.GetName("/BaseFont")));
                return;
            }
            _firstChar = font.Elements.GetInteger("/FirstChar");
            _widths = new double[widths.Elements.Count];
            for (int index = 0; index < _widths.Length; index++)
            {
                widths.Elements.TryGetReal(index, out var width);
                _widths[index] = width;
            }
        }

        public double Measure(string text, double size)
        {
            double total = 0;
            foreach (var b in PdfEncoders.WinAnsiEncoding.GetBytes(text))
                total += CharWidth(b);
            return total * size / 1000;
        }

        double CharWidth(byte code)
        {
            int index = code - _firstChar;
            if (_widths != null && index >= 0 && index < _widths.Length && _widths[index] > 0)
                return _widths[index];
            if (_standardWidths != null && code >= 32 && _standardWidths[code - 32] > 0)
                return _standardWidths[code - 32];
            return 500;
        }

        /// <summary>
        /// Maps a base font name to one of the twelve Latin standard fonts: the standard names themselves, without a
        /// subset prefix (ABCDEF+), and Arial, Times New Roman and Courier New, which are metric-compatible with
        /// Helvetica, Times and Courier.
        /// </summary>
        static string StandardFontName(string baseFont)
        {
            var name = baseFont.TrimStart('/');
            if (name.Length > 7 && name[6] == '+')
                name = name.Substring(7);
            if (StandardFontWidths.Get(name) != null)
                return name;

            bool bold = name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
            bool italic = name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0
                          || name.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
            if (name.StartsWith("Arial", StringComparison.OrdinalIgnoreCase))
                return StyledName("Helvetica", bold, italic, "Oblique");
            if (name.StartsWith("TimesNewRoman", StringComparison.OrdinalIgnoreCase))
                return StyledName("Times", bold, italic, "Italic");
            if (name.StartsWith("CourierNew", StringComparison.OrdinalIgnoreCase))
                return StyledName("Courier", bold, italic, "Oblique");
            return name;
        }

        static string StyledName(string family, bool bold, bool italic, string slant)
        {
            if (bold && italic)
                return family + "-Bold" + slant;
            if (bold)
                return family + "-Bold";
            if (italic)
                return family + "-" + slant;
            if (family == "Times")
                return "Times-Roman";
            return family;
        }
    }
}
