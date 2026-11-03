// Tagged PDF built with the core StructureBuilder. Saving a figure failed with "A direct object can only be added
// once to a container" when its attribute array collapsed to a single dictionary. Original fixtures; MIT, see LICENSE.
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public class TaggedPdfTests
{
    public TaggedPdfTests() => TestFonts.Initialize();

    static PdfDictionary FirstChild(PdfDictionary element)
        => element.Elements.GetDictionary("/K") ?? element.Elements.GetArray("/K")!.Elements.GetDictionary(0)!;

    [Fact]
    public void A_figure_with_alternative_text_and_bounding_box_is_saved()
    {
        using var saved = new MemoryStream();
        using (var document = new PdfDocument())
        {
            document.Info.Title = "Tagged figure";
            var structure = UAManager.ForDocument(document).StructureBuilder;
            var page = document.AddPage();
            using (var graphics = XGraphics.FromPdfPage(page))
            {
                var box = new XRect(40, 40, 100, 60);
                structure.BeginElement(PdfIllustrationElementTag.Figure, "A red rectangle", box);
                graphics.DrawRectangle(XBrushes.Red, box);
                structure.End();
            }
            document.Save(saved);
        }

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        var root = reopened.Catalog.Elements.GetDictionary("/StructTreeRoot")!;
        var figure = FirstChild(FirstChild(root));
        Assert.Equal("/Figure", figure.Elements.GetName("/S"));
        Assert.Equal("A red rectangle", figure.Elements.GetString("/Alt"));
        Assert.Equal(4, figure.Elements.GetDictionary("/A")!.Elements.GetArray("/BBox")!.Elements.Count);
    }
}
