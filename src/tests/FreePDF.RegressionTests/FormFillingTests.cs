// Filling AcroForms through the v7 forms model: lookup by name, typed values, appearances. Issue #9.
// Original fixtures (FormFixture); MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class FormFillingTests
{
    static PdfDocument Open() => PdfReader.Open(new MemoryStream(FormFixture.Create()), PdfDocumentOpenMode.Modify);

    [Fact]
    public void Fields_are_found_by_their_full_name()
    {
        using var document = Open();
        var fields = document.Catalog.GetAcroForm()!.Fields;

        Assert.Equal(6, fields.Count);
        Assert.Equal("name", fields["name"]!.Name);
        Assert.Equal("address.city", fields["address.city"]!.FullName);
        Assert.Null(fields["missing"]);
        Assert.Null(fields["address.missing"]);
        Assert.True(fields.TryGetField("size", out var size));
        Assert.Equal("size", size!.Name);
    }

    [Fact]
    public void Fields_get_the_type_they_inherit_from_their_parent()
    {
        using var document = Open();
        var fields = document.Catalog.GetAcroForm()!.Fields;

        Assert.IsType<PdfFormTextField>(fields["address.city"]);
        Assert.IsType<PdfFormTextField>(fields["name"]);
        Assert.IsType<PdfFormCheckBoxField>(fields["agree"]);
        Assert.IsType<PdfFormRadioButtonField>(fields["size"]);
        Assert.IsType<PdfFormComboBoxField>(fields["country"]);
        Assert.IsType<PdfFormListBoxField>(fields["colour"]);
    }

    static PdfDocument SaveAndReopen(PdfDocument document)
    {
        using var saved = new MemoryStream();
        document.Save(saved);
        return PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Modify);
    }

    static PdfFormFields Fields(PdfDocument document) => document.Catalog.GetAcroForm()!.Fields;

    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("Åse Øyen – Zoë 日本")]
    public void Text_fields_take_their_text(string text)
    {
        using var document = Open();
        var name = (PdfFormTextField)Fields(document)["name"]!;
        Assert.Equal("Old name", name.Text);
        name.Text = text;
        ((PdfFormTextField)Fields(document)["address.city"]!).Text = "Kristiansand";

        using var reopened = SaveAndReopen(document);
        Assert.Equal(text, ((PdfFormTextField)Fields(reopened)["name"]!).Text);
        Assert.Equal("Kristiansand", ((PdfFormTextField)Fields(reopened)["address.city"]!).Text);
    }

    [Fact]
    public void A_check_box_uses_the_on_state_its_appearance_defines()
    {
        using var document = Open();
        var agree = (PdfFormCheckBoxField)Fields(document)["agree"]!;
        Assert.False(agree.Checked);
        agree.Checked = true;
        Assert.Equal("/Ja", agree.Elements.GetName("/V"));
        Assert.Equal("/Ja", agree.Elements.GetName("/AS"));

        using var reopened = SaveAndReopen(document);
        var reread = (PdfFormCheckBoxField)Fields(reopened)["agree"]!;
        Assert.True(reread.Checked);
        reread.Checked = false;
        Assert.Equal("/Off", reread.Elements.GetName("/V"));
        Assert.Equal("/Off", reread.Elements.GetName("/AS"));
    }

    [Fact]
    public void A_radio_group_selects_one_button_by_index_or_value()
    {
        using var document = Open();
        var size = (PdfFormRadioButtonField)Fields(document)["size"]!;
        Assert.Equal(new[] { "Small", "Large" }, size.Options);
        Assert.Equal(-1, size.SelectedIndex);
        size.SelectedValue = "Large";

        using var reopened = SaveAndReopen(document);
        var reread = (PdfFormRadioButtonField)Fields(reopened)["size"]!;
        Assert.Equal(1, reread.SelectedIndex);
        Assert.Equal("/Large", reread.Elements.GetName("/V"));
        var states = reread.GetKids().Select(kid => kid.Elements.GetName("/AS")).ToArray();
        Assert.Equal(new[] { "/Off", "/Large" }, states);
        reread.SelectedIndex = 0;
        Assert.Equal(new[] { "/Small", "/Off" }, reread.GetKids().Select(kid => kid.Elements.GetName("/AS")));
    }

    [Fact]
    public void Choice_fields_select_by_index_or_export_value()
    {
        using var document = Open();
        var country = (PdfFormComboBoxField)Fields(document)["country"]!;
        Assert.Equal(new[] { "Norway", "Sweden", "Denmark" }, country.Options);
        Assert.Equal(new[] { "NO", "SE", "DK" }, country.ExportValues);
        Assert.Equal(0, country.SelectedIndex);
        country.SelectedValue = "DK";
        var colour = (PdfFormListBoxField)Fields(document)["colour"]!;
        Assert.Equal(-1, colour.SelectedIndex);
        colour.SelectedIndex = 1;

        using var reopened = SaveAndReopen(document);
        Assert.Equal(2, ((PdfFormComboBoxField)Fields(reopened)["country"]!).SelectedIndex);
        var rereadColour = (PdfFormListBoxField)Fields(reopened)["colour"]!;
        Assert.Equal("Green", rereadColour.SelectedValue);
        Assert.Throws<ArgumentException>(() => rereadColour.SelectedValue = "Purple");
    }

    // ISO 32000-1 Table 231: /I is for fields with multiple selection or options that share an export value, and
    // "should not be used" otherwise. Writing it changed the field's keys, which pyHanko rejects after a signature.
    [Fact]
    public void A_single_choice_field_gets_no_selected_indices()
    {
        using var document = Open();
        ((PdfFormComboBoxField)Fields(document)["country"]!).SelectedValue = "DK";
        ((PdfFormListBoxField)Fields(document)["colour"]!).SelectedIndex = 1;

        using var reopened = SaveAndReopen(document);
        Assert.False(Fields(reopened)["country"]!.Elements.ContainsKey("/I"));
        Assert.False(Fields(reopened)["colour"]!.Elements.ContainsKey("/I"));
    }

    static PdfDocument OpenChoice(string field)
        => PdfReader.Open(new MemoryStream(TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R] >>",
            "<< /Fields [5 0 R] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 6 0 R >> >> >>",
            field,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        })), PdfDocumentOpenMode.Modify);

    const string ChoiceWidget = "/DA (/Helv 10 Tf 0 g) /Type /Annot /Subtype /Widget /Rect [20 180 160 240] /P 3 0 R /F 4";

    [Fact]
    public void A_multiple_selection_list_keeps_selected_indices()
    {
        using var document = OpenChoice($"<< /FT /Ch /Ff 2097152 /T (colour) /Opt [(Red) (Green) (Blue)] {ChoiceWidget} >>");
        ((PdfFormListBoxField)Fields(document)["colour"]!).SelectedIndex = 2;

        using var reopened = SaveAndReopen(document);
        Assert.Equal(2, Fields(reopened)["colour"]!.Elements.GetArray("/I")!.Elements.GetInteger(0));
    }

    [Fact]
    public void Options_with_the_same_export_value_are_told_apart_by_their_index()
    {
        using var document = OpenChoice($"<< /FT /Ch /T (size) /Opt [[(M) (Medium)] [(L) (Large)] [(L) (Large, long)]] {ChoiceWidget} >>");
        ((PdfFormListBoxField)Fields(document)["size"]!).SelectedIndex = 2;

        using var reopened = SaveAndReopen(document);
        var size = (PdfFormListBoxField)Fields(reopened)["size"]!;
        Assert.Equal(2, size.Elements.GetArray("/I")!.Elements.GetInteger(0));
        Assert.Equal(2, size.SelectedIndex);
        Assert.Equal("L", size.SelectedValue);
    }

    [Fact]
    public void Setting_the_selected_value_again_keeps_the_option_that_shares_it()
    {
        using var document = OpenChoice($"<< /FT /Ch /T (size) /Opt [[(M) (Medium)] [(L) (Large)] [(L) (Large, long)]] /V (L) /I [2] {ChoiceWidget} >>");
        var size = (PdfFormListBoxField)Fields(document)["size"]!;
        Assert.Equal(2, size.SelectedIndex);

        size.SelectedValue = size.SelectedValue;

        Assert.Equal(2, size.SelectedIndex);
        Assert.Equal(2, size.Elements.GetArray("/I")!.Elements.GetInteger(0));
    }

    [Fact]
    public void Selected_indices_the_field_has_are_kept_up_to_date()
    {
        using var document = OpenChoice($"<< /FT /Ch /T (colour) /Opt [(Red) (Green) (Blue)] /V (Red) /I [0] {ChoiceWidget} >>");
        ((PdfFormListBoxField)Fields(document)["colour"]!).SelectedIndex = 1;

        using var reopened = SaveAndReopen(document);
        Assert.Equal(1, Fields(reopened)["colour"]!.Elements.GetArray("/I")!.Elements.GetInteger(0));
    }

    [Fact]
    public void A_read_only_field_refuses_new_values()
    {
        using var document = Open();
        var name = (PdfFormTextField)Fields(document)["name"]!;
        name.IsReadOnly = true;

        var refused = Assert.Throws<InvalidOperationException>(() => name.Text = "New");
        Assert.Contains("name", refused.Message);
    }

    static string NormalAppearance(PdfDictionary widget)
    {
        var appearance = widget.Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!;
        Assert.Equal("/Form", appearance.Elements.GetName("/Subtype"));
        return TestPdf.Latin1.GetString(appearance.Stream!.UnfilteredValue);
    }

    [Fact]
    public void Filling_a_text_field_draws_its_appearance_with_the_default_appearance_font_and_colour()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "Ada (Countess) Lovelace";

        using var reopened = SaveAndReopen(document);
        var name = Fields(reopened)["name"]!;
        var stream = NormalAppearance(name);
        Assert.Contains("/Tx BMC", stream);
        Assert.Contains("/Helv 12 Tf 0 0 1 rg", stream);
        Assert.Contains(@"(Ada \(Countess\) Lovelace) Tj", stream);
        var appearance = name.Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!;
        Assert.NotNull(appearance.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/Helv"));
        Assert.False(reopened.Catalog.GetAcroForm()!.NeedAppearances);
    }

    [Fact]
    public void Choice_fields_draw_the_selected_text_or_the_highlighted_list()
    {
        using var document = Open();
        ((PdfFormComboBoxField)Fields(document)["country"]!).SelectedValue = "DK";
        ((PdfFormListBoxField)Fields(document)["colour"]!).SelectedIndex = 1;

        using var reopened = SaveAndReopen(document);
        Assert.Contains("(Denmark) Tj", NormalAppearance(Fields(reopened)["country"]!));
        var list = NormalAppearance(Fields(reopened)["colour"]!);
        Assert.Contains("(Red) Tj", list);
        Assert.Contains("(Blue) Tj", list);
        Assert.Contains("0.6 0.75 0.86 rg", list);
    }

    [Fact]
    public void Norwegian_letters_are_drawn_in_the_WinAnsi_encoding()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "Åse Ærø";

        Assert.Contains("(\u00c5se \u00c6r\u00f8) Tj", NormalAppearance(Fields(document)["name"]!));
        Assert.False(document.Catalog.GetAcroForm()!.NeedAppearances);
    }

    [Fact]
    public void Text_the_form_font_cannot_encode_is_left_to_the_viewer()
    {
        using var document = Open();
        ((PdfFormTextField)Fields(document)["name"]!).Text = "日本";

        Assert.True(document.Catalog.GetAcroForm()!.NeedAppearances);
    }

    // The name field is 200 wide with a 2 inset and 12 pt text, so right-aligned text starts at 198 minus its width.
    // Widths in 1/1000 em from Adobe's AFM files: Helvetica A 667, d 556, a 556, Aring 667, s 500, e 556;
    // Helvetica-Bold A 722, d 611, a 556; Times-Roman A 722, d 500, a 444. A font without metrics counts 500 a character.
    [Theory]
    [InlineData("/Helvetica", "Ada", 198 - 1779 * 0.012)]
    [InlineData("/Helvetica", "Åse", 198 - 1723 * 0.012)]
    [InlineData("/Arial,Bold", "Ada", 198 - 1889 * 0.012)]
    [InlineData("/ABCDEF+TimesNewRomanPSMT", "Ada", 198 - 1666 * 0.012)]
    [InlineData("/SomeOtherFont", "Ada", 198 - 1500 * 0.012)]
    public void Right_aligned_text_is_placed_with_the_standard_font_widths(string baseFont, string text, double expectedX)
    {
        using var document = Open();
        var form = document.Catalog.GetAcroForm()!;
        var font = form.Elements.GetDictionary("/DR")!.Elements.GetDictionary("/Font")!.Elements.GetDictionary("/Helv")!;
        font.Elements.SetName("/BaseFont", baseFont);
        var name = (PdfFormTextField)Fields(document)["name"]!;
        name.Elements.SetInteger("/Q", 2);
        name.Text = text;

        var position = System.Text.RegularExpressions.Regex.Match(NormalAppearance(name), @"(\S+) \S+ Td");
        Assert.True(position.Success);
        double x = double.Parse(position.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expectedX, x, 3);
    }
}
