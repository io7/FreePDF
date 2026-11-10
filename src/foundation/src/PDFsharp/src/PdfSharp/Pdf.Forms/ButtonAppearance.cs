// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Globalization;
using System.Text;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Internal;

namespace PdfSharp.Pdf.Forms
{
    /// <summary>
    /// Builds the normal appearances of new check boxes, radio buttons and empty signature fields as vector drawings:
    /// background and border from the widget's /MK and /BS, and for the on state a check mark or a dot. No font is
    /// needed, so the appearances also suit PDF/A.
    /// </summary>
    static class ButtonAppearance
    {
        // Control point distance of a quarter circle drawn with one cubic Bezier curve.
        const double Kappa = 0.5522847498;

        public enum Kind
        {
            CheckBox,
            RadioButton,
            Box,
        }

        /// <summary>
        /// Writes /AP /N with the on state (for check boxes and radio buttons) and /Off, and sets /AS to /Off.
        /// A box (empty signature field) gets one appearance stream.
        /// </summary>
        public static void Build(PdfDictionary widget, PdfDocument document, Kind kind, string onState, string markColor)
        {
            if (!widget.Elements.TryGetRectangle(PdfAnnotation.Keys.Rect, out var rect))
                return;
            double width = Math.Abs(rect.X2 - rect.X1);
            double height = Math.Abs(rect.Y2 - rect.Y1);

            var mk = widget.Elements.GetDictionary("/MK");
            var background = FieldAppearance.ColorOperator(mk?.Elements.GetArray("/BG"), fill: true);
            var border = FieldAppearance.ColorOperator(mk?.Elements.GetArray("/BC"), fill: false);
            double borderWidth = FieldAppearance.BorderWidth(widget, border != null);

            var off = new StringBuilder();
            if (kind == Kind.RadioButton)
            {
                double radius = Math.Min(width, height) / 2;
                if (background != null)
                    AppendCircle(off.Append(background).Append('\n'), width / 2, height / 2, radius).Append("f\n");
                if (border != null && borderWidth > 0)
                {
                    off.Append(Invariant($"{border} {borderWidth} w\n"));
                    AppendCircle(off, width / 2, height / 2, radius - borderWidth / 2).Append("S\n");
                }
            }
            else
            {
                if (background != null)
                    off.Append(Invariant($"{background} 0 0 {width} {height} re f\n"));
                if (border != null && borderWidth > 0)
                    off.Append(Invariant($"{border} {borderWidth} w {borderWidth / 2} {borderWidth / 2} {width - borderWidth} {height - borderWidth} re S\n"));
            }

            var ap = new PdfDictionary(document);
            if (kind == Kind.Box)
            {
                ap.Elements["/N"] = Stream(document, width, height, off.ToString());
                widget.Elements["/AP"] = ap;
                return;
            }

            var on = new StringBuilder(off.ToString());
            double size = Math.Min(width, height);
            if (kind == Kind.RadioButton)
                AppendCircle(on.Append(markColor).Append(" rg\n"), width / 2, height / 2, size * 0.22).Append("f\n");
            else
            {
                // A check mark in the middle of the box, about two thirds of its size.
                double x = (width - size) / 2, y = (height - size) / 2;
                on.Append(Invariant($"{markColor} RG {Math.Max(1, size * 0.1)} w 1 J 1 j\n"));
                on.Append(Invariant($"{x + size * 0.22} {y + size * 0.52} m {x + size * 0.42} {y + size * 0.28} l {x + size * 0.8} {y + size * 0.74} l S\n"));
            }

            var normal = new PdfDictionary(document);
            normal.Elements[onState] = Stream(document, width, height, on.ToString());
            normal.Elements["/Off"] = Stream(document, width, height, off.ToString());
            ap.Elements["/N"] = normal;
            widget.Elements["/AP"] = ap;
            widget.Elements.SetName("/AS", "/Off");
        }

        static PdfReference Stream(PdfDocument document, double width, double height, string content)
        {
            var appearance = new PdfDictionary(document);
            appearance.Elements.SetName("/Type", "/XObject");
            appearance.Elements.SetName("/Subtype", "/Form");
            appearance.Elements["/BBox"] = new PdfRectangle(0, 0, width, height);
            appearance.Elements["/Resources"] = new PdfDictionary(document);
            appearance.CreateStream(PdfEncoders.RawEncoding.GetBytes(content));
            document.Internals.AddObject(appearance);
            return appearance.Reference!;
        }

        static StringBuilder AppendCircle(StringBuilder stream, double cx, double cy, double r)
        {
            double k = r * Kappa;
            stream.Append(Invariant($"{cx + r} {cy} m\n"));
            stream.Append(Invariant($"{cx + r} {cy + k} {cx + k} {cy + r} {cx} {cy + r} c\n"));
            stream.Append(Invariant($"{cx - k} {cy + r} {cx - r} {cy + k} {cx - r} {cy} c\n"));
            stream.Append(Invariant($"{cx - r} {cy - k} {cx - k} {cy - r} {cx} {cy - r} c\n"));
            stream.Append(Invariant($"{cx + k} {cy - r} {cx + r} {cy - k} {cx + r} {cy} c\n"));
            return stream;
        }

        static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }
}
