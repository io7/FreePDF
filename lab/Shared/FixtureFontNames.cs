// Original fixture control, shared verbatim by both isolated generators. MIT.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace FreePDF.Lab.Fixtures;

static class FixtureFontNames
{
    // MuPDF 1.28.5 can rasterize identical font bytes differently when only the
    // random subset tag changes. Control this input before either engine saves;
    // do not edit font programs, page commands, or the rendered comparison.
    public static string[] Normalize(PdfDocument document)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dictionary in FontDictionaries(document))
        foreach (var key in new[] { "/BaseFont", "/FontName" })
        {
            var name = dictionary.Elements.GetName(key);
            if (!IsSubsetName(name)) continue;
            if (!names.TryGetValue(name, out var replacement))
            {
                var index = names.Count;
                var tag = new char[6];
                for (var i = tag.Length - 1; i >= 0; i--)
                {
                    tag[i] = (char)('A' + index % 26);
                    index /= 26;
                }
                if (index != 0) throw new InvalidDataException("Too many fixture font subsets.");
                replacement = "/" + new string(tag) + name.Substring(7);
                names.Add(name, replacement);
            }
            dictionary.Elements.SetName(key, replacement);
        }
        return Read(document);
    }

    public static string[] Read(PdfDocument document) => FontDictionaries(document)
        .SelectMany(dictionary => new[] { dictionary.Elements.GetName("/BaseFont"), dictionary.Elements.GetName("/FontName") })
        .Where(IsSubsetName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    static bool IsSubsetName(string name) => name.Length > 8 && name[0] == '/' && name[7] == '+'
        && name.Skip(1).Take(6).All(letter => letter >= 'A' && letter <= 'Z');

    static IEnumerable<PdfDictionary> FontDictionaries(PdfDocument document)
    {
        var seen = new HashSet<PdfObject>();
        foreach (var item in document.Internals.GetAllObjects())
        foreach (var dictionary in Visit(item, seen)) yield return dictionary;
    }

    static IEnumerable<PdfDictionary> Visit(PdfItem item, HashSet<PdfObject> seen)
    {
        if (item is PdfReference reference) item = reference.Value;
        if (!(item is PdfObject value) || !seen.Add(value)) yield break;
        if (value is PdfDictionary dictionary)
        {
            var type = dictionary.Elements.GetName("/Type");
            if (type is "/Font" or "/FontDescriptor") yield return dictionary;
            foreach (var child in dictionary.Elements.Values)
            foreach (var nested in Visit(child, seen)) yield return nested;
        }
        else if (value is PdfArray array)
            foreach (var child in array.Elements)
            foreach (var nested in Visit(child, seen)) yield return nested;
    }
}
