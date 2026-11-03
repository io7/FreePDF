// Files written by FreePDF name FreePDF as the application that made them. Up to 7.2.0 the document information,
// the XMP metadata, the file comments and the signature dictionary said "PDFsharp", "empira MigraDoc" and
// www.pdfsharp.com, so a FreePDF file could not be told apart from one written by empira's PDFsharp.
// Original tests; MIT, see LICENSE.
using MigraDoc;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Metadata;
using PdfSharp.Pdf.Signatures;
using Xunit;

namespace FreePDF.RegressionTests;

public class ProducerIdentityTests
{
    const string Url = "https://github.com/io7/FreePDF";
    static readonly string Version = PdfSharpProductVersionInformation.SemanticVersion;

    public ProducerIdentityTests() => TestFonts.Initialize();

    [Fact]
    public void The_product_information_names_FreePDF_and_io7()
    {
        Assert.Equal("FreePDF", PdfSharpProductVersionInformation.Title);
        Assert.Equal("FreePDF", PdfSharpProductVersionInformation.Product);
        Assert.Equal("io7", PdfSharpProductVersionInformation.Company);
        Assert.Equal(Url, PdfSharpProductVersionInformation.Url);
        Assert.Contains("empira Software GmbH", PdfSharpProductVersionInformation.Copyright);
        Assert.Contains("io7", PdfSharpProductVersionInformation.Copyright);

        Assert.Equal("FreePDF Layout", MigraDocProductVersionInformation.Title);
        Assert.Equal("FreePDF Layout", MigraDocProductVersionInformation.Product);
        Assert.Equal("io7", MigraDocProductVersionInformation.Company);
        Assert.Equal(Url, MigraDocProductVersionInformation.Url);
        Assert.Contains("empira Software GmbH", MigraDocProductVersionInformation.Copyright);
        Assert.Contains("io7", MigraDocProductVersionInformation.Copyright);
    }

    [Fact]
    public void A_new_document_names_FreePDF_as_producer_and_creator()
    {
        using var document = new PdfDocument();
        document.Options.Layout = PdfWriterLayout.Compact;
        document.AddPage();

        var saved = Save(document);

        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        Assert.StartsWith($"FreePDF {Version}", reopened.Info.Producer);
        Assert.Equal($"FreePDF {Version} ({Url})", reopened.Info.Creator);
        AssertNoUpstreamName(TestPdf.Latin1.GetString(saved));
    }

    [Fact]
    public void A_layout_document_names_FreePDF_Layout_as_creator()
    {
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = "FreePDF Synthetic";
        document.AddSection().AddParagraph("Body text");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Options.Layout = PdfWriterLayout.Compact;

        var saved = Save(renderer.PdfDocument);

        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        Assert.Equal($"FreePDF Layout {Version} ({Url})", reopened.Info.Creator);
        Assert.StartsWith($"FreePDF {Version}", reopened.Info.Producer);
        AssertNoUpstreamName(TestPdf.Latin1.GetString(saved));
    }

    [Fact]
    public void XMP_metadata_names_FreePDF()
    {
        using var document = new PdfDocument();
        MetadataManager.ForDocument(document).Strategy = DocumentMetadataStrategy.AutoGenerate;
        document.AddPage();

        var saved = Save(document);

        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        var xmp = System.Text.Encoding.UTF8.GetString(MetadataManager.ForDocument(reopened).GetMetadata()!.Stream.UnfilteredValue);
        Assert.Contains($"<pdf:Producer>FreePDF {Version}", xmp);
        Assert.Contains($"<xap:CreatorTool>FreePDF {Version} ({Url})</xap:CreatorTool>", xmp);
        AssertNoUpstreamName(xmp);
    }

    [Fact]
    public void Editing_a_PDFsharp_file_names_FreePDF_and_keeps_the_original_producer()
    {
        using var document = Open("(PDFsharp 6.2.4 \\(www.pdfsharp.com\\))");

        using var reopened = PdfReader.Open(new MemoryStream(Save(document)), PdfDocumentOpenMode.Import);
        Assert.StartsWith($"FreePDF {Version}", reopened.Info.Producer);
        Assert.EndsWith(" (Original: PDFsharp 6.2.4 (www.pdfsharp.com))", reopened.Info.Producer);
        Assert.Equal("Synthetic application", reopened.Info.Creator);
    }

    [Fact]
    public void Saving_a_FreePDF_file_again_keeps_its_producer()
    {
        using var document = Open("(FreePDF 7.2.1 \\(https://github.com/io7/FreePDF\\))");

        using var reopened = PdfReader.Open(new MemoryStream(Save(document)), PdfDocumentOpenMode.Import);
        Assert.Equal("FreePDF 7.2.1 (https://github.com/io7/FreePDF)", reopened.Info.Producer);
    }

    [Fact]
    public void Verbose_layout_comments_name_FreePDF()
    {
        using var document = new PdfDocument();
        document.Options.Layout = PdfWriterLayout.Verbose;
        document.AddPage();

        var text = TestPdf.Latin1.GetString(Save(document));

        Assert.Contains($"% FreePDF {PdfSharpProductVersionInformation.Version} (verbose layout)\n", text);
        Assert.Contains($"% Created with FreePDF {Version} (", text);
        // Object comments name CLR types in the PdfSharp namespace; the file comments name the product.
        foreach (var line in text.Split('\n').Where(l => l.StartsWith("%", StringComparison.Ordinal)))
            AssertNoUpstreamName(line);
    }

    [Fact]
    public async Task A_signature_names_FreePDF_as_the_signing_application()
    {
        Assert.Equal($"FreePDF {Url}", new DigitalSignatureOptions().AppName);

        using var document = new PdfDocument();
        document.Options.Layout = PdfWriterLayout.Compact;
        document.AddPage();
        DigitalSignatureHandler.ForDocument(document, new PlaceholderSigner(),
            new DigitalSignatureOptions { AppearanceHandler = new EmptyAppearance() });

        using var saved = new MemoryStream();
        await document.SaveAsync(saved);

        var text = TestPdf.Latin1.GetString(saved.ToArray());
        Assert.Contains("/Prop_Build", text);
        AssertNoUpstreamName(text);
    }

    static PdfDocument Open(string producer)
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            $"<< /Producer {producer} /Creator (Synthetic application) >>" }, _ => "/Info 4 0 R");
        var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        document.Options.Layout = PdfWriterLayout.Compact;
        return document;
    }

    static byte[] Save(PdfDocument document)
    {
        using var saved = new MemoryStream();
        document.Save(saved);
        return saved.ToArray();
    }

    static void AssertNoUpstreamName(string text)
    {
        Assert.DoesNotContain("pdfsharp", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("empira", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("migradoc", text, StringComparison.OrdinalIgnoreCase);
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
