// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using System.Collections.Generic;
using PdfSharp.Pdf.Advanced;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Reads PDF objects without creating entries, so a page is never changed by reading it.
    /// </summary>
    static class TextItems
    {
        internal static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

        internal static PdfDictionary? Dict(PdfDictionary? dictionary, string key)
        {
            if (dictionary == null)
                return null;
            return Resolve(dictionary.Elements[key]) as PdfDictionary;
        }

        internal static string? NameValue(PdfItem? item) => Resolve(item) switch
        {
            PdfName name => name.Value,
            PdfNameObject name => name.Value,
            _ => null,
        };

        internal static string NameOf(PdfDictionary? dictionary, string key)
        {
            if (dictionary == null)
                return "";
            return NameValue(dictionary.Elements[key]) ?? "";
        }

        internal static double? Number(PdfItem? item) => Resolve(item) switch
        {
            PdfInteger n => n.Value,
            PdfLongInteger n => n.Value,
            PdfReal n => n.Value,
            PdfIntegerObject n => n.Value,
            PdfLongIntegerObject n => n.Value,
            PdfRealObject n => n.Value,
            _ => null,
        };

        internal static int? Integer(PdfItem? item)
        {
            var number = Number(item);
            if (number == null || number.Value != Math.Floor(number.Value) || number.Value < int.MinValue || number.Value > int.MaxValue)
                return null;
            return (int)number.Value;
        }

        internal static IEnumerable<PdfItem> Items(PdfItem? item)
        {
            item = Resolve(item);
            if (item is PdfArray array)
            {
                foreach (var element in array.Elements)
                    yield return element;
            }
            else if (item != null && item is not PdfNull && item is not PdfNullObject)
            {
                yield return item;
            }
        }

        /// <summary>A matrix array [a b c d e f], or null if it is not six numbers.</summary>
        internal static TextMatrix? Matrix(PdfItem? item)
        {
            if (Resolve(item) is not PdfArray array || array.Elements.Count != 6)
                return null;
            var values = new double[6];
            for (int i = 0; i < 6; i++)
            {
                var value = Number(array.Elements[i]);
                if (value == null)
                    return null;
                values[i] = value.Value;
            }
            return new TextMatrix(values[0], values[1], values[2], values[3], values[4], values[5]);
        }
    }
}
