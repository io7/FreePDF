// Factur-X / ZUGFeRD hybrid invoices: PDF/A-3 with the CII invoice XML embedded and declared in the catalog /AF array
// and in the XMP metadata. Original synthetic invoice data; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Attachments;
using PdfSharp.Pdf.Invoicing;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.PdfA;
using Xunit;

namespace FreePDF.RegressionTests;

public class FacturXTests
{
    internal const string MinimumInvoice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"
            xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100"
            xmlns:udt="urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100">
          <rsm:ExchangedDocumentContext>
            <ram:GuidelineSpecifiedDocumentContextParameter><ram:ID>urn:factur-x.eu:1p0:minimum</ram:ID></ram:GuidelineSpecifiedDocumentContextParameter>
          </rsm:ExchangedDocumentContext>
          <rsm:ExchangedDocument>
            <ram:ID>INV-2026-001</ram:ID>
            <ram:TypeCode>380</ram:TypeCode>
            <ram:IssueDateTime><udt:DateTimeString format="102">20261103</udt:DateTimeString></ram:IssueDateTime>
          </rsm:ExchangedDocument>
        </rsm:CrossIndustryInvoice>
        """;

    static byte[] Xml => Encoding.UTF8.GetBytes(MinimumInvoice);

    static PdfDictionary FileSpecification(PdfDocument document)
    {
        var af = document.Catalog.Elements.GetArray("/AF")!;
        Assert.Equal(1, af.Elements.Count);
        return af.Elements.GetDictionary(0)!;
    }

    static string SaveAndReadMetadata(PdfDocument document, out byte[] saved)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        saved = stream.ToArray();
        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        return Encoding.UTF8.GetString(reopened.Catalog.Elements.GetDictionary("/Metadata")!.Stream!.Value);
    }

    [Theory]
    [InlineData(FacturXProfile.Minimum, "/Data", "MINIMUM")]
    [InlineData(FacturXProfile.BasicWL, "/Data", "BASIC WL")]
    [InlineData(FacturXProfile.Basic, "/Alternative", "BASIC")]
    [InlineData(FacturXProfile.EN16931, "/Alternative", "EN 16931")]
    [InlineData(FacturXProfile.Extended, "/Alternative", "EXTENDED")]
    public void The_invoice_is_embedded_linked_and_declared(FacturXProfile profile, string relationship, string level)
    {
        using var document = new PdfDocument();
        FacturXManager.ForDocument(document).SetInvoice(Xml, profile);
        document.AddPage();

        var spec = FileSpecification(document);
        Assert.Equal("factur-x.xml", spec.Elements.GetString("/F"));
        Assert.Equal("factur-x.xml", spec.Elements.GetString("/UF"));
        Assert.Equal(relationship, spec.Elements.GetName("/AFRelationship"));
        Assert.Equal("3B", PdfAManager.ForDocument(document).Format.Name);

        var xmp = SaveAndReadMetadata(document, out var saved);
        Assert.Contains("urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#", xmp);
        Assert.Contains("<fx:DocumentType>INVOICE</fx:DocumentType>", xmp);
        Assert.Contains("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>", xmp);
        Assert.Contains("<fx:Version>1.0</fx:Version>", xmp);
        Assert.Contains($"<fx:ConformanceLevel>{level}</fx:ConformanceLevel>", xmp);
        Assert.Contains("<pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</pdfaSchema:namespaceURI>", xmp);
        Assert.Contains("<pdfaid:part>3</pdfaid:part>", xmp);

        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Modify);
        var file = EmbeddedFilesManager.ForDocument(reopened).GetEmbeddedFileInfo("factur-x.xml")!;
        Assert.Equal(MinimumInvoice, Encoding.UTF8.GetString(file.Data));
        Assert.Equal("text/xml", file.FileType);
        Assert.Equal(relationship.TrimStart('/'), file.AFRelationship);
    }

    [Fact]
    public void The_XRechnung_profile_uses_its_own_file_name()
    {
        using var document = new PdfDocument();
        FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.XRechnung);
        document.AddPage();

        Assert.Equal("xrechnung.xml", FileSpecification(document).Elements.GetString("/F"));
        var xmp = SaveAndReadMetadata(document, out _);
        Assert.Contains("<fx:DocumentFileName>xrechnung.xml</fx:DocumentFileName>", xmp);
        Assert.Contains("<fx:ConformanceLevel>XRECHNUNG</fx:ConformanceLevel>", xmp);
    }

    [Fact]
    public void The_relationship_can_be_chosen()
    {
        using var document = new PdfDocument();
        FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.EN16931, PdfAFRelationship.Source);

        Assert.Equal("/Source", FileSpecification(document).Elements.GetName("/AFRelationship"));
    }

    [Fact]
    public void An_existing_PDF_A_3_format_is_kept()
    {
        using var document = new PdfDocument();
        PdfAManager.ForDocument(document).SetFormat(PdfAFormats.PdfA_3u);
        FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.EN16931);

        Assert.Equal("3U", PdfAManager.ForDocument(document).Format.Name);
    }

    [Theory]
    [InlineData("<invoice/>")]
    [InlineData("<rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\">")]
    [InlineData("")]
    public void Only_well_formed_CII_invoice_XML_is_accepted(string xml)
    {
        using var document = new PdfDocument();

        Assert.Throws<ArgumentException>(() => FacturXManager.ForDocument(document).SetInvoice(Encoding.UTF8.GetBytes(xml), FacturXProfile.Minimum));
    }

    [Fact]
    public void Another_PDF_A_part_is_refused()
    {
        using var document = new PdfDocument();
        PdfAManager.ForDocument(document).SetFormat(PdfAFormats.PdfA_2b);

        Assert.Throws<InvalidOperationException>(() => FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.Minimum));
    }

    [Fact]
    public void A_document_that_already_has_pages_but_no_PDF_A_format_is_refused()
    {
        using var document = new PdfDocument();
        document.AddPage();

        var refused = Assert.Throws<InvalidOperationException>(() => FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.Minimum));
        Assert.Contains("PDF/A-3", refused.Message);
    }

    [Fact]
    public void A_second_invoice_is_refused()
    {
        using var document = new PdfDocument();
        FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.Minimum);

        Assert.Throws<InvalidOperationException>(() => FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.Minimum));
    }

    [Fact]
    public void A_tagged_invoice_declares_both_extension_schemas_in_one_list()
    {
        // XMP allows pdfaExtension:schemas once per resource; a second list made the whole metadata unreadable.
        using var document = new PdfDocument();
        FacturXManager.ForDocument(document).SetInvoice(Xml, FacturXProfile.EN16931);
        PdfSharp.UniversalAccessibility.UAManager.ForDocument(document);
        document.Info.Title = "Tagged invoice";
        document.AddPage();

        var xmp = SaveAndReadMetadata(document, out _);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xmp, "<pdfaExtension:schemas>"));
        Assert.Contains("<pdfaSchema:prefix>fx</pdfaSchema:prefix>", xmp);
        Assert.Contains("<pdfaSchema:prefix>pdfuaid</pdfaSchema:prefix>", xmp);
        Assert.Contains("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>", xmp);
        Assert.Contains("<pdfuaid:part>1</pdfuaid:part>", xmp);
    }
}
