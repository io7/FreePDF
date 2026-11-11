// Flattening AcroForms: widgets drawn into the page content, then the form removed. Original fixture (FormFixture);
// MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class FormFlatteningTests
{
    static PdfDocument Open() => PdfReader.Open(new MemoryStream(FormFixture.Create()), PdfDocumentOpenMode.Modify);

    static PdfDocument SaveAndReopen(PdfDocument document)
    {
        var stream = new MemoryStream();
        document.Save(stream, false);
        return PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
    }

    static PdfFormFields Fields(PdfDocument document) => document.Catalog.GetAcroForm()!.Fields;

    static string PageContent(PdfDocument document)
        => TestPdf.Latin1.GetString(document.Pages[0].Contents.CreateSingleContent().Stream!.UnfilteredValue);

    /// <summary>
    /// The content of every form XObject the page draws, in drawing order.
    /// </summary>
    static List<string> DrawnForms(PdfDocument document)
    {
        var xObjects = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        var drawn = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(PageContent(document), @"(/FreePDFField\d+) Do"))
            drawn.Add(TestPdf.Latin1.GetString(xObjects.Elements.GetDictionary(match.Groups[1].Value)!.Stream!.UnfilteredValue));
        return drawn;
    }

    [Fact]
    public void Flattening_draws_the_filled_values_into_the_page_and_removes_the_form()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "Ada Lovelace";
        ((PdfFormCheckBoxField)Fields(document)["agree"]!).Checked = true;
        ((PdfFormComboBoxField)Fields(document)["country"]!).SelectedValue = "SE";

        document.Flatten();
        using var reopened = SaveAndReopen(document);

        Assert.Null(reopened.Catalog.GetAcroForm());
        Assert.Null(reopened.Pages[0].Elements.GetArray("/Annots"));
        var drawn = DrawnForms(reopened);
        Assert.Contains(drawn, content => content.Contains("(Ada Lovelace) Tj"));
        Assert.Contains(drawn, content => content.Contains("(Sweden) Tj"));
        Assert.Contains(drawn, content => content.Contains("q 0 g 2 2 10 10 re f Q"));
    }

    // A list box that allows several selected options holds them as an array of export values (ISO 32000-1 Table
    // 231). Flattening threw; the drawn list now highlights every selected option.
    [Fact]
    public void A_list_box_with_several_selected_options_is_flattened_with_all_of_them_highlighted()
    {
        using var document = Open();
        var colour = (PdfFormListBoxField)Fields(document)["colour"]!;
        colour.Elements.SetInteger("/Ff", 1 << 21);
        colour.Elements["/V"] = new PdfArray(document, new PdfString("Red"), new PdfString("Blue"));
        document.Catalog.GetAcroForm()!.NeedAppearances = true;

        Assert.Equal("Red", colour.SelectedValue);
        Assert.Equal(0, colour.SelectedIndex);
        document.Flatten();
        using var reopened = SaveAndReopen(document);

        var list = Assert.Single(DrawnForms(reopened), content => content.Contains("(Green) Tj"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(list, "0.6 0.75 0.86 rg").Count);
    }

    // Review of #47: a value held as an indirect string was ignored, and with /V in another order than /Opt the
    // value and the index named different options.
    [Fact]
    public void List_box_values_held_as_indirect_strings_or_in_another_order_are_read()
    {
        using var document = Open();
        var colour = (PdfFormListBoxField)Fields(document)["colour"]!;
        colour.Elements.SetInteger("/Ff", 1 << 21);
        // The constructor makes the string an indirect object of the document.
        var green = new PdfStringObject(document, "Green");
        colour.Elements["/V"] = green.Reference;
        Assert.Equal("Green", colour.SelectedValue);
        Assert.Equal(1, colour.SelectedIndex);

        var red = new PdfStringObject(document, "Red");
        colour.Elements["/V"] = new PdfArray(document, new PdfString("Blue"), red.Reference!);
        Assert.Equal("Red", colour.SelectedValue);
        Assert.Equal(0, colour.SelectedIndex);
    }

    [Fact]
    public void Each_widget_is_placed_on_its_rectangle()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "Ada";

        document.Flatten();

        // The name widget's rectangle is [20 350 220 370]; its appearance box is [0 0 200 20].
        Assert.Contains("q 1 0 0 1 20 350 cm /FreePDFField", PageContent(document));
    }

    [Fact]
    public void The_page_content_is_wrapped_so_its_graphics_state_does_not_reach_the_fields()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "Ada";

        document.Flatten();

        var content = PageContent(document);
        Assert.StartsWith("q\n", content.TrimStart('\n'));
        Assert.Contains("\nQ\nq 1 0 0 1 20 350 cm", content);
    }

    [Fact]
    public void A_value_written_without_an_appearance_is_drawn_when_flattening()
    {
        using var document = Open();
        // As a 6.2.4 program did: write /V directly and ask viewers to draw it.
        var name = Fields(document)["name"]!;
        name.Elements["/V"] = new PdfString("Written directly");
        document.Catalog.GetAcroForm()!.NeedAppearances = true;

        document.Flatten();

        Assert.Contains(DrawnForms(document), content => content.Contains("(Written directly) Tj"));
    }

    [Fact]
    public void A_value_that_cannot_be_drawn_stops_flattening_without_changing_the_document()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "日本";

        var refused = Assert.Throws<InvalidOperationException>(() => document.Flatten());

        Assert.Contains("'name'", refused.Message);
        Assert.NotNull(document.Catalog.GetAcroForm());
        Assert.NotNull(document.Pages[0].Elements.GetArray("/Annots"));
    }

    [Fact]
    public void Hidden_widgets_are_not_drawn_and_other_annotations_are_kept()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "Hidden value";
        Fields(document)["name"]!.Elements.SetInteger("/F", 2);
        var note = new PdfDictionary(document);
        note.Elements.SetName("/Type", "/Annot");
        note.Elements.SetName("/Subtype", "/Text");
        note.Elements["/Rect"] = new PdfArray(document, new PdfInteger(300), new PdfInteger(300), new PdfInteger(320), new PdfInteger(320));
        document.Internals.AddObject(note);
        document.Pages[0].Elements.GetArray("/Annots")!.Elements.Add(note.Reference!);

        document.Flatten();
        using var reopened = SaveAndReopen(document);

        Assert.DoesNotContain(DrawnForms(reopened), content => content.Contains("Hidden value"));
        var annotations = reopened.Pages[0].Elements.GetArray("/Annots")!;
        Assert.Equal(1, annotations.Elements.Count);
        Assert.Equal("/Text", annotations.Elements.GetDictionary(0)!.Elements.GetName("/Subtype"));
    }

    [Fact]
    public void A_document_without_a_form_is_left_as_it_is()
    {
        using var document = new PdfDocument();
        document.AddPage();

        document.Flatten();

        Assert.Null(document.Catalog.GetAcroForm());
    }

    // A page whose content is Flate applied twice (a few kilobytes for 16 MiB): flattening opened the page's content and
    // so decoded it whole, which a few kilobytes more could make gigabytes. The new streams go around it; it stays encoded.
    [Fact]
    public void Flattening_does_not_decode_the_page_content()
    {
        byte[] Zlib(byte[] data)
        {
            using var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0xDA);
            using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                deflate.Write(data, 0, data.Length);
            output.Write(new byte[4], 0, 4);
            return output.ToArray();
        }
        var content = Zlib(Zlib(new byte[16 * 1024 * 1024]));
        var text = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>\nendobj\n"
                   + "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
                   + "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [4 0 R] /Contents 6 0 R >>\nendobj\n"
                   + "4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Rect [10 10 110 30] /P 3 0 R /AP << /N 5 0 R >> >>\nendobj\n"
                   + "5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 100 20] /Length 15 >>\nstream\n0 0 100 20 re f\nendstream\nendobj\n"
                   + $"6 0 obj\n<< /Filter [/FlateDecode /FlateDecode] /Length {content.Length} >>\nstream\n";
        var objects = new List<int>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^\d 0 obj"))
            objects.Add(match.Index);
        using var file = new MemoryStream();
        void Write(byte[] bytes) => file.Write(bytes, 0, bytes.Length);
        Write(TestPdf.Latin1.GetBytes(text));
        Write(content);
        Write(TestPdf.Latin1.GetBytes("\nendstream\nendobj\n"));
        long xref = file.Position;
        var table = "xref\n0 7\n0000000000 65535 f \n" + string.Concat(objects.Select(offset => $"{offset:D10} 00000 n \n"))
                    + $"trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n";
        Write(TestPdf.Latin1.GetBytes(table));
        using var document = PdfReader.Open(new MemoryStream(file.ToArray()), PdfDocumentOpenMode.Modify);

        document.Flatten();

        var original = document.Internals.GetObject(new PdfObjectID(6)) as PdfDictionary;
        Assert.NotNull(original?.Elements["/Filter"]);
        Assert.Equal(content.Length, original!.Stream!.Value.Length);
        Assert.Equal(3, document.Pages[0].Elements.GetArray("/Contents")!.Elements.Count);
    }

    // An appearance stream without /Subtype becomes a form XObject when it is drawn into the page: the check for a missing
    // /Subtype compared GetName's result with "", but GetName gives "/" for a missing entry.
    [Fact]
    public void An_appearance_without_a_subtype_is_drawn_as_a_form_XObject()
    {
        var pdf = TestPdf.Create(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [4 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Rect [10 10 110 30] /P 3 0 R /AP << /N 5 0 R >> >>",
            "<< /BBox [0 0 100 20] /Length 15 >>\nstream\n0 0 100 20 re f\nendstream",
        });
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);

        document.Flatten();

        var xObjects = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        Assert.Equal("/Form", xObjects.Elements.GetDictionary("/FreePDFField0")!.Elements.GetName("/Subtype"));
    }
}
