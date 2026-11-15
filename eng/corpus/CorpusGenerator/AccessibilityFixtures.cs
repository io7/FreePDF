// Original small oracle/CLI fixtures, independent of the large generated corpus.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;

static class AccessibilityFixtures
{
    internal static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        using var buffer = new MemoryStream();
        using (var pdf = new PdfDocument())
        {
            pdf.Info.Title = "Synthetic accessibility fixture";
            var tags = UAManager.ForDocument(pdf).StructureBuilder;
            using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
            {
                tags.BeginElement(PdfIllustrationElementTag.Figure, "<script>alert('untrusted PDF text')</script>", new XRect(40, 40, 100, 60));
                gfx.DrawRectangle(XBrushes.Red, 40, 40, 100, 60);
                tags.End();
            }
            pdf.Save(buffer);
        }
        var original = buffer.ToArray();
        WriteNew("valid.pdf", original);
        foreach (var mutation in new[] { "missing-alt", "missing-language", "untagged" })
        {
            using var pdf = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Modify);
            if (mutation == "missing-language") pdf.Catalog.Elements.Remove("/Lang");
            else if (mutation == "untagged") pdf.Catalog.Elements.Remove("/StructTreeRoot");
            else
            {
                static PdfDictionary Child(PdfDictionary d) => d.Elements.GetDictionary("/K") ?? d.Elements.GetArray("/K")!.Elements.GetDictionary(0)!;
                Child(Child(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!)).Elements.Remove("/Alt");
            }
            using var bytes = new MemoryStream(); pdf.Save(bytes); WriteNew(mutation + ".pdf", bytes.ToArray());
        }
        File.WriteAllText(Path.Combine(directory, "README.txt"), "Synthetic machine-check fixtures, not certified accessible documents. The valid fixture deliberately includes HTML-like alternative text to test report escaping; its meaning requires human review.\n");
        void WriteNew(string name, byte[] data)
        {
            using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew);
            output.Write(data);
        }
    }
}
