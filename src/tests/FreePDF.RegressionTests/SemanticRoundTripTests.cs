// Original text, navigation, form and attachment fixture. MIT; see LICENSE.
using System.Text;
using FreePDF.Lab.Fixtures;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class SemanticRoundTripTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saving_preserves_text_navigation_annotations_form_values_and_attachment_bytes(bool inspectFirst)
    {
        using var doc = PdfReader.Open(new MemoryStream(PreservationFixtures.Semantic()), PdfDocumentOpenMode.Modify);
        if (inspectFirst) Inspect(doc);
        doc.Info.Subject = "edited metadata";
        using var saved = new MemoryStream(); doc.Save(saved); saved.Position=0;
        using var reopened = PdfReader.Open(saved,PdfDocumentOpenMode.Modify);
        Inspect(reopened);
        Assert.Equal("edited metadata",reopened.Info.Subject);
        using var savedAgain = new MemoryStream(); reopened.Save(savedAgain); savedAgain.Position=0;
        using var twice = PdfReader.Open(savedAgain,PdfDocumentOpenMode.Modify);
        Inspect(twice);
    }

    static void Inspect(PdfDocument doc)
    {
        Assert.Equal("Original title",doc.Info.Title);
        Assert.Equal(1,doc.PageCount);
        var text=ContentReader.ReadContent(doc.Pages[0]).OfType<COperator>()
            .Where(op=>op.Name=="Tj").SelectMany(op=>op.Operands.OfType<CString>()).Select(s=>s.Value);
        Assert.Equal(new[]{"Invoice 123"},text);
        var annotations=doc.Pages[0].Annotations;
        Assert.Equal(3,annotations.Count);
        Assert.Equal("review note",annotations[0].Contents);
        Assert.Equal("https://example.org/invoice/123",annotations[1].Elements.GetDictionary("/A")!.Elements.GetString("/URI"));
        var catalog=doc.Internals.Catalog;
        var form=catalog.GetAcroForm()!;
        var field=form.Elements.GetArray("/Fields")!.Elements.GetDictionary(0)!;
        Assert.Equal("Customer",field.Elements.GetString("/T"));
        Assert.Equal("Ada Example",field.Elements.GetString("/V"));
        Assert.Equal(annotations[2].Reference!.ObjectID,field.Reference!.ObjectID);
        Assert.Equal(field.Elements.GetString("/V"),annotations[2].Elements.GetString("/V"));
        var names=catalog.Elements.GetDictionary("/Names")!;
        var destinations=names.Elements.GetDictionary("/Dests")!.Elements.GetArray("/Names")!;
        Assert.Equal("invoice",destinations.Elements.GetString(0));
        var destination=destinations.Elements.GetArray(1)!;
        Assert.Same(doc.Pages[0],destination.Elements.GetObject(0));
        var files=names.Elements.GetDictionary("/EmbeddedFiles")!.Elements.GetArray("/Names")!;
        Assert.Equal("invoice.xml",files.Elements.GetString(0));
        var specification=files.Elements.GetDictionary(1)!;
        Assert.Equal("invoice.xml",specification.Elements.GetString("/F"));
        var stream=specification.Elements.GetDictionary("/EF")!.Elements.GetDictionary("/F")!.Stream!;
        Assert.Equal(Encoding.UTF8.GetBytes("<invoice id=\"123\"/>"),stream.UnfilteredValue);
    }

}
