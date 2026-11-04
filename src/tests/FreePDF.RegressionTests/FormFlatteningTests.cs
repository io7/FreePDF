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
}
