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

    static Document InvoiceWithTable(int rows)
    {
        var document = Invoice();
        var table = document.LastSection.AddTable();
        table.Borders.Width = 0.5;
        table.AddColumn("4cm");
        table.AddColumn("3cm");
        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Cells[0].AddParagraph("Item");
        head.Cells[1].AddParagraph("Amount");
        for (var r = 0; r < rows; r++)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph($"Item {r + 1}");
            row.Cells[1].AddParagraph("10");
        }
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
    public void Tables_are_tagged_with_rows_header_cells_and_data_cells()
    {
        using var pdf = RenderAndReopen(InvoiceWithTable(rows: 1));

        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).ToArray();
        Assert.Equal(new[] { "/Table", "/TR", "/TH", "/P", "/TH", "/P", "/TR", "/TD", "/P", "/TD", "/P" }, types.SkipWhile(t => t != "/Table"));
    }

    [Fact]
    public void Each_page_of_a_long_table_repeats_its_header_cells()
    {
        using var pdf = RenderAndReopen(InvoiceWithTable(rows: 80));

        Assert.True(pdf.PageCount > 1);
        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).ToArray();
        Assert.Equal(pdf.PageCount, types.Count(t => t == "/Table"));
        Assert.Equal(pdf.PageCount * 2, types.Count(t => t == "/TH"));
    }

    static void AddList(Section section, ListType type, int items)
    {
        for (var i = 0; i < items; i++)
        {
            var item = section.AddParagraph($"Item {i + 1}");
            item.Format.ListInfo.ListType = type;
            item.Format.LeftIndent = "1cm";
        }
    }

    [Fact]
    public void Consecutive_list_paragraphs_form_one_list_of_labelled_items()
    {
        var document = Invoice();
        AddList(document.LastSection, ListType.BulletList1, 2);
        document.LastSection.AddParagraph("Between the lists");
        AddList(document.LastSection, ListType.NumberList1, 1);

        using var pdf = RenderAndReopen(document);

        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).SkipWhile(t => t != "/L").ToArray();
        Assert.Equal(new[] {
            "/L", "/LI", "/Lbl", "/LBody", "/LI", "/Lbl", "/LBody",
            "/P",
            "/L", "/LI", "/Lbl", "/LBody" }, types);
    }

    [Fact]
    public void A_list_that_crosses_a_page_break_stays_one_list()
    {
        var document = Invoice();
        AddList(document.LastSection, ListType.BulletList1, 80);

        using var pdf = RenderAndReopen(document);

        Assert.True(pdf.PageCount > 1);
        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).ToArray();
        Assert.Equal(1, types.Count(t => t == "/L"));
        Assert.Equal(80, types.Count(t => t == "/LI"));
    }

    // A 2 x 2 pixel 8-bit RGB PNG, as MigraDoc's base64 image name.
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVQI12N0a9rCwMDAxMDAwMDAAAARjAGAcK5hlwAAAABJRU5ErkJggg==";

    [Fact]
    public void An_image_with_alternative_text_is_a_figure_and_a_decorative_one_is_an_artifact()
    {
        var document = Invoice();
        var logo = document.LastSection.AddImage(Pixel);
        logo.AlternativeText = "Company logo";
        document.LastSection.AddImage(Pixel).Decorative = true;

        using var pdf = RenderAndReopen(document);

        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).ToArray();
        Assert.Equal(1, types.Count(t => t == "/Figure"));
    }

    [Fact]
    public void An_image_without_alternative_text_stops_tagged_rendering()
    {
        var document = Invoice();
        document.LastSection.AddImage(Pixel);
        var renderer = new PdfDocumentRenderer { Document = document, TaggedPdf = true };

        var refused = Assert.Throws<InvalidOperationException>(renderer.RenderDocument);
        Assert.Contains("AlternativeText", refused.Message);
    }

    [Fact]
    public void Alternative_text_and_decorative_survive_DDL_serialization()
    {
        var document = Invoice();
        var logo = document.LastSection.AddImage(Pixel);
        logo.AlternativeText = "Company logo";
        logo.Decorative = true;

        var ddl = MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document);
        var copy = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(ddl);

        var image = copy.LastSection.Elements.OfType<MigraDoc.DocumentObjectModel.Shapes.Image>().Single();
        Assert.Equal("Company logo", image.AlternativeText);
        Assert.True(image.Decorative);
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
