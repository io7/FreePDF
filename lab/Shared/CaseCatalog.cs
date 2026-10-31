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
        new() { Id = "pdfa", Title = "PDF/A generation path (conformance unverified)" },
        new() { Id = "reader-trailer", Title = "Cyclic trailer rejection", KnownReferenceDefect = "Preview 1 loops over a cyclic Prev chain; upstream issue 266. The candidate page records a successful rejection check, not a rendering of the malformed input." },
        new() { Id = "reader-pages", Title = "Cyclic page-tree rejection", KnownReferenceDefect = "Preview 1 cannot safely traverse a cyclic page tree; upstream issue 361. The candidate page records a successful rejection check." },
        new() { Id = "inherited-page", Title = "Inherited page dimensions", KnownReferenceDefect = "Preview 1 initializes pages before inheriting their dimensions; upstream issue 375." },
        new() { Id = "stream-length", Title = "Incorrect stream length recovery", KnownReferenceDefect = "Preview 1 can consume a following object when Length is too large; upstream issue 394." },
        new() { Id = "hybrid-xref", Title = "Hybrid cross-reference import", KnownReferenceDefect = "Preview 1 ignores the supplemental XRefStm when opening hybrid PDFs; upstream issue 388." },
        new() { Id = "multi-stream", Title = "Editing multiple content streams", KnownReferenceDefect = "Preview 1 can leave the graphics state open after a trailing comment; upstream issue 345." },
        new() { Id = "indexed-png", Title = "Palette PNG transparency", KnownReferenceDefect = "Preview 1 writes incorrect alpha values for 4-bit palette PNGs; upstream issue 392." },
        new() { Id = "nested-glyphs", Title = "Nested composite font glyphs", KnownReferenceDefect = "Preview 1 omits nested components from a font subset; upstream issue 348." }
    };
}
