// Original synthetic corpus producer, using FreePDF's public APIs only.
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;

if (args.Length != 2) throw new ArgumentException("CorpusGenerator MANIFEST.jsonl OUTPUT_DIRECTORY | --fixtures OUTPUT_DIRECTORY");
GlobalFontSettings.FontResolver = new CorpusFontResolver();
if (args[0] == "--fixtures") { AccessibilityFixtures.Write(args[1]); return; }
Directory.CreateDirectory(args[1]);
foreach (var line in File.ReadLines(args[0]))
{
    using var json = JsonDocument.Parse(line);
    var c = json.RootElement;
    var id = c.GetProperty("id").GetString()!;
    // Manifest is generated locally; still disallow path traversal when reused independently.
    if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^case-[0-9]{6}$")) throw new ArgumentException("Invalid case ID.");
    using var document = new PdfDocument();
    document.Info.Title = id;
    document.Options.CompressContentStreams = c.GetProperty("compress").GetBoolean();
    var tags = UAManager.ForDocument(document).StructureBuilder;
    var size = c.GetProperty("fontSize").GetInt32();
    var margin = c.GetProperty("margin").GetInt32();
    var style = c.GetProperty("style").GetString() switch { "bold" => XFontStyleEx.Bold, "italic" => XFontStyleEx.Italic, _ => XFontStyleEx.Regular };
    var font = new XFont("Corpus Sans", size, style);
    var color = c.GetProperty("color").GetString()!;
    var brush = new XSolidBrush(XColor.FromArgb(Convert.ToInt32(color[1..3], 16), Convert.ToInt32(color[3..5], 16), Convert.ToInt32(color[5..7], 16)));
    foreach (var lines in c.GetProperty("lines").EnumerateArray())
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(c.GetProperty("width").GetDouble());
        page.Height = XUnit.FromPoint(c.GetProperty("height").GetDouble());
        using var gfx = XGraphics.FromPdfPage(page);
        tags.BeginElement(PdfBlockLevelElementTag.Heading1);
        gfx.DrawString(lines[0].GetString()!, font, brush, margin, margin + size);
        tags.End();
        var layout = c.GetProperty("layout").GetString();
        if (layout == "list") tags.BeginElement(PdfBlockLevelElementTag.List);
        if (layout == "table") tags.BeginBlockLevelElement("Table");
        for (int row = 1; row < lines.GetArrayLength(); row++)
        {
            double y = margin + size + row * size * 1.8;
            if (layout == "list") { tags.BeginElement(PdfBlockLevelElementTag.ListItem); tags.BeginElement(PdfBlockLevelElementTag.ListBody); }
            else if (layout == "table") { tags.BeginBlockLevelElement("TR"); tags.BeginBlockLevelElement("TD"); }
            else tags.BeginElement(PdfBlockLevelElementTag.Paragraph);
            gfx.DrawString(lines[row].GetString()!, font, brush, margin + (layout == "list" ? 12 : 0), y);
            tags.End();
            if (layout is "list" or "table") tags.End();
            if (layout == "table")
            {
                tags.BeginArtifact();
                gfx.DrawLine(XPens.Gray, margin, y + 3, page.Width.Point - margin, y + 3);
                tags.End();
            }
        }
        if (layout is "list" or "table") tags.End();
        var box = new XRect(margin, page.Height.Point - margin - 36, 60, 30);
        tags.BeginElement(PdfIllustrationElementTag.Figure, "Synthetic " + c.GetProperty("illustration").GetString(), box);
        switch (c.GetProperty("illustration").GetString())
        {
            case "ellipse": gfx.DrawEllipse(brush, box); break;
            case "image":
                using (var stream = typeof(CorpusFontResolver).Assembly.GetManifestResourceStream("CorpusImage.png")!)
                using (var image = XImage.FromStream(stream)) gfx.DrawImage(image, box);
                break;
            default: gfx.DrawRectangle(brush, box); break;
        }
        tags.End();
    }
    using var output = new FileStream(Path.Combine(args[1], id + ".pdf"), FileMode.CreateNew);
    document.Save(output);
}

sealed class CorpusFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("CorpusFont", bold, italic);
    public byte[] GetFont(string faceName)
    {
        using var stream = typeof(CorpusFontResolver).Assembly.GetManifestResourceStream("CorpusFont.ttf")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
}
