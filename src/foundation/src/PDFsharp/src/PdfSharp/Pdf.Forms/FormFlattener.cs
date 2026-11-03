// FreePDF - MIT licence. See the LICENSE file in the solution root.

using System.Globalization;
using System.Text;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Internal;

namespace PdfSharp.Pdf.Forms
{
    /// <summary>
    /// Flattens a form (<see cref="PdfDocument.Flatten"/>): draws the normal appearance of every visible widget into
    /// its page's content, placed as ISO 32000-1 12.5.5 describes, then removes the widgets and the form.
    /// </summary>
    static class FormFlattener
    {
        const int HiddenFlag = 1 << 1;

        public static void Flatten(PdfDocument document)
        {
            var form = document.Catalog.GetAcroForm();
            if (form != null)
                PrepareAppearances(form);

            foreach (var page in document.Pages)
                FlattenPage(page);

            document.Catalog.Elements.Remove(PdfCatalog.Keys.AcroForm);
        }

        /// <summary>
        /// Draws the appearance of every text and choice field that needs one: all of them when the form asks viewers
        /// to build appearances (NeedAppearances), otherwise those with a widget that has none. Nothing is changed if
        /// a value cannot be drawn, so the document is either flattened completely or left as it was.
        /// </summary>
        static void PrepareAppearances(PdfForm form)
        {
            var pending = new List<(PdfFormTextFieldBase Field, FieldContent Content)>();
            var failures = new List<string>();
            foreach (var field in form.Fields.GetAllFields())
            {
                var content = field.CurrentContent();
                if (content == null || content.AllText.Length == 0 || field is not PdfFormTextFieldBase textField)
                    continue;
                if (!form.NeedAppearances && field.GetWidgets().All(widget => NormalAppearance(widget) != null))
                    continue;
                if (FieldAppearance.CanBuild(textField, content))
                    pending.Add((textField, content));
                else
                    failures.Add($"'{field.FullName}'");
            }
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The form cannot be flattened, because FreePDF cannot draw the value of {String.Join(", ", failures)}: its default " +
                    "appearance needs a font in the form's resources that can encode the text. The document was not changed.");
            }
            foreach (var (field, content) in pending)
                FieldAppearance.TryUpdate(field, content);
        }

        static void FlattenPage(PdfPage page)
        {
            var annotations = page.Elements.GetArray("/Annots");
            if (annotations == null)
                return;

            var kept = new List<PdfItem>();
            var drawing = new StringBuilder();
            foreach (var item in annotations.Elements)
            {
                var entry = item;
                PdfReference.Dereference(ref entry);
                if (entry is not PdfDictionary annotation || annotation.Elements.GetName("/Subtype") != "/Widget")
                {
                    kept.Add(item);
                    continue;
                }
                var appearance = NormalAppearance(annotation);
                if (appearance == null || (annotation.Elements.GetInteger("/F") & HiddenFlag) != 0)
                    continue;
                if (!annotation.Elements.TryGetRectangle("/Rect", out var rect) || !TryPlacement(appearance, rect, out var placement))
                    continue;
                var name = AddXObject(page, appearance);
                drawing.Append("q ").Append(placement).Append(" cm ").Append(name).Append(" Do Q\n");
            }

            if (kept.Count == 0)
                page.Elements.Remove("/Annots");
            else
            {
                annotations.Elements.Clear();
                foreach (var item in kept)
                    annotations.Elements.Add(item);
            }

            if (drawing.Length == 0)
                return;
            // Keep the page's own graphics state from leaking into the drawn fields.
            page.Contents.PrependContent().CreateStream(PdfEncoders.RawEncoding.GetBytes("q\n"));
            page.Contents.AppendContent().CreateStream(PdfEncoders.RawEncoding.GetBytes("Q\n" + drawing));
        }

        /// <summary>
        /// The widget's normal appearance stream: /AP /N itself, or for a check box or radio button the state named
        /// by /AS. Null if there is none.
        /// </summary>
        internal static PdfDictionary? NormalAppearance(PdfDictionary widget)
        {
            var normalEntry = widget.Elements.GetDictionary("/AP")?.Elements["/N"];
            if (normalEntry == null)
                return null;
            PdfItem normal = normalEntry;
            PdfReference.Dereference(ref normal);
            if (normal is not PdfDictionary appearances)
                return null;
            if (appearances.Stream != null)
                return appearances;
            var state = widget.Elements.GetName("/AS");
            if (state.Length == 0)
                return null;
            var stateAppearance = appearances.Elements.GetDictionary(state);
            if (stateAppearance?.Stream == null)
                return null;
            return stateAppearance;
        }

        /// <summary>
        /// The matrix that maps the appearance's bounding box, transformed by its /Matrix, onto the widget's
        /// rectangle (ISO 32000-1 12.5.5, algorithm 8.1 step 2). False for an empty box or rectangle.
        /// </summary>
        static bool TryPlacement(PdfDictionary appearance, PdfRectangle rect, out string placement)
        {
            placement = "";
            if (!appearance.Elements.TryGetRectangle("/BBox", out var box))
                return false;
            double a = 1, b = 0, c = 0, d = 1, e = 0, f = 0;
            if (appearance.Elements.GetArray("/Matrix") is { Elements.Count: 6 } matrix)
            {
                matrix.Elements.TryGetReal(0, out a);
                matrix.Elements.TryGetReal(1, out b);
                matrix.Elements.TryGetReal(2, out c);
                matrix.Elements.TryGetReal(3, out d);
                matrix.Elements.TryGetReal(4, out e);
                matrix.Elements.TryGetReal(5, out f);
            }

            double minX = Double.MaxValue, minY = Double.MaxValue, maxX = Double.MinValue, maxY = Double.MinValue;
            foreach (var (x, y) in new[] { (box.X1, box.Y1), (box.X2, box.Y1), (box.X1, box.Y2), (box.X2, box.Y2) })
            {
                double tx = a * x + c * y + e;
                double ty = b * x + d * y + f;
                minX = Math.Min(minX, tx);
                minY = Math.Min(minY, ty);
                maxX = Math.Max(maxX, tx);
                maxY = Math.Max(maxY, ty);
            }

            double left = Math.Min(rect.X1, rect.X2);
            double bottom = Math.Min(rect.Y1, rect.Y2);
            double width = Math.Abs(rect.X2 - rect.X1);
            double height = Math.Abs(rect.Y2 - rect.Y1);
            if (maxX - minX <= 0 || maxY - minY <= 0 || width <= 0 || height <= 0)
                return false;

            double scaleX = width / (maxX - minX);
            double scaleY = height / (maxY - minY);
            placement = String.Join(" ", new[] { scaleX, 0, 0, scaleY, left - minX * scaleX, bottom - minY * scaleY }
                .Select(value => value.ToString("0.#####", CultureInfo.InvariantCulture)));
            return true;
        }

        /// <summary>
        /// Adds the appearance stream to the page's XObject resources under a new name and returns that name.
        /// </summary>
        static string AddXObject(PdfPage page, PdfDictionary appearance)
        {
            if (appearance.Elements.GetName("/Subtype").Length == 0)
                appearance.Elements.SetName("/Subtype", "/Form");
            if (appearance.Reference == null)
                page.Owner.Internals.AddObject(appearance);
            var resources = page.Resources;
            var xObjects = resources.Elements.GetDictionary("/XObject");
            if (xObjects == null)
            {
                xObjects = new PdfDictionary(page.Owner);
                resources.Elements["/XObject"] = xObjects;
            }
            int number = 0;
            string name;
            do
                name = "/FreePDFField" + (number++).ToString(CultureInfo.InvariantCulture);
            while (xObjects.Elements.ContainsKey(name));
            xObjects.Elements[name] = appearance.RequiredReference;
            return name;
        }
    }
}
