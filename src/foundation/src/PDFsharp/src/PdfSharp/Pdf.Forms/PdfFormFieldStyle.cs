// FreePDF - MIT, see the LICENSE file in the solution root.

using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;

namespace PdfSharp.Pdf.Forms
{
    /// <summary>
    /// How a new form field looks: its text, border and background. Used by the <c>Add…</c> methods of
    /// <see cref="PdfForm"/>; behaviour such as read only, required, multiline or password is set on the returned
    /// field (<see cref="PdfFormField.SetFlag(PdfFormFieldFlags)"/>).
    /// </summary>
    public sealed class PdfFormFieldStyle
    {
        /// <summary>
        /// Gets or sets the font size of the field's text in points. 0, the default, fits the text to the field.
        /// The font is Helvetica.
        /// </summary>
        public double FontSize { get; set; }

        /// <summary>
        /// Gets or sets the color of the text, the check mark and the radio button dot. The default is black.
        /// </summary>
        public XColor TextColor { get; set; } = XColors.Black;

        /// <summary>
        /// Gets or sets the border color. The default is black; <see cref="XColor.Empty"/> draws no border.
        /// </summary>
        public XColor BorderColor { get; set; } = XColors.Black;

        /// <summary>
        /// Gets or sets the background color. The default, <see cref="XColor.Empty"/>, leaves the field transparent.
        /// </summary>
        public XColor BackgroundColor { get; set; } = XColor.Empty;

        /// <summary>
        /// Gets or sets the border width in points. The default is 1.
        /// </summary>
        public double BorderWidth { get; set; } = 1;

        /// <summary>
        /// Gets or sets the alignment of the text in text and choice fields: left (the default), center or right.
        /// </summary>
        public XParagraphAlignment Alignment { get; set; } = XParagraphAlignment.Left;

        /// <summary>
        /// Gets or sets the text that viewers show as the field's tool tip and screen readers read as its name.
        /// </summary>
        public string? ToolTip { get; set; }
    }
}
