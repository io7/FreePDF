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
    public void A_long_table_stays_one_table_and_its_repeated_heading_rows_are_artifacts()
    {
        using var pdf = RenderAndReopen(InvoiceWithTable(rows: 80));

        Assert.True(pdf.PageCount > 1);
        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).ToArray();
        Assert.Equal(1, types.Count(t => t == "/Table"));
        Assert.Equal(81, types.Count(t => t == "/TR"));
        Assert.Equal(2, types.Count(t => t == "/TH"));
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

    static PdfDictionary[] Children(PdfDictionary element)
        => (element.Elements.GetValue("/K") switch
        {
            PdfArray array => array.Elements.Select(e => (e as PdfSharp.Pdf.Advanced.PdfReference)?.Value ?? e),
            PdfSharp.Pdf.Advanced.PdfReference reference => new[] { reference.Value },
            PdfDictionary dictionary => new PdfItem[] { dictionary },
            _ => Array.Empty<PdfItem>(),
        }).OfType<PdfDictionary>().Where(d => d.Elements.ContainsKey("/S")).ToArray();

    static PdfDictionary Child(PdfDictionary element, string type, int index = 0)
        => Children(element).Where(c => c.Elements.GetName("/S") == type).ElementAt(index);

    static void AddItem(Section section, string text, ListType type)
    {
        var item = section.AddParagraph(text);
        item.Format.ListInfo.ListType = type;
        item.Format.LeftIndent = "1cm";
    }

    [Fact]
    public void Deeper_list_levels_nest_inside_the_item_before_them()
    {
        var document = Invoice();
        var section = document.LastSection;
        AddItem(section, "A", ListType.BulletList1);
        AddItem(section, "A.1", ListType.BulletList2);
        AddItem(section, "A.2", ListType.BulletList2);
        AddItem(section, "B", ListType.BulletList1);
        section.AddParagraph("After the list");

        using var pdf = RenderAndReopen(document);

        var root = Child(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!, "/Document");
        var list = Child(root, "/L");
        Assert.Equal(2, Children(list).Length);
        var nested = Child(Child(Child(list, "/LI", 0), "/LBody"), "/L");
        Assert.Equal(2, Children(nested).Count(c => c.Elements.GetName("/S") == "/LI"));
        Assert.Equal(new[] { "/H1", "/P", "/H2", "/L", "/P" }, Children(root).Select(c => c.Elements.GetName("/S")));
    }

    [Theory]
    [InlineData(ListType.BulletList3)]
    [InlineData(ListType.NumberList2)]
    public void A_nested_list_at_the_end_of_a_run_closes_back_to_the_document(ListType deeper)
    {
        var document = Invoice();
        var section = document.LastSection;
        AddItem(section, "A", ListType.BulletList1);
        AddItem(section, "A.1", deeper);
        section.AddParagraph("After the list");

        using var pdf = RenderAndReopen(document);

        var root = Child(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!, "/Document");
        Assert.Equal(new[] { "/H1", "/P", "/H2", "/L", "/P" }, Children(root).Select(c => c.Elements.GetName("/S")));
        var nested = Child(Child(Child(Child(root, "/L"), "/LI"), "/LBody"), "/L");
        Assert.Single(Children(nested));
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

    [Fact]
    public void Hyperlinks_are_link_elements_that_own_their_annotations()
    {
        var document = Invoice();
        var paragraph = document.LastSection.AddParagraph("Pay at ");
        paragraph.AddHyperlink("https://example.com/pay", HyperlinkType.Web).AddText("example.com");
        paragraph.AddText(" today.");

        using var pdf = RenderAndReopen(document);

        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).ToArray();
        Assert.Equal(1, types.Count(t => t == "/Link"));
        var annotation = Assert.Single(pdf.Pages[0].Annotations.OfType<PdfSharp.Pdf.Annotations.PdfLinkAnnotation>());
        Assert.Equal("https://example.com/pay", annotation.Elements.GetString("/Contents"));
        Assert.True(annotation.Elements.ContainsKey("/StructParent"));
        Assert.Equal("/S", pdf.Pages[0].Elements.GetName("/Tabs"));
    }

    static MigraDoc.DocumentObjectModel.Shapes.Charts.Chart AddChart(Section section)
    {
        var chart = section.AddChart(MigraDoc.DocumentObjectModel.Shapes.Charts.ChartType.Column2D);
        chart.Width = "8cm";
        chart.Height = "4cm";
        chart.SeriesCollection.AddSeries().Add(10, 14, 9);
        chart.XValues.AddXSeries().Add("Jan", "Feb", "Mar");
        chart.XAxis.MajorTickMark = MigraDoc.DocumentObjectModel.Shapes.Charts.TickMarkType.Outside;
        chart.YAxis.MajorTickMark = MigraDoc.DocumentObjectModel.Shapes.Charts.TickMarkType.Outside;
        return chart;
    }

    [Fact]
    public void A_chart_is_one_figure_and_a_text_frame_keeps_its_paragraphs()
    {
        var document = Invoice();
        AddChart(document.LastSection).AlternativeText = "Sales by month";
        var frame = document.LastSection.AddTextFrame();
        frame.Width = "6cm";
        frame.Height = "2cm";
        frame.LineFormat.Width = 1;
        frame.AddParagraph("Framed note");

        using var pdf = RenderAndReopen(document);

        var types = StructureTypes(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!).ToArray();
        Assert.Equal(new[] { "/Figure", "/P" }, types.SkipWhile(t => t != "/Figure"));
    }

    [Fact]
    public void A_chart_without_alternative_text_stops_tagged_rendering()
    {
        var document = Invoice();
        AddChart(document.LastSection);
        var renderer = new PdfDocumentRenderer { Document = document, TaggedPdf = true };

        var refused = Assert.Throws<InvalidOperationException>(renderer.RenderDocument);
        Assert.Contains("Chart has no alternative text", refused.Message);
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
    public void A_clone_of_a_rendered_document_can_be_rendered_again()
    {
        // Document.Clone kept the original's renderer binding, although the binding error tells users to clone.
        var document = Invoice();
        new PdfDocumentRenderer { Document = document }.RenderDocument();

        var copy = document.Clone();
        var renderer = new PdfDocumentRenderer { Document = copy, TaggedPdf = true };
        renderer.RenderDocument();
        Assert.True(renderer.PdfDocument.PageCount > 0);
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
