// Identical authored fixtures are compiled against each engine in a separate process.
using System.Reflection;
using System.Text;
using System.Text.Json;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Attachments;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Metadata;
using PdfSharp.Pdf.PdfA;

namespace FreePDF.Lab.Fixtures;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || !CaseCatalog.All.Any(c => c.Id == args[0]))
                throw new ArgumentException("Usage: Generator <case-id> <output-directory>");
            GlobalFontSettings.FontResolver = new FixtureFontResolver();
            Directory.CreateDirectory(args[1]);
            using var document = Create(args[0]);
            document.Info.Title = "FreePDF lab / " + args[0];
            document.Info.Elements.SetString("/CreationDate", "D:20200102030405Z");
            document.Info.Elements.SetString("/ModDate", "D:20200102030405Z");
            var path = Path.Combine(args[1], "document.pdf");
            document.Save(path);
            using var reopened = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
            var attachments = EmbeddedFilesManager.ForDocument(reopened);
            var fields = reopened.Catalog.Elements.GetDictionary("/AcroForm")?.Elements.GetArray("/Fields");
            var attachment = attachments.FileCount == 0 ? null : attachments.GetEmbeddedFileInfo(0);
            var summary = new {
                CaseId = args[0], Library = typeof(PdfDocument).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Runtime = Environment.Version.ToString(), Pages = reopened.PageCount, Title = reopened.Info.Title,
                Attachments = attachments.FileCount, AttachmentText = attachment == null ? null : Encoding.UTF8.GetString(attachment.Data),
                Fields = fields?.Elements.Count ?? 0, Metadata = reopened.Catalog.Elements.ContainsKey("/Metadata"),
                ReaderRejection = args[0].StartsWith("reader-", StringComparison.Ordinal) ? "PdfReaderException" : null,
                FixtureFontSha256 = args[0] == "nested-glyphs" ? ResourceHash("CompositeFont.ttf") : null,
                Semantics = args[0] == "semantic-roundtrip" ? SemanticSummary(reopened) : null,
                ContentRectangles = args[0] == "incremental-content" ? ContentReader.ReadContent(reopened.Pages[0]).OfType<COperator>().Count(op => op.Name == "re") : (int?)null
            };
            File.WriteAllText(Path.Combine(args[1], "structure.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static PdfDocument Create(string id)
    {
        if (id is "incremental-content" or "shared-ownership" or "semantic-roundtrip")
        {
            var bytes = id == "incremental-content" ? PreservationFixtures.Incremental()
                : id == "shared-ownership" ? PreservationFixtures.SharedContent(true) : PreservationFixtures.Semantic();
            var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
            if (id == "shared-ownership")
            {
                var first = document.Pages[0].Contents.Elements.GetDictionary(0)!;
                var second = document.Pages[1].Contents.Elements.GetDictionary(0)!;
                if (first.Stream!.UnfilteredValue.Length == 0 || !ReferenceEquals(first, second))
                    throw new InvalidDataException("Accessing another page invalidated the existing content object.");
            }
            if (id == "incremental-content" && !ContentReader.ReadContent(document.Pages[0]).OfType<COperator>().Any(op => op.Name == "re"))
                throw new InvalidDataException("Incremental update lost the page's original rectangle.");
            if (id == "semantic-roundtrip") _ = SemanticSummary(document);
            return document;
        }
        if (id is "paragraphs" or "tables" or "merged-table") return Layout(id);
        if (id is "inherited-page" or "stream-length" or "hybrid-xref" or "multi-stream") return Imported(id);
        if (id is "reader-trailer" or "reader-pages") VerifyRejected(id);
        var doc = new PdfDocument();
        if (id == "pdfa") PdfAManager.ForDocument(doc).SetFormat(PdfAFormat.ForcePdfAFormat(1, 'B'));
        if (id == "attachments")
        {
            MetadataManager.ForDocument(doc).Strategy = DocumentMetadataStrategy.AutoGenerate;
            EmbeddedFilesManager.ForDocument(doc).AddFile(new EmbeddedFileInfo {
                NamesKey = "fixture.xml", FileName = "fixture.xml", FileType = "application/xml",
                Data = Encoding.UTF8.GetBytes("<fixture>original synthetic data</fixture>"),
                CreationTime = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), ModificationTime = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero)
            });
        }
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(420); page.Height = XUnit.FromPoint(300);
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Lab Sans", 18);
        switch (id)
        {
            case "text":
                gfx.DrawString("FreePDF: Café, ÆØÅ, Ελληνικά", font, XBrushes.Black, 24, 50);
                gfx.DrawString("Bold simulation / 0123456789", new XFont("Lab Sans", 15, XFontStyleEx.Bold), XBrushes.DarkBlue, 24, 90);
                gfx.DrawString("Italic simulation and underline", new XFont("Lab Sans", 15, XFontStyleEx.Italic | XFontStyleEx.Underline), XBrushes.Black, 24, 130);
                break;
            case "graphics":
                gfx.DrawRectangle(XPens.DarkBlue, XBrushes.LightBlue, 20, 20, 100, 60);
                gfx.DrawEllipse(new XPen(XColors.DarkRed, 3), 150, 20, 90, 60);
                var state = gfx.Save(); gfx.TranslateTransform(100, 180); gfx.RotateTransform(25);
                gfx.IntersectClip(new XRect(0, 0, 180, 60)); gfx.DrawRectangle(XBrushes.Orange, -20, -10, 250, 90); gfx.Restore(state);
                break;
            case "transparency":
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(0, 0, 0, 0)), 20, 20, 100, 80);
                gfx.DrawRectangle(XBrushes.DarkBlue, 160, 20, 100, 80);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(100, 255, 0, 0)), 210, 60, 100, 80);
                var saved = gfx.Save(); gfx.DrawLine(new XPen(XColor.FromArgb(0, 0, 0, 0), 8), 20, 160, 160, 160); gfx.Restore(saved);
                gfx.DrawLine(XPens.Black, 20, 200, 160, 200);
                break;
            case "gradient-solid":
                gfx.DrawRectangle(new XLinearGradientBrush(new XPoint(20, 20), new XPoint(120, 120), XColors.Red, XColors.Blue), 20, 20, 100, 100);
                gfx.DrawRectangle(XBrushes.Black, 180, 20, 100, 100);
                break;
            case "paths":
                var path = new XGraphicsPath();
                for (var i = 0; i < 5; i++) { path.StartFigure(); path.AddLine(30, 30 + 40 * i, 330, 30 + 40 * i); }
                gfx.DrawPath(new XPen(XColors.DarkBlue, 3), path);
                break;
            case "images":
                using (var stream = typeof(Program).Assembly.GetManifestResourceStream("LabImage.png")!)
                using (var image = XImage.FromStream(stream)) { gfx.DrawImage(image, 20, 20, 100, 100); gfx.DrawImage(image, 160, 20, 200, 80); }
                break;
            case "indexed-png":
                gfx.DrawString("Palette alpha: transparent / half / opaque", new XFont("Lab Sans", 14), XBrushes.Black, 24, 30);
                gfx.DrawRectangle(XBrushes.LightGray, 24, 60, 320, 160);
                using (var indexed = XImage.FromStream(new MemoryStream(PalettePng.Create(4, 5, 2, true))))
                    gfx.DrawImage(indexed, 24, 60, 320, 160);
                break;
            case "nested-glyphs":
                gfx.DrawString("Nested composite: three bars", font, XBrushes.Black, 24, 35);
                gfx.DrawString("\ue000", new XFont("Lab Composite", 220), XBrushes.DarkBlue, 40, 220);
                break;
            case "reader-trailer":
            case "reader-pages":
                gfx.DrawString("Malformed PDF rejected safely", font, XBrushes.DarkGreen, 24, 55);
                gfx.DrawString(id == "reader-trailer" ? "Cyclic trailer chain" : "Cyclic page tree", font, XBrushes.Black, 24, 95);
                gfx.DrawString("This page records a rejection check.", new XFont("Lab Sans", 13), XBrushes.Black, 24, 145);
                break;
            case "forms": AddCheckbox(doc, page); gfx.DrawString("Synthetic checkbox", font, XBrushes.Black, 60, 60); break;
            default: gfx.DrawString("FreePDF / " + id, font, XBrushes.Black, 24, 50); break;
        }
        return doc;
    }

    static object SemanticSummary(PdfDocument document)
    {
        var page = document.Pages[0];
        var text = string.Concat(ContentReader.ReadContent(page).OfType<COperator>()
            .Where(op => op.Name == "Tj").SelectMany(op => op.Operands.OfType<CString>()).Select(s => s.Value));
        var note = page.Annotations[0].Contents;
        var link = page.Annotations[1].Elements.GetDictionary("/A")!.Elements.GetString("/URI");
        var field = document.Catalog.GetAcroForm()!.Elements.GetArray("/Fields")!.Elements.GetDictionary(0)!;
        var name = field.Elements.GetString("/T"); var value = field.Elements.GetString("/V");
        var names = document.Catalog.Elements.GetDictionary("/Names")!;
        var destinations = names.Elements.GetDictionary("/Dests")!.Elements.GetArray("/Names")!;
        var destination = destinations.Elements.GetString(0);
        var target = destinations.Elements.GetArray(1)!.Elements.GetObject(0);
        var files = names.Elements.GetDictionary("/EmbeddedFiles")!.Elements.GetArray("/Names")!;
        var filename = files.Elements.GetString(0);
        var data = files.Elements.GetDictionary(1)!.Elements.GetDictionary("/EF")!.Elements.GetDictionary("/F")!.Stream!.UnfilteredValue;
        var attachment = Encoding.UTF8.GetString(data);
        if (text != "Invoice 123" || note != "review note" || link != "https://example.org/invoice/123" || name != "Customer"
            || value != "Ada Example" || destination != "invoice" || !ReferenceEquals(page, target)
            || filename != "invoice.xml" || attachment != "<invoice id=\"123\"/>")
            throw new InvalidDataException("Semantic document values changed during processing.");
        return new { Text = text, Annotation = note, Link = link, FieldName = name, FieldValue = value,
            Destination = destination, DestinationPage = 1, FileName = filename, Attachment = attachment };
    }

    static void VerifyRejected(string id)
    {
        var scenario = id == "reader-trailer" ? "trailer-self" : "tree-self";
        try
        {
            using var input = new MemoryStream(ReaderFixtureData.Get(scenario));
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            _ = document.PageCount;
        }
        catch (PdfReaderException error)
        {
            var expected = id == "reader-trailer" ? "cross-reference" : "page tree";
            if (error.Message.IndexOf(expected, StringComparison.Ordinal) < 0) throw;
            Console.WriteLine("Rejected synthetic input: " + error.Message);
            return;
        }
        throw new InvalidDataException("The reader accepted a cyclic PDF.");
    }

    static PdfDocument Imported(string id)
    {
        const string red = "1 0 0 rg 30 150 80 80 re f";
        const string green = "0 0.6 0 rg 150 150 80 80 re f";
        string Stream(string value, int? length = null) => $"<< /Length {length ?? value.Length} >>\nstream\n{value}\nendstream";
        byte[] bytes;
        if (id == "hybrid-xref") bytes = PdfFixtureData.Hybrid(false);
        else if (id == "inherited-page") bytes = PdfFixtureData.Create(new[] {
            ReaderFixtureData.SimplePage[0], "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 420 300] >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>", Stream(green)
        });
        else bytes = PdfFixtureData.Create(new[] {
            ReaderFixtureData.SimplePage[0], ReaderFixtureData.SimplePage[1],
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 300] /Contents [4 0 R 5 0 R] >>",
            id == "stream-length" ? Stream(red, 100000) : Stream("1 0 0 rg 1 0 0 1 70 0 cm"),
            id == "stream-length" ? Stream(green) : Stream("40 150 60 60 re f % trailing comment")
        });
        var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        try
        {
            var page = document.Pages[0];
            if (id == "stream-length" && Encoding.ASCII.GetString(page.Contents.Elements.GetDictionary(0)!.Stream!.UnfilteredValue).TrimEnd('\r', '\n') != red)
                throw new InvalidDataException("Stream recovery consumed bytes beyond the first content stream.");
            var marker = id == "hybrid-xref" ? document.Catalog.Elements.GetString("/HybridMarker") : id;
            if (id == "hybrid-xref" && marker != "compressed marker") throw new InvalidDataException("Hybrid object was not preserved.");
            page.Width = XUnit.FromPoint(420); page.Height = XUnit.FromPoint(300);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawString(marker, new XFont("Lab Sans", 16), XBrushes.Black, 24, 35);
            if (id == "multi-stream") graphics.DrawRectangle(XBrushes.DarkBlue, 200, 90, 60, 60);
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    static string ResourceHash(string name)
    {
        using var input = typeof(Program).Assembly.GetManifestResourceStream(name)!;
        using var hash = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    static PdfDocument Layout(string id)
    {
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = "Lab Sans";
        document.Styles[StyleNames.Normal]!.Font.Size = 11;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(420); section.PageSetup.PageHeight = Unit.FromPoint(500);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(30);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(30);
        if (id == "paragraphs")
        {
            for (var i = 0; i < 24; i++) { var p = section.AddParagraph("Paragraph " + (i + 1) + ". Original fixture text wraps consistently across lines, with accents: Café ÆØÅ. "); p.Format.SpaceAfter = Unit.FromPoint(12); }
        }
        else
        {
            var table = section.AddTable(); table.Borders.Width = 0.75;
            for (var i = 0; i < 3; i++) table.AddColumn(Unit.FromPoint(120));
            for (var row = 0; row < (id == "merged-table" ? 3 : 48); row++)
            {
                var r = table.AddRow(); r.HeadingFormat = row == 0; r.Shading.Color = row % 2 == 0 ? Colors.LightBlue : Colors.White;
                for (var col = 0; col < 3; col++) r.Cells[col].AddParagraph($"Row {row + 1}, column {col + 1}");
            }
            if (id == "merged-table") { table[0, 0].MergeRight = 2; table[0, 0].MergeDown = 2; table.Rows[0].HeadingFormat = false; }
        }
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument(); return renderer.PdfDocument;
    }

    static void AddCheckbox(PdfDocument doc, PdfPage page)
    {
        var appearance = new PdfDictionary(doc);
        appearance.Elements.SetName("/Type", "/XObject"); appearance.Elements.SetName("/Subtype", "/Form");
        appearance.Elements["/BBox"] = new PdfRectangle(new XRect(0, 0, 20, 20));
        appearance.CreateStream(Encoding.ASCII.GetBytes("0 0 0 RG 1 w 1 1 18 18 re S 4 10 m 8 4 l 16 16 l S"));
        doc.Internals.AddObject(appearance);
        var normal = new PdfDictionary(doc); normal.Elements["/Yes"] = appearance.Reference!;
        var ap = new PdfDictionary(doc); ap.Elements["/N"] = normal;
        var widget = new PdfDictionary(doc);
        widget.Elements.SetName("/Type", "/Annot"); widget.Elements.SetName("/Subtype", "/Widget"); widget.Elements.SetName("/FT", "/Btn");
        widget.Elements.SetString("/T", "approved"); widget.Elements.SetName("/V", "/Yes"); widget.Elements.SetName("/AS", "/Yes");
        widget.Elements["/Rect"] = new PdfRectangle(new XRect(24, 235, 20, 20)); widget.Elements["/AP"] = ap;
        doc.Internals.AddObject(widget);
        page.Elements["/Annots"] = new PdfArray(doc, widget.Reference!);
        var form = new PdfDictionary(doc); form.Elements["/Fields"] = new PdfArray(doc, widget.Reference!);
        doc.Catalog.Elements["/AcroForm"] = form;
    }

    sealed class FixtureFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(familyName == "Lab Composite" ? "composite" : "fixed", bold, italic);
        public byte[] GetFont(string faceName) { using var input = typeof(Program).Assembly.GetManifestResourceStream(faceName == "composite" ? "CompositeFont.ttf" : "LabFont.ttf")!; using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray(); }
    }
}
