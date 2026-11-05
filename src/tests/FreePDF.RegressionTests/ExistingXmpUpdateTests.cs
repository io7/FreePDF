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

    static byte[] FileWithXmp(string packet, string pageExtra = "", string info = "<< /Title (Info title) /Author (Info author) >>",
        string? metadataObject = null)
    {
        var xmp = Encoding.UTF8.GetBytes(packet);
        // ASCII-safe: the packet travels hex encoded through the fixture builder.
        var hex = string.Concat(xmp.Select(value => value.ToString("X2"))) + ">";
        return TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] /Annots [6 0 R] {pageExtra}>>",
            info,
            metadataObject ?? $"<< /Type /Metadata /Subtype /XML /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream",
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

    // Found in review: a metadata stream that cannot be decoded made every Modify save throw, PDF/A or not.
    [Theory]
    [InlineData("<< /Type /Metadata /Subtype /XML /Filter /FlateDecode /Length 6 >>\nstream\nbroken\nendstream")]
    [InlineData("<< /Type /Metadata /Subtype /XML /Filter /FooDecode /Length 6 >>\nstream\nbroken\nendstream")]
    public void A_metadata_stream_that_cannot_be_decoded_does_not_stop_a_save(string metadataObject)
    {
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp("", metadataObject: metadataObject)), PdfDocumentOpenMode.Modify);
        Save(document);
    }

    // Found in review: characters XML cannot hold made the save throw; packet headers that are not the usual one too.
    [Theory]
    [InlineData("/Title (Report\\000) /Author (A\\001B)", "")]
    [InlineData("/Title (Plain)", "\n")]
    [InlineData("/Title (Plain)", "<?xml version=\"1.0\"?>")]
    public void Unusual_values_and_headers_do_not_stop_the_update(string info, string beforeHeader)
    {
        var bytes = FileWithXmp(beforeHeader + Packet(PdfAIdentification), info: $"<< {info} >>");
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);

        var (xml, names) = Parse(XmpOf(Save(document)));
        var title = xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText;
        Assert.StartsWith(info.Contains("Report") ? "Report" : "Plain", title);
    }

    [Fact]
    public void A_double_encoded_byte_order_mark_in_the_header_does_not_stop_the_update()
    {
        var packet = Packet(PdfAIdentification).Replace("begin=\"\uFEFF\"", "begin=\"\u00EF\u00BB\u00BF\"");
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(packet)), PdfDocumentOpenMode.Modify);
        document.Info.Title = "Updated";

        var (xml, names) = Parse(XmpOf(Save(document)));
        Assert.Equal("Updated", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText);
    }

    // Found in review: pdfaid:part inside the XMP of a placed file (xmpMM:Pantry) made a plain file count as PDF/A.
    [Fact]
    public void A_nested_PDF_A_identification_is_not_a_claim()
    {
        const string pantry =
            "<rdf:Description rdf:about=\"\" xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\"><xmpMM:Pantry><rdf:Bag><rdf:li>" +
            "<rdf:Description xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" pdfaid:part=\"1\" pdfaid:conformance=\"B\"/>" +
            "</rdf:li></rdf:Bag></xmpMM:Pantry></rdf:Description>\n";
        var packet = Packet(pantry);
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(packet)), PdfDocumentOpenMode.Modify);
        document.Info.Title = "New title";

        var saved = Save(document);
        Assert.Equal(packet, XmpOf(saved));
        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        Assert.NotNull(reopened.Pages[0].Elements["/Group"]);
    }

    // Found in review: a line break in a value was normalised by XML parsers, and a cleared title left the old one.
    [Fact]
    public void Line_breaks_survive_and_an_empty_entry_empties_the_property()
    {
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(Packet(PdfAIdentification))), PdfDocumentOpenMode.Modify);
        document.Info.Subject = "First line\r\nSecond line";
        document.Info.Title = "";

        var (xml, names) = Parse(XmpOf(Save(document)));
        Assert.Equal("First line\r\nSecond line", xml.SelectSingleNode("//dc:description/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText);
        Assert.Equal("", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText);
    }

    // Found in review: AutoGenerate set by the caller (or by PDF/UA) on an opened PDF/A file skipped the update.
    [Fact]
    public void An_explicit_AutoGenerate_strategy_also_updates_a_PDF_A_file()
    {
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(Packet(PdfAIdentification))), PdfDocumentOpenMode.Modify);
        MetadataManager.ForDocument(document).Strategy = DocumentMetadataStrategy.AutoGenerate;
        document.Info.Title = "New title";

        var (xml, names) = Parse(XmpOf(Save(document)));
        Assert.Equal("New title", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText);
    }

    // Found in review: information strings in PDFDocEncoding (no byte order mark) went into the XMP as Latin-1, so
    // curly quotes, dashes and the euro sign became control characters or other characters.
    [Fact]
    public void PDFDocEncoding_information_strings_are_decoded_for_the_XMP()
    {
        var info = "<< /Title (\\215Quoted\\216 \\204 Dash \\222 \\200 \\240 \\030) >>";
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(Packet(PdfAIdentification), info: info)), PdfDocumentOpenMode.Modify);

        var (xml, names) = Parse(XmpOf(Save(document)));
        Assert.Equal("\u201CQuoted\u201D \u2014 Dash \u2122 \u2022 \u20AC \u02D8",
            xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li[@xml:lang='x-default']", names)!.InnerText);
    }

    // Found in review: an instruction such as <?xml-stylesheet?> was dropped as if it were the XML declaration, and a
    // value written as a character reference (&#xD;) was rewritten.
    [Fact]
    public void Processing_instructions_and_character_references_in_other_properties_are_kept()
    {
        var packet = Packet("<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:source>&#xD;</dc:source></rdf:Description>\n" + PdfAIdentification)
            .Replace("<x:xmpmeta", "<?xml-stylesheet href=\"x.xsl\"?><x:xmpmeta");
        using var document = PdfReader.Open(new MemoryStream(FileWithXmp(packet)), PdfDocumentOpenMode.Modify);

        var xmp = XmpOf(Save(document));
        Assert.Contains("<?xml-stylesheet href=\"x.xsl\"?>", xmp);
        Assert.Contains("<dc:source>&#xD;</dc:source>", xmp);
    }
}
