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
        Assert.Equal(1, rereadColour.Elements.GetArray("/I")!.Elements.GetInteger(0));
        Assert.Throws<ArgumentException>(() => rereadColour.SelectedValue = "Purple");
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
}
