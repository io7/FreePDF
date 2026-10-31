// Original application-level release fixtures. No customer data; MIT.
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using MigraDoc.RtfRendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.Attachments;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.PdfA;
using PdfSharp.Pdf.Signatures;

static class ReleaseWorkflows
{
    const string Attachment = "<invoice id=\"INV-2026-001\" total=\"5200.00\" currency=\"NOK\"/>";

    internal static void Run(string output, string certificatePath, string version)
    {
        Directory.CreateDirectory(output);
        var manifest = new XElement("releaseChecks", new XAttribute("version", version), new XAttribute("runtime", Environment.Version));
        void Record(string name, int pages, string[] text, string profile = "", string password = "", string digest = "", bool attachment = false) =>
            manifest.Add(new XElement("document", new XAttribute("file", name + ".pdf"), new XAttribute("pages", pages),
                new XAttribute("pdfa", profile), new XAttribute("password", password), new XAttribute("signature", digest),
                text.Select(t => new XElement("text", t)),
                attachment ? new XElement("attachment", new XAttribute("name", "invoice.xml"), Attachment) : null));

        var invoice = new Document();
        var normal = invoice.Styles[StyleNames.Normal] ?? throw new InvalidDataException("Default document style missing.");
        normal.Font.Name = "Package Font";
        normal.Font.Size = 10;
        var section = invoice.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.AddParagraph("Invoice INV-2026-001").Format.Font.Size = 20;
        section.AddParagraph("Synthetic invoice: ÆØÅ – Café");
        section.Headers.Primary.AddParagraph("FreePDF application trial");
        section.Footers.Primary.AddParagraph().AddPageField();
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.AddColumn(Unit.FromCentimeter(3)); table.AddColumn(Unit.FromCentimeter(8)); table.AddColumn(Unit.FromCentimeter(3));
        var heading = table.AddRow(); heading.HeadingFormat = true;
        heading.Cells[0].AddParagraph("Line"); heading.Cells[1].AddParagraph("Description"); heading.Cells[2].AddParagraph("NOK");
        for (var i = 1; i <= 64; i++)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph("Line " + i.ToString("000", CultureInfo.InvariantCulture));
            row.Cells[1].AddParagraph("Original fixture item with enough text to exercise wrapping.");
            row.Cells[2].AddParagraph((i * 2.5m).ToString("F2", CultureInfo.InvariantCulture));
        }
        section.AddParagraph("Total: 5200.00 NOK");
        var renderer = new PdfDocumentRenderer { Document = invoice }; renderer.RenderDocument();
        using var invoicePdf = renderer.PdfDocument;
        var invoicePages = invoicePdf.PageCount;
        Expect(invoicePages >= 2, "Invoice did not exercise pagination.");
        invoicePdf.Info.Title = "Original invoice fixture";
        AddAttachment(invoicePdf);
        invoicePdf.Save(Path.Combine(output, "invoice.pdf"));
        new RtfDocumentRenderer().Render(invoice, Path.Combine(output, "invoice.rtf"), "");
        using (var reopened = PdfReader.Open(Path.Combine(output, "invoice.pdf"), PdfDocumentOpenMode.Modify))
        {
            Expect(reopened.Info.Title == "Original invoice fixture", "Invoice title changed.");
            var files = EmbeddedFilesManager.ForDocument(reopened);
            Expect(files.FileCount == 1 && Encoding.UTF8.GetString(files.GetEmbeddedFileInfo(0).Data) == Attachment, "Invoice attachment changed.");
        }
        Record("invoice", invoicePages, new[] { "Invoice INV-2026-001", "Line 064", "5200.00 NOK", "ÆØÅ" }, attachment: true);

        using (var imported = PdfReader.Open(Path.Combine(output, "invoice.pdf"), PdfDocumentOpenMode.Import))
        using (var merged = new PdfDocument())
        {
            foreach (var page in imported.Pages) merged.AddPage(page);
            using var appendix = Basic("APPENDIX ORIGINAL");
            using var bytes = new MemoryStream(); appendix.Save(bytes); bytes.Position = 0;
            using var appendixInput = PdfReader.Open(bytes, PdfDocumentOpenMode.Import);
            merged.AddPage(appendixInput.Pages[0]);
            using (var graphics = XGraphics.FromPdfPage(merged.Pages[0], XGraphicsPdfPageOptions.Append))
                graphics.DrawString("COPY STAMP", new XFont("Package Font", 11), XBrushes.DarkBlue, 30, 35);
            merged.Save(Path.Combine(output, "merged.pdf"));
            using var reopened = PdfReader.Open(Path.Combine(output, "merged.pdf"), PdfDocumentOpenMode.Modify);
            Expect(reopened.PageCount == invoicePages + 1, "Merge lost pages.");
            Record("merged", reopened.PageCount, new[] { "Invoice INV-2026-001", "Line 064", "APPENDIX ORIGINAL", "COPY STAMP" });
        }

        foreach (var bits in new[] { 128, 256 })
        {
            var name = "encrypted-aes" + bits;
            using var encrypted = Basic("ENCRYPTED ORIGINAL " + bits);
            if (bits == 128) encrypted.SecurityHandler.SetEncryptionToV4UsingAES(); else encrypted.SecurityHandler.SetEncryptionToV5();
            encrypted.SecuritySettings.UserPassword = "fixture-reader";
            encrypted.SecuritySettings.OwnerPassword = "fixture-owner";
            var path = Path.Combine(output, name + ".pdf"); encrypted.Save(path);
            using var reopened = PdfReader.Open(path, "fixture-owner", PdfDocumentOpenMode.Modify);
            Expect(reopened.PageCount == 1, "Encrypted PDF lost its page.");
            try { using var wrong = PdfReader.Open(path, "wrong-password", PdfDocumentOpenMode.Modify); throw new InvalidDataException("Wrong password was accepted."); }
            catch (PdfReaderException) { }
            Record(name, 1, new[] { "ENCRYPTED ORIGINAL " + bits }, password: "fixture-reader");
        }

        foreach (var part in new[] { 1, 2, 3 })
        {
            var name = "pdfa-" + part + "b";
            using var archival = Basic("ARCHIVAL ORIGINAL " + part, part);
            if (part == 3) AddAttachment(archival);
            archival.Save(Path.Combine(output, name + ".pdf"));
            Record(name, 1, new[] { "ARCHIVAL ORIGINAL " + part }, profile: part + "b", attachment: part == 3);
        }

#pragma warning disable SYSLIB0057 // Cross-target test consumer includes Framework.
        using var certificate = new X509Certificate2(certificatePath, "test-only");
#pragma warning restore SYSLIB0057
        foreach (var digest in new[] { PdfMessageDigestType.SHA256, PdfMessageDigestType.SHA384, PdfMessageDigestType.SHA512 })
        {
            var name = "signed-" + digest.ToString().ToLowerInvariant();
            using var signed = Basic("SIGNED ORIGINAL " + digest);
            DigitalSignatureHandler.ForDocument(signed, new PdfSharpDefaultSigner(certificate, digest), new DigitalSignatureOptions
            {
                AppearanceHandler = new EmptyAppearance(), Rectangle = new XRect(20, 100, 150, 30),
                Reason = "Original release validation fixture", AppName = "FreePDF"
            });
            var path = Path.Combine(output, name + ".pdf"); signed.Save(path);
            VerifySignature(File.ReadAllBytes(path));
            Record(name, 1, new[] { "SIGNED ORIGINAL " + digest }, digest: digest.ToString());
            var changed = File.ReadAllBytes(path);
            Expect(Encoding.ASCII.GetString(changed, 0, 7) == "%PDF-1.", "Unexpected PDF header for tamper control.");
            changed[7] = changed[7] == (byte)'7' ? (byte)'6' : (byte)'7';
            Directory.CreateDirectory(Path.Combine(output, "negative"));
            File.WriteAllBytes(Path.Combine(output, "negative", name + "-tampered.pdf"), changed);
        }
        new XDocument(manifest).Save(Path.Combine(output, "consumer-results.xml"));
        Console.WriteLine("PASS: invoice/RTF, attachments, merge/stamp, AES-128/256, PDF/A generation and three cryptographically verified PDF signatures.");
    }

    static PdfDocument Basic(string text, int archivalPart = 0)
    {
        var document = new PdfDocument();
        if (archivalPart != 0) PdfAManager.ForDocument(document).SetFormat(PdfAFormat.ForcePdfAFormat(archivalPart, 'B'));
        var page = document.AddPage();
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawString(text, new XFont("Package Font", 14), XBrushes.Black, 30, 60);
        return document;
    }

    static void AddAttachment(PdfDocument document) => EmbeddedFilesManager.ForDocument(document).AddFile(new EmbeddedFileInfo
    {
        NamesKey = "invoice.xml", FileName = "invoice.xml", FileType = "application/xml", Description = "Original synthetic invoice data",
        Data = Encoding.UTF8.GetBytes(Attachment), AFRelationship = PdfAFRelationship.Data,
        ModificationTime = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero)
    });

    static void VerifySignature(byte[] pdf)
    {
        var match = Regex.Match(Encoding.ASCII.GetString(pdf), @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        Expect(match.Success, "Signature byte range missing.");
        var ranges = Enumerable.Range(1, 4).Select(i => int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        Expect(ranges[0] == 0 && ranges[2] + ranges[3] == pdf.Length, "Signature does not cover the complete document.");
        var hex = Encoding.ASCII.GetString(pdf, ranges[1] + 1, ranges[2] - ranges[1] - 2);
        var signature = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        var content = pdf.Take(ranges[1]).Concat(pdf.Skip(ranges[2]).Take(ranges[3])).ToArray();
        var cms = new SignedCms(new ContentInfo(content), true); cms.Decode(signature); cms.CheckSignature(true);
        content[0] ^= 1;
        var tampered = new SignedCms(new ContentInfo(content), true); tampered.Decode(signature);
        try { tampered.CheckSignature(true); throw new InvalidDataException("Tampered signature was accepted."); }
        catch (CryptographicException) { }
    }

    static void Expect(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    sealed class EmptyAppearance : IAnnotationAppearanceHandler { public void DrawAppearance(XGraphics graphics, XRect rectangle) { } }
}
