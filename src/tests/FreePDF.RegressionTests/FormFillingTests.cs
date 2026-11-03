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
}
