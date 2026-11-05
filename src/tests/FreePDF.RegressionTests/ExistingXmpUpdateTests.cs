// XMP that a file already has, kept by FreePDF when it saves: it must identify a PDF/A file and agree with the document
// information (upstream PDFsharp #356 and #166). Original fixtures; MIT, see LICENSE.
using System.Text;
using System.Xml;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Metadata;
using PdfSharp.Pdf.PdfA;
using Xunit;

namespace FreePDF.RegressionTests;

public class ExistingXmpUpdateTests
{
    const string CustomSchema = "urn:example:custom#";

    static string Packet(string extraDescriptions) =>
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
        "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title><rdf:Alt>" +
        "<rdf:li xml:lang=\"x-default\">Old title</rdf:li><rdf:li xml:lang=\"de\">Alter Titel</rdf:li></rdf:Alt></dc:title></rdf:Description>\n" +
        "<rdf:Description rdf:about=\"\" xmlns:xap=\"http://ns.adobe.com/xap/1.0/\" xap:ModifyDate=\"\"/>\n" +
        $"<rdf:Description rdf:about=\"\" xmlns:c=\"{CustomSchema}\"><c:Kept>yes</c:Kept></rdf:Description>\n" +
        extraDescriptions +
        "</rdf:RDF></x:xmpmeta>\n<?xpacket end=\"w\"?>";

    const string PdfAIdentification =
        "<rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"><pdfaid:part>1</pdfaid:part><pdfaid:conformance>B</pdfaid:conformance></rdf:Description>\n";

    static byte[] FileWithXmp(string packet, string pageExtra = "")
    {
        var xmp = Encoding.UTF8.GetBytes(packet);
        // ASCII-safe: the packet travels hex encoded through the fixture builder.
        var hex = string.Concat(xmp.Select(value => value.ToString("X2"))) + ">";
        return TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Annots [6 0 R] {pageExtra}>>",
            "<< /Title (Info title) /Author (Info author) >>",
            $"<< /Type /Metadata /Subtype /XML /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream",
            "<< /Type /Annot /Subtype /Link /Rect [10 10 50 30] /Border [0 0 0] /A << /S /URI /URI (https://example.com) >> >>",
        }, _ => "/Info 4 0 R");
    }

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static string XmpOf(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return Encoding.UTF8.GetString(MetadataManager.ForDocument(document).GetMetadata()!.Stream.UnfilteredValue);
    }

    static (XmlDocument Xml, XmlNamespaceManager Names) Parse(string packet)
    {
        int start = packet.IndexOf("<x:xmpmeta", StringComparison.Ordinal);
        int end = packet.IndexOf("</x:xmpmeta>", StringComparison.Ordinal) + "</x:xmpmeta>".Length;
        var xml = new XmlDocument();
        xml.LoadXml(packet.Substring(start, end - start));
        var names = new XmlNamespaceManager(xml.NameTable);
        names.AddNamespace("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        names.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
        names.AddNamespace("xmp", "http://ns.adobe.com/xap/1.0/");
        names.AddNamespace("pdf", "http://ns.adobe.com/pdf/1.3/");
        names.AddNamespace("pdfaid", "http://www.aiim.org/pdfa/ns/id/");
        names.AddNamespace("c", CustomSchema);
        return (xml, names);
    }

    [Fact]
    public void SetPdfA_on_a_file_with_its_own_XMP_identifies_it_and_keeps_the_rest()
    {
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(Packet(""))), PdfDocumentOpenMode.Modify);
        document.SetPdfA(PdfAFormats.PdfA_1b);

        var saved = Save(document);
        var (xml, names) = Parse(XmpOf(saved));
        Assert.Equal("1", xml.SelectSingleNode("//pdfaid:part", names)!.InnerText);
        Assert.Equal("B", xml.SelectSingleNode("//pdfaid:conformance", names)!.InnerText);
        Assert.Equal("Info title", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText);
        Assert.Equal("Alter Titel", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='de']", names)!.InnerText);
        Assert.Equal("Info author", xml.SelectSingleNode("//dc:creator/rdf:Seq/rdf:li", names)!.InnerText);
        Assert.Equal("yes", xml.SelectSingleNode("//c:Kept", names)!.InnerText);
        Assert.StartsWith("FreePDF", xml.SelectSingleNode("//pdf:Producer", names)!.InnerText);
        Assert.Single(xml.SelectNodes("//xmp:ModifyDate | //rdf:Description/@xmp:ModifyDate", names)!);

        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        var link = reopened.Pages[0].Elements.GetArray("/Annots")!.Elements.GetDictionary(0)!;
        Assert.Equal(4, link.Elements.GetInteger("/F") & 4);
        Assert.Equal(reopened.Info.Producer, xml.SelectSingleNode("//pdf:Producer", names)!.InnerText);
    }

    [Fact]
    public void A_Modify_save_of_a_PDF_A_file_brings_its_XMP_up_to_date()
    {
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(Packet(PdfAIdentification))), PdfDocumentOpenMode.Modify);
        document.Info.Title = "New title";

        var saved = Save(document);
        var (xml, names) = Parse(XmpOf(saved));
        Assert.Equal("New title", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText);
        Assert.Equal("1", xml.SelectSingleNode("//pdfaid:part", names)!.InnerText);
        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        var modified = reopened.Info.ModificationDate!.Value;
        Assert.Equal(MetadataManagerDate(modified), xml.SelectSingleNode("//xmp:ModifyDate", names)!.InnerText);
        Assert.Equal("yes", xml.SelectSingleNode("//c:Kept", names)!.InnerText);
    }

    static string MetadataManagerDate(DateTimeOffset value)
        => value.ToString("yyyy-MM-ddTHH:mm:ssK", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Other_files_and_incremental_updates_keep_their_XMP_unchanged()
    {
        var plain = Packet("");
        using (var document = PdfReader.Open(new MemoryStream(FileWithXmp(plain)), PdfDocumentOpenMode.Modify))
        {
            document.Info.Title = "New title";
            Assert.Equal(plain, XmpOf(Save(document)));
        }

        var pdfa = Packet(PdfAIdentification);
        using (var document = PdfReader.Open(new MemoryStream(FileWithXmp(pdfa)), PdfDocumentOpenMode.Append))
        {
            document.Info.Title = "New title";
            Assert.Equal(pdfa, XmpOf(Save(document)));
        }
    }

    [Fact]
    public void SetPdfA_replaces_XMP_that_is_not_XML()
    {
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp("<?xpacket begin=\"\" id=\"x\"?><broken<<<<?xpacket end=\"w\"?>")), PdfDocumentOpenMode.Modify);
        document.SetPdfA(PdfAFormats.PdfA_2b);

        var (xml, names) = Parse(XmpOf(Save(document)));
        Assert.Equal("2", xml.SelectSingleNode("//pdfaid:part", names)!.InnerText);
        Assert.Equal("Info title", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li", names)!.InnerText);
    }

    // PDF/A-1 forbids page transparency groups (rule 6.4-3); a Modify save added one to every page of an opened file.
    [Fact]
    public void A_Modify_save_of_a_PDF_A_1_file_adds_no_page_transparency_group()
    {
        using (var document = PdfReader.Open(new MemoryStream(FileWithXmp(Packet(PdfAIdentification))), PdfDocumentOpenMode.Modify))
        {
            using var reopened = PdfReader.Open(new MemoryStream(Save(document)), PdfDocumentOpenMode.Import);
            Assert.Null(reopened.Pages[0].Elements["/Group"]);
        }
        // Other files keep getting the group.
        using (var document = PdfReader.Open(new MemoryStream(FileWithXmp(Packet(""))), PdfDocumentOpenMode.Modify))
        {
            using var reopened = PdfReader.Open(new MemoryStream(Save(document)), PdfDocumentOpenMode.Import);
            Assert.NotNull(reopened.Pages[0].Elements["/Group"]);
        }
    }

    [Fact]
    public void SetPdfA_1_removes_a_page_transparency_group_of_an_opened_file()
    {
        var bytes = FileWithXmp(Packet(""), "/Group << /S /Transparency /CS /DeviceRGB >> ");
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        document.SetPdfA(PdfAFormats.PdfA_1b);

        using var reopened = PdfReader.Open(new MemoryStream(Save(document)), PdfDocumentOpenMode.Import);
        Assert.Null(reopened.Pages[0].Elements["/Group"]);
    }
}
