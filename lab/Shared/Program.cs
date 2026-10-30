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
                Fields = fields?.Elements.Count ?? 0, Metadata = reopened.Catalog.Elements.ContainsKey("/Metadata")
            };
            File.WriteAllText(Path.Combine(args[1], "structure.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static PdfDocument Create(string id)
    {
        if (id is "paragraphs" or "tables" or "merged-table") return Layout(id);
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
            case "forms": AddCheckbox(doc, page); gfx.DrawString("Synthetic checkbox", font, XBrushes.Black, 60, 60); break;
            default: gfx.DrawString("FreePDF / " + id, font, XBrushes.Black, 24, 50); break;
        }
        return doc;
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
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("fixed", bold, italic);
        public byte[] GetFont(string faceName) { using var input = typeof(Program).Assembly.GetManifestResourceStream("LabFont.ttf")!; using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray(); }
    }
}
