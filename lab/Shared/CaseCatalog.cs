// FreePDF regression lab. MIT; fonts have their own retained licence.
namespace FreePDF.Lab.Fixtures;

public sealed class CaseSpec
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string KnownReferenceDefect { get; set; } = "";
}

public static class CaseCatalog
{
    public static readonly CaseSpec[] All = {
        new() { Id = "text", Title = "Text, accents and font styles" },
        new() { Id = "graphics", Title = "Shapes, transforms and clipping" },
        new() { Id = "transparency", Title = "Opacity and state restoration" },
        new() { Id = "gradient-solid", Title = "Gradient followed by solid fill", KnownReferenceDefect = "Preview 1 fails to restore the solid fill color space." },
        new() { Id = "paths", Title = "Disconnected path figures", KnownReferenceDefect = "Preview 1 connects figures after StartFigure; upstream issue 396." },
        new() { Id = "images", Title = "PNG placement and scaling" },
        new() { Id = "paragraphs", Title = "Paragraph layout and page breaks" },
        new() { Id = "tables", Title = "Tables, borders and repeated headings" },
        new() { Id = "merged-table", Title = "Fully merged table cells", KnownReferenceDefect = "Preview 1 throws on full-width vertical merges; upstream issue 358." },
        new() { Id = "attachments", Title = "Embedded file and XMP metadata" },
        new() { Id = "forms", Title = "Checkbox field and annotation appearance" },
        new() { Id = "pdfa", Title = "PDF/A generation path (conformance unverified)" }
    };
}
