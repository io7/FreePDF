// Tagged PDF from the layout engine (PdfDocumentRenderer.TaggedPdf): headings and paragraphs are structure elements,
// headers and footers are artifacts. Original fixtures; MIT, see LICENSE.
using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class TaggedLayoutTests
{
    public TaggedLayoutTests() => TestFonts.Initialize();

    static Document Invoice(string title = "Tagged invoice")
    {
        var document = new Document { Culture = CultureInfo.GetCultureInfo("nb-NO") };
        document.Info.Title = title;
        document.Styles[StyleNames.Normal]!.Font.Name = "FreePDF Synthetic";
        var section = document.AddSection();
        section.Headers.Primary.AddParagraph("Header text");
        section.Footers.Primary.AddParagraph("Page ").AddPageField();
        var heading = section.AddParagraph("Invoice");
        heading.Format.OutlineLevel = OutlineLevel.Level1;
        var body = section.AddParagraph("Body text");
        body.Format.Borders.Width = 0.5;
        var sub = section.AddParagraph("Details");
        sub.Format.OutlineLevel = OutlineLevel.Level2;
        return document;
    }

    static PdfDocument RenderAndReopen(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document, TaggedPdf = true };
        renderer.RenderDocument();
        using var saved = new MemoryStream();
        renderer.Save(saved, false);
        return PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
    }

    static IEnumerable<string> StructureTypes(PdfDictionary element)
    {
        var kids = element.Elements.GetValue("/K");
        var children = kids switch
        {
            PdfArray array => array.Elements.Select(e => (e as PdfSharp.Pdf.Advanced.PdfReference)?.Value ?? e),
            PdfSharp.Pdf.Advanced.PdfReference reference => new[] { reference.Value },
            PdfDictionary dictionary => new PdfItem[] { dictionary },
            _ => Array.Empty<PdfItem>(),
        };
        foreach (var child in children.OfType<PdfDictionary>().Where(d => d.Elements.ContainsKey("/S")))
        {
            yield return child.Elements.GetName("/S");
            foreach (var type in StructureTypes(child))
                yield return type;
        }
    }

    [Fact]
    public void Headings_and_paragraphs_are_tagged_and_headers_and_footers_are_not()
    {
        using var pdf = RenderAndReopen(Invoice());

        var root = pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!;
        Assert.Equal(new[] { "/Document", "/H1", "/P", "/H2" }, StructureTypes(root));
        Assert.Equal("nb-NO", pdf.Catalog.Elements.GetString("/Lang"));
        Assert.True(pdf.Catalog.Elements.GetDictionary("/ViewerPreferences")!.Elements.GetBoolean("/DisplayDocTitle"));
        var xmp = System.Text.Encoding.UTF8.GetString(pdf.Catalog.Elements.GetDictionary("/Metadata")!.Stream!.Value);
        Assert.Contains("<pdfuaid:part>1</pdfuaid:part>", xmp);
    }

    [Fact]
    public void A_tagged_document_needs_a_title()
    {
        var renderer = new PdfDocumentRenderer { Document = Invoice(title: ""), TaggedPdf = true };

        var refused = Assert.Throws<InvalidOperationException>(renderer.RenderDocument);
        Assert.Contains("title", refused.Message);
    }

    [Fact]
    public void Untagged_rendering_is_unchanged()
    {
        var renderer = new PdfDocumentRenderer { Document = Invoice() };
        renderer.RenderDocument();
        using var saved = new MemoryStream();
        renderer.Save(saved, false);

        using var pdf = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.False(pdf.Catalog.Elements.ContainsKey("/StructTreeRoot"));
    }
}
