// Creating form fields (7.9.0): PdfForm.AddTextField, AddCheckBox, AddRadioButtonGroup, AddComboBox, AddListBox and
// AddSignatureField. Each field has its widget on the page, an appearance, and survives saving, reopening, filling,
// flattening, merging and signing. Original fixtures and a self-signed test identity; MIT, see LICENSE.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.PdfA;
using PdfSharp.Pdf.Security;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class FormCreationTests
{
    public FormCreationTests() => TestFonts.Initialize();

    // A4 portrait: 595 x 842 points.
    static PdfDocument NewDocumentWithForm()
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(595);
        page.Height = XUnit.FromPoint(842);
        var form = document.Catalog.GetOrCreateAcroForm();
        form.AddTextField("name", page, new XRect(100, 50, 200, 20), new PdfFormFieldStyle { FontSize = 10, ToolTip = "Name" });
        form.AddCheckBox("agree", page, new XRect(100, 100, 14, 14));
        form.AddRadioButtonGroup("size", page, new[] { ("S", new XRect(100, 150, 12, 12)), ("M", new XRect(130, 150, 12, 12)) });
        form.AddComboBox("country", page, new XRect(100, 200, 150, 20), new[] { "Norway", "Sweden" });
        form.AddListBox("items", page, new XRect(100, 250, 150, 50), new[] { "Apples", "Pears", "Plums" });
        form.AddSignatureField("signature", page, new XRect(100, 350, 200, 50));
        return document;
    }

    static PdfDocument SaveAndOpen(PdfDocument document, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Modify)
    {
        var bytes = Save(document);
        return PdfReader.Open(new MemoryStream(bytes), mode);
    }

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    [Fact]
    public void Created_fields_are_read_back_with_their_types_and_widgets()
    {
        using var reopened = SaveAndOpen(NewDocumentWithForm());

        var fields = reopened.Catalog.GetAcroForm()!.Fields;
        Assert.IsType<PdfFormTextField>(fields["name"]);
        Assert.IsType<PdfFormCheckBoxField>(fields["agree"]);
        Assert.IsType<PdfFormRadioButtonField>(fields["size"]);
        Assert.IsType<PdfFormComboBoxField>(fields["country"]);
        Assert.IsType<PdfFormListBoxField>(fields["items"]);
        Assert.IsType<PdfFormSignatureField>(fields["signature"]);

        // Every widget is on the page and has a normal appearance: 5 fields with one widget, 2 radio buttons.
        var annots = reopened.Pages[0].Elements.GetArray("/Annots")!;
        Assert.Equal(7, annots.Elements.Count);
        foreach (var item in annots.Elements)
        {
            var widget = (PdfDictionary)((PdfReference)item).Value;
            Assert.Equal("/Widget", widget.Elements.GetName("/Subtype"));
            Assert.NotNull(widget.Elements.GetDictionary("/AP"));
            Assert.Equal(4, widget.Elements.GetInteger("/F"));
        }
    }

    // Drawing coordinates (top left origin) become PDF coordinates: a 200 x 20 box at (100, 50) on an 842 pt page
    // spans y from 772 to 792.
    [Fact]
    public void The_rectangle_is_converted_from_drawing_coordinates()
    {
        using var reopened = SaveAndOpen(NewDocumentWithForm());

        var rect = reopened.Catalog.GetAcroForm()!.Fields["name"]!.Elements.GetRectangle("/Rect")!;
        Assert.Equal(100, rect.X1, 3);
        Assert.Equal(842 - 70, rect.Y1, 3);
        Assert.Equal(300, rect.X2, 3);
        Assert.Equal(842 - 50, rect.Y2, 3);
    }

    [Fact]
    public void Created_fields_can_be_filled_after_reopening()
    {
        using var reopened = SaveAndOpen(NewDocumentWithForm());
        var fields = reopened.Catalog.GetAcroForm()!.Fields;

        ((PdfFormTextField)fields["name"]!).Text = "Kari Nordmann";
        ((PdfFormCheckBoxField)fields["agree"]!).Checked = true;
        ((PdfFormRadioButtonField)fields["size"]!).SelectedValue = "M";
        ((PdfFormComboBoxField)fields["country"]!).SelectedValue = "Sweden";
        ((PdfFormListBoxField)fields["items"]!).SelectedValue = "Plums";
        using var filled = SaveAndOpen(reopened);

        var again = filled.Catalog.GetAcroForm()!.Fields;
        Assert.Equal("Kari Nordmann", ((PdfFormTextField)again["name"]!).Text);
        Assert.True(((PdfFormCheckBoxField)again["agree"]!).Checked);
        Assert.Equal("M", ((PdfFormRadioButtonField)again["size"]!).SelectedValue);
        Assert.Equal("Sweden", ((PdfFormComboBoxField)again["country"]!).SelectedValue);
        Assert.Equal("Plums", ((PdfFormListBoxField)again["items"]!).SelectedValue);
        Assert.Contains("(Kari Nordmann) Tj", AppearanceText(again["name"]!));
    }

    // Check boxes and radio buttons get on and off appearances (no font needed); the radio buttons' on states are
    // their values, and each button is a kid of the group.
    [Fact]
    public void Buttons_have_on_and_off_appearances()
    {
        using var reopened = SaveAndOpen(NewDocumentWithForm());
        var fields = reopened.Catalog.GetAcroForm()!.Fields;

        var agree = fields["agree"]!;
        var states = agree.Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!.Elements.Keys.OrderBy(k => k).ToList();
        Assert.Equal(new[] { "/Off", "/Yes" }, states);
        Assert.Equal("/Off", agree.Elements.GetName("/AS"));

        var size = (PdfFormRadioButtonField)fields["size"]!;
        Assert.Equal(new[] { "S", "M" }, size.Options);
        Assert.Null(size.SelectedValue);
        foreach (var kid in size.Kids.Elements.Select(item => (PdfDictionary)((PdfReference)item).Value))
            Assert.Same(size, ((PdfReference)kid.Elements["/Parent"]!).Value);
    }

    // The default resources hold Helvetica for text and choice fields; the field's /DA names it with the size and
    // color of the style, and the style's alignment becomes /Q.
    [Fact]
    public void Text_fields_use_helvetica_from_the_default_resources()
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        var form = document.Catalog.GetOrCreateAcroForm();
        var field = form.AddTextField("total", page, new XRect(10, 10, 100, 20),
            new PdfFormFieldStyle { FontSize = 9, TextColor = XColors.Red, Alignment = PdfSharp.Drawing.Layout.XParagraphAlignment.Right });

        Assert.Equal("/Helv 9 Tf 1 0 0 rg", field.Elements.GetString("/DA"));
        Assert.Equal(2, field.Elements.GetInteger("/Q"));
        var helvetica = (PdfDictionary)((PdfReference)form.Elements.GetDictionary("/DR")!.Elements.GetDictionary("/Font")!.Elements["/Helv"]!).Value;
        Assert.Equal("/Helvetica", helvetica.Elements.GetName("/BaseFont"));
        Assert.Equal("/WinAnsiEncoding", helvetica.Elements.GetName("/Encoding"));
    }

    // Flattening draws the created fields into the page and removes them.
    [Fact]
    public void Created_fields_can_be_flattened()
    {
        using var document = NewDocumentWithForm();
        var fields = document.Catalog.GetAcroForm()!.Fields;
        ((PdfFormTextField)fields["name"]!).Text = "Flat";
        ((PdfFormCheckBoxField)fields["agree"]!).Checked = true;

        document.Flatten();
        using var reopened = SaveAndOpen(document);

        Assert.Null(reopened.Pages[0].Elements.GetArray("/Annots"));
        var xObjects = reopened.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        Assert.True(xObjects.Elements.Count >= 6, "each field's appearance is drawn");
    }

    // Merging copies the created fields.
    [Fact]
    public void Created_fields_survive_a_merge()
    {
        using var source = SaveAndOpen(NewDocumentWithForm(), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        merged.AppendPages(source);
        using var reopened = SaveAndOpen(merged);

        var names = reopened.Catalog.GetAcroForm()!.Fields.GetAllFields().Select(f => f.FullName).Where(n => !n.Contains('.')).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "agree", "country", "items", "name", "signature", "size" }, names);
    }

    // The empty signature field is signed into with DigitalSignatureOptions.FieldName.
    [Fact]
    public void A_created_signature_field_can_be_signed()
    {
        using var document = SaveAndOpen(NewDocumentWithForm());
        DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(Certificate, PdfMessageDigestType.SHA256),
            new DigitalSignatureOptions { FieldName = "signature", Reason = "Approved" });

        using var signed = SaveAndOpen(document);

        var field = signed.Catalog.GetAcroForm()!.Fields["signature"]!;
        var value = (PdfDictionary)((PdfReference)field.Elements["/V"]!).Value;
        Assert.Equal("/Sig", value.Elements.GetName("/Type"));
        Assert.Equal(6, signed.Catalog.GetAcroForm()!.Fields.Count);
    }

    // Reading page.Annotations after adding fields must not turn the fields into plain annotations.
    [Fact]
    public void Reading_the_page_annotations_keeps_the_fields()
    {
        using var document = NewDocumentWithForm();

        Assert.Equal(7, document.Pages[0].Annotations.Count);
        using var reopened = SaveAndOpen(document);

        Assert.IsType<PdfFormTextField>(reopened.Catalog.GetAcroForm()!.Fields["name"]);
        Assert.Equal(6, reopened.Catalog.GetAcroForm()!.Fields.Count);
    }

    [Fact]
    public void Invalid_fields_are_refused()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var form = document.Catalog.GetOrCreateAcroForm();
        form.AddTextField("taken", page, new XRect(10, 10, 50, 20));
        var box = new XRect(10, 40, 50, 20);

        Assert.Throws<ArgumentException>(() => form.AddTextField("taken", page, box));
        Assert.Throws<ArgumentException>(() => form.AddTextField("a.b", page, box));
        Assert.Throws<ArgumentException>(() => form.AddTextField("", page, box));
        Assert.Throws<ArgumentException>(() => form.AddTextField("zero", page, new XRect(10, 10, 0, 20)));
        Assert.Throws<ArgumentException>(() => form.AddRadioButtonGroup("none", page, Array.Empty<(string, XRect)>()));
        Assert.Throws<ArgumentException>(() => form.AddRadioButtonGroup("twice", page, new[] { ("A", box), ("A", box) }));
        Assert.Throws<ArgumentException>(() => form.AddComboBox("empty", page, box, Array.Empty<string>()));

        using var other = new PdfDocument();
        Assert.Throws<ArgumentException>(() => form.AddTextField("foreign", other.AddPage(), box));

        var rotated = document.AddPage();
        rotated.Rotate = 90;
        Assert.Throws<NotSupportedException>(() => form.AddCheckBox("turned", rotated, box));
    }

    // PDF/A needs embedded fonts: text and choice fields (Helvetica) are refused, buttons and signature fields
    // (vector appearances) are allowed.
    [Fact]
    public void A_pdf_a_document_refuses_text_fields_but_takes_buttons()
    {
        using var document = new PdfDocument();
        document.SetPdfA(PdfAFormats.PdfA_2b);
        var page = document.AddPage();
        var form = document.Catalog.GetOrCreateAcroForm();

        Assert.Throws<InvalidOperationException>(() => form.AddTextField("name", page, new XRect(10, 10, 100, 20)));
        Assert.Throws<InvalidOperationException>(() => form.AddListBox("list", page, new XRect(10, 40, 100, 40), new[] { "A" }));
        form.AddCheckBox("agree", page, new XRect(10, 90, 12, 12));
        form.AddSignatureField("sign", page, new XRect(10, 110, 100, 40));
        Assert.Equal(2, form.Fields.Count);
    }

    // An opened PDF/A file is not marked as PDF/A in memory; its XMP claim is what refuses the text fields.
    [Fact]
    public void An_opened_pdf_a_file_refuses_text_fields()
    {
        var created = new PdfDocument();
        created.SetPdfA(PdfAFormats.PdfA_2b);
        created.AddPage();
        using var document = SaveAndOpen(created);
        var page = document.Pages[0];
        var form = document.Catalog.GetOrCreateAcroForm();

        Assert.Throws<InvalidOperationException>(() => form.AddTextField("name", page, new XRect(10, 10, 100, 20)));
        Assert.Throws<InvalidOperationException>(() => form.AddComboBox("list", page, new XRect(10, 40, 100, 20), new[] { "A" }));
        form.AddCheckBox("agree", page, new XRect(10, 90, 12, 12));
        Assert.Equal(1, form.Fields.Count);
    }

    // PDF/UA needs tagged widgets, which the Add methods do not write: a tagged document and an opened file whose XMP
    // claims PDF/UA refuse every field and stay unchanged.
    [Fact]
    public void A_pdf_ua_document_refuses_fields()
    {
        using var tagged = new PdfDocument();
        var page = tagged.AddPage();
        PdfSharp.UniversalAccessibility.UAManager.ForDocument(tagged);
        var form = tagged.Catalog.GetOrCreateAcroForm();

        Assert.Throws<InvalidOperationException>(() => form.AddCheckBox("agree", page, new XRect(10, 10, 12, 12)));
        Assert.Throws<InvalidOperationException>(() => form.AddSignatureField("sign", page, new XRect(10, 40, 100, 40)));
        Assert.Null(page.Elements.GetArray("/Annots"));
        Assert.Equal(0, form.Fields.Count);

        using var opened = PdfReader.Open(new MemoryStream(Save(tagged)), PdfDocumentOpenMode.Modify);
        Assert.Throws<InvalidOperationException>(() =>
            opened.Catalog.GetOrCreateAcroForm().AddCheckBox("agree", opened.Pages[0], new XRect(10, 10, 12, 12)));
    }

    // "Off" names the off state; a leading slash would turn the value into a different state name.
    [Theory]
    [InlineData("Off")]
    [InlineData("/Yes")]
    public void Radio_values_that_clash_with_state_names_are_refused(string value)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var form = document.Catalog.GetOrCreateAcroForm();

        Assert.Throws<ArgumentException>(() => form.AddRadioButtonGroup("choice", page,
            new[] { ("On", new XRect(10, 10, 12, 12)), (value, new XRect(30, 10, 12, 12)) }));
    }

    // Everything is checked before any object is made: a refused call leaves no field, widget or font behind.
    [Fact]
    public void A_refused_call_leaves_the_document_unchanged()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var form = document.Catalog.GetOrCreateAcroForm();
        var good = new XRect(10, 10, 12, 12);

        Assert.Throws<ArgumentException>(() => form.AddRadioButtonGroup("choice", page, new[] { ("A", good), ("B", new XRect(30, 10, 0, 12)) }));
        Assert.Throws<ArgumentException>(() => form.AddTextField("name", page, new XRect(double.NaN, 10, 100, 20)));
        Assert.Throws<ArgumentException>(() => form.AddTextField("name", page, new XRect(10, 10, double.PositiveInfinity, 20)));
        Assert.Throws<ArgumentOutOfRangeException>(() => form.AddTextField("name", page, good, new PdfFormFieldStyle { FontSize = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => form.AddComboBox("list", page, good, new[] { "A" }, new PdfFormFieldStyle { FontSize = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => form.AddCheckBox("agree", page, good, new PdfFormFieldStyle { BorderWidth = -2 }));

        Assert.Null(page.Elements.GetArray("/Annots"));
        Assert.Equal(0, form.Fields.Count);
        Assert.Null(form.Elements.GetDictionary("/DR"));
    }

    // Rectangles are measured from the top left of the crop box, which need not start at the origin.
    [Fact]
    public void The_rectangle_is_relative_to_the_crop_box()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(600, 800));
        page.CropBox = new PdfRectangle(new XPoint(550, 700), new XPoint(50, 100));
        var form = document.Catalog.GetOrCreateAcroForm();

        var rect = form.AddTextField("name", page, new XRect(10, 20, 100, 30)).Elements.GetRectangle("/Rect")!;

        Assert.Equal(60, rect.X1, 3);
        Assert.Equal(700 - 50, rect.Y1, 3);
        Assert.Equal(160, rect.X2, 3);
        Assert.Equal(700 - 20, rect.Y2, 3);
    }

    // Widget colors have no transparency: a fully transparent color means none.
    [Fact]
    public void A_transparent_color_means_no_border_or_background()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var form = document.Catalog.GetOrCreateAcroForm();

        var field = form.AddCheckBox("agree", page, new XRect(10, 10, 12, 12),
            new PdfFormFieldStyle { BorderColor = XColors.Transparent, BackgroundColor = XColor.FromArgb(0, 255, 0, 0) });

        Assert.Null(field.Elements.GetDictionary("/MK"));
        Assert.Equal(0, field.Elements.GetDictionary("/BS")!.Elements.GetReal("/W"));
    }

    static string AppearanceText(PdfDictionary widget)
    {
        var normal = (PdfDictionary)((PdfReference)widget.Elements.GetDictionary("/AP")!.Elements["/N"]!).Value;
        return Encoding.ASCII.GetString(normal.Stream!.UnfilteredValue);
    }

    static readonly X509Certificate2 Certificate = CreateCertificate();

    static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=FreePDF form creation test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
