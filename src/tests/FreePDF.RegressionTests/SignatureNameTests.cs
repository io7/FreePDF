// Signature field structure only; no assertion of cryptographic validity. MIT.
using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class SignatureNameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Added_signature_names_avoid_existing_fields_and_nested_fields(bool nested)
    {
        using var document = new PdfDocument();
        document.AddPage();
        var fields = new PdfArray(document);
        var form = new PdfDictionary(document);
        form.Elements["/Fields"] = fields;
        document.Catalog.Elements["/AcroForm"] = form;
        var first = Field(document, "Signature1");
        if (nested)
        {
            var parent = Field(document, "Group");
            parent.Elements["/Kids"] = new PdfArray(document, first);
            fields.Elements.Add(parent);
        }
        else fields.Elements.Add(first);
        fields.Elements.Add(Field(document, "Signature2"));

        var handler = DigitalSignatureHandler.ForDocument(document, new PlaceholderSigner(),
            new DigitalSignatureOptions { AppearanceHandler = new EmptyAppearance() });
        var add = typeof(DigitalSignatureHandler).GetMethod("AddSignatureComponentsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)add.Invoke(handler, null)!;
        await (Task)add.Invoke(handler, null)!;
        var resultingFields = document.Catalog.Elements.GetDictionary("/AcroForm")!.Elements.GetArray("/Fields")!;
        Assert.Equal("Signature3", resultingFields.Elements.GetDictionary(2)!.Elements.GetString("/T"));
        Assert.Equal("Signature4", resultingFields.Elements.GetDictionary(3)!.Elements.GetString("/T"));
    }

    static PdfDictionary Field(PdfDocument document, string name)
    {
        var field = new PdfDictionary(document);
        field.Elements.SetString("/T", name);
        field.Elements.SetName("/FT", "/Tx");
        return field;
    }

    sealed class PlaceholderSigner : IDigitalSigner
    {
        public string CertificateName => "Synthetic fixture";
        public Task<int> GetSignatureSizeAsync() => Task.FromResult(3);
        public Task<byte[]> GetSignatureAsync(Stream stream) => Task.FromResult(new byte[] { 1, 2, 3 });
    }

    sealed class EmptyAppearance : IAnnotationAppearanceHandler
    {
        public void DrawAppearance(XGraphics gfx, XRect rect) { }
    }
}
