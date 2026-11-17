// Original small oracle/CLI fixtures, independent of the large generated corpus.
using System.Text;
using FreePDF.Lab.Fixtures;
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
        foreach (var mutation in new[] { "missing-alt", "missing-language", "untagged", "incomplete-missing-language", "nested-form", "reused-tagged-form", "untagged-form-paint" })
        {
            using var pdf = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Modify);
            if (mutation == "missing-language") pdf.Catalog.Elements.Remove("/Lang");
            else if (mutation == "untagged") pdf.Catalog.Elements.Remove("/StructTreeRoot");
            else if (mutation == "incomplete-missing-language")
            {
                // A confirmed failure plus an inline property dictionary outside native coverage.
                pdf.Catalog.Elements.Remove("/Lang");
                pdf.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Span <</Lang (en) /ActualText (sample)>> BDC EMC"));
            }
            else if (mutation is "nested-form" or "reused-tagged-form" or "untagged-form-paint")
            {
                var inner = AccessibilityFormFixture.AddTaggedForm(pdf);
                if (mutation == "reused-tagged-form")
                    pdf.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Outer Do"));
                if (mutation == "untagged-form-paint")
                    inner.Stream.Value = inner.Stream.Value.Concat(Encoding.ASCII.GetBytes(" 2 2 5 5 re f")).ToArray();
            }
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
