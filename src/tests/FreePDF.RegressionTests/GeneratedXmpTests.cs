// Generated XMP metadata that was malformed or made saving throw, found in the upstream PDFsharp triage of 2026-11-05.
// The fixtures are our own. MIT, see LICENSE.
using System.Text;
using System.Xml;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Metadata;
using Xunit;

namespace FreePDF.RegressionTests;

public class GeneratedXmpTests
{
    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    static XmlDocument ReadXmp(byte[] pdf)
    {
        using var reopened = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var text = Encoding.UTF8.GetString(MetadataManager.ForDocument(reopened).GetMetadata()!.Stream.UnfilteredValue);
        var xml = new XmlDocument();
        xml.LoadXml(text.Substring(text.IndexOf("<x:xmpmeta", StringComparison.Ordinal),
            text.IndexOf("</x:xmpmeta>", StringComparison.Ordinal) + "</x:xmpmeta>".Length - text.IndexOf("<x:xmpmeta", StringComparison.Ordinal)));
        return xml;
    }

    static XmlNamespaceManager Namespaces(XmlDocument xml)
    {
        var namespaces = new XmlNamespaceManager(xml.NameTable);
        namespaces.AddNamespace("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        namespaces.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
        namespaces.AddNamespace("xap", "http://ns.adobe.com/xap/1.0/");
        namespaces.AddNamespace("pdf", "http://ns.adobe.com/pdf/1.3/");
        return namespaces;
    }

    // Upstream #419: '&' or '<' in a title made the XMP malformed. Control characters are not allowed in XML at all.
    [Fact]
    public void Text_in_generated_XMP_is_escaped()
    {
        using var document = new PdfDocument();
        document.SetPdfA();
        document.Info.Title = "Profit & Loss <2026> \u0001\U0001F600";
        document.Info.Author = "Smith & \"Sons\"";
        document.Info.Subject = "a > b";
        document.Info.Keywords = "R&D";
        document.AddPage();

        var xml = ReadXmp(Save(document));
        var namespaces = Namespaces(xml);
        Assert.Equal("Profit & Loss <2026> \U0001F600", xml.SelectSingleNode("//dc:title/rdf:Alt/rdf:li", namespaces)!.InnerText);
        Assert.Equal("Smith & \"Sons\"", xml.SelectSingleNode("//dc:creator/rdf:Seq/rdf:li", namespaces)!.InnerText);
        Assert.Equal("a > b", xml.SelectSingleNode("//dc:description/rdf:Alt/rdf:li", namespaces)!.InnerText);
        Assert.Equal("R&D", xml.SelectSingleNode("//pdf:Keywords", namespaces)!.InnerText);
    }

    // Upstream #166: a document without a modification date got an empty xmp:ModifyDate, which is no valid date.
    [Fact]
    public void A_missing_date_is_left_out_of_generated_XMP()
    {
        using var document = new PdfDocument();
        MetadataManager.ForDocument(document).Strategy = DocumentMetadataStrategy.AutoGenerate;
        document.AddPage();
        Assert.Null(document.Info.ModificationDate);

        var xml = ReadXmp(Save(document));
        var namespaces = Namespaces(xml);
        Assert.Null(xml.SelectSingleNode("//xap:ModifyDate", namespaces));
        var created = xml.SelectSingleNode("//xap:CreateDate", namespaces)!.InnerText;
        Assert.NotEqual("", created);
        Assert.Equal(created, xml.SelectSingleNode("//xap:MetadataDate", namespaces)!.InnerText);
    }

    // Upstream #362: an information entry that is not text (a number, an array, a dangling reference) made saving with
    // generated XMP throw.
    [Theory]
    [InlineData("/Title 42")]
    [InlineData("/Title [(a) (b)]")]
    [InlineData("/Title 99 0 R")]
    [InlineData("/Author << /A 1 >>")]
    [InlineData("/CreationDate 7")]
    [InlineData("/ModDate (not a date)")]
    public void Malformed_information_entries_do_not_stop_XMP_generation(string entry)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            $"<< {entry} /Subject (kept) >>",
        }, _ => "/Info 4 0 R");
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        MetadataManager.ForDocument(document).Strategy = DocumentMetadataStrategy.AutoGenerate;

        var xml = ReadXmp(Save(document));
        Assert.Equal("kept", xml.SelectSingleNode("//dc:description/rdf:Alt/rdf:li", Namespaces(xml))!.InnerText);
    }
}
