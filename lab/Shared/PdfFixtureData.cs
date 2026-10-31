// Original synthetic PDFs shared by the regression tests and comparison engines. MIT.
using System.Globalization;
using System.Text;

namespace FreePDF.Lab.Fixtures;

static class PdfFixtureData
{
    public static byte[] Create(string[] objects, Func<int, string>? trailerEntries = null)
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(text.Length);
            text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = text.Length;
        text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R {trailerEntries?.Invoke(xref)} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
    internal static byte[] Hybrid(bool newerObject, bool olderObject = false)
    {
        const string packed = "8 0 (compressed marker)";
        var objects = new List<string> {
            "<< /Type /Catalog /Pages 2 0 R /HybridMarker 8 0 R >>",
            ReaderFixtureData.SimplePage[1], ReaderFixtureData.SimplePage[2],
            $"<< /Type /ObjStm /N 1 /First 4 /Length {packed.Length} >>\nstream\n{packed}\nendstream",
            "<< /Type /XRef /Size 9 /W [1 4 2] /Index [8 1] /Filter /ASCIIHexDecode /Length 15 >>\nstream\n02000000040000>\nendstream"
        };
        if (olderObject) objects.AddRange(new[] {"null", "null", "(old file-level marker)"});
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i=0;i<objects.Count;i++) { offsets.Add(text.Length); text.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n"); }
        var main = text.Length;
        text.Append("xref\n0 9\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        for (var i=objects.Count+1;i<9;i++) text.Append("0000000000 65535 f \n");
        text.Append($"trailer\n<< /Size 9 /Root 1 0 R >>\nstartxref\n{main}\n%%EOF\n");
        var update = text.Length;
        text.Append($"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 9 /Root 1 0 R /Prev {main} /XRefStm {offsets[4]} >>\nstartxref\n{update}\n%%EOF\n");
        if (newerObject)
        {
            var position = text.Length; text.Append("8 0 obj\n(newer marker)\nendobj\n");
            var last = text.Length;
            text.Append($"xref\n8 1\n{position.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \ntrailer\n<< /Size 9 /Root 1 0 R /Prev {update} >>\nstartxref\n{last}\n%%EOF\n");
        }
        return Encoding.ASCII.GetBytes(text.ToString());
    }

}

static class ReaderFixtureData
{
    internal static readonly string[] SimplePage = {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>"
    };

    internal static byte[] Get(string name)
    {
        switch (name)
        {
            case "valid": return PdfFixtureData.Create(SimplePage);
            case "trailer-self": return PdfFixtureData.Create(SimplePage, xref => $"/Prev {xref}");
            case "trailer-negative": return PdfFixtureData.Create(SimplePage, _ => "/Prev -1");
            case "trailer-outside": return PdfFixtureData.Create(SimplePage, _ => "/Prev 999999");
            case "trailer-cycle":
            case "incremental":
            {
                var original = PdfFixtureData.Create(SimplePage, name == "trailer-cycle" ? _ => "/Prev 0000000000" : null);
                var text = Encoding.ASCII.GetString(original);
                var oldXref = text.IndexOf("xref\n", StringComparison.Ordinal);
                var nextXref = text.Length;
                if (name == "trailer-cycle")
                    text = text.Replace("/Prev 0000000000", "/Prev " + nextXref.ToString("D10", CultureInfo.InvariantCulture));
                return Encoding.ASCII.GetBytes(text + $"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 4 /Root 1 0 R /Prev {oldXref} >>\nstartxref\n{nextXref}\n%%EOF\n");
            }
            case "tree-self": return PdfFixtureData.Create(new[] {
                SimplePage[0], "<< /Type /Pages /Kids [2 0 R] /Count 1 >>", SimplePage[2] });
            case "tree-cycle": return PdfFixtureData.Create(new[] {
                SimplePage[0], "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Pages /Parent 2 0 R /Kids [2 0 R] /Count 1 >>" });
            case "tree-repeated": return PdfFixtureData.Create(new[] {
                SimplePage[0], "<< /Type /Pages /Kids [3 0 R 3 0 R] /Count 2 >>", SimplePage[2] });
            case "hybrid-outside": return PdfFixtureData.Create(SimplePage, _ => "/XRefStm 999999");
            case "hybrid-table": return PdfFixtureData.Create(SimplePage, xref => $"/XRefStm {xref}");
            case "tree-deep":
            {
                const int depth = 2000;
                var objects = new List<string> { SimplePage[0] };
                for (var number = 2; number < depth + 2; number++)
                    objects.Add($"<< /Type /Pages /Kids [{number+1} 0 R] /Count 1 {(number == 2 ? "/MediaBox [0 0 240 200]" : $"/Parent {number-1} 0 R")} >>");
                objects.Add($"<< /Type /Page /Parent {depth+1} 0 R >>");
                return PdfFixtureData.Create(objects.ToArray());
            }
            default: throw new ArgumentException("Unknown reader probe.", nameof(name));
        }
    }
}

