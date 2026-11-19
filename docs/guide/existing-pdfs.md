# Existing PDF files

`PdfReader.Open` opens a file. The mode says what you will do with it:

| Mode | For |
| --- | --- |
| `Modify` | changing the document and saving it again |
| `Import` | reading it, or copying its pages into another document |
| `Append` | saving changes as an incremental update, which keeps existing signatures valid |

`ReadOnly` and `InformationOnly` are obsolete and not implemented; use `Import` to read a file.

## Draw on existing pages

<!-- snippet: stamp-pages -->
```csharp
using (var document = PdfReader.Open("report.pdf", PdfDocumentOpenMode.Modify))
{
    var font = new XFont("Arial", 60, XFontStyleEx.Bold);
    var translucentRed = new XSolidBrush(XColor.FromArgb(60, 255, 0, 0));

    foreach (var page in document.Pages)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var centre = new XPoint(page.Width.Point / 2, page.Height.Point / 2);
        gfx.RotateAtTransform(-45, centre);
        gfx.DrawString("COPY", font, translucentRed, centre, XStringFormats.Center);
    }

    document.Save("report-copy.pdf");
}
```

`XGraphicsPdfPageOptions.Append` draws on top of the page's content; `Prepend` draws underneath it.

## Combine files

<!-- snippet: import-pages -->
```csharp
using var combined = new PdfDocument();
foreach (var path in new[] { "a.pdf", "b.pdf" })
{
    using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    combined.AppendPages(source);   // all pages, with their form fields, links and bookmarks
}

using (var source = PdfReader.Open("b.pdf", PdfDocumentOpenMode.Import))
    combined.AddPage(source.Pages[0]);   // one page

combined.Save("combined.pdf");
```

`AppendPages` keeps form fields, links and bookmarks working in the new file. `AddPage(page)` copies one
page the way PDFsharp 6.2.4 did. The README has more on
[merging and splitting](https://github.com/io7/FreePDF#merge-and-split-with-forms-bookmarks-and-links).

## Files with a password

`PdfReader.Open(path, password, PdfDocumentOpenMode.Import)` opens a file that has a user password. To
change such a file (`Modify` or `Append`), give the owner password.

## Signed files

Saving a signed file in `Modify` mode rewrites it and breaks its signatures. Use `Append` mode; see
[the README](https://github.com/io7/FreePDF#fill-in-or-sign-a-signed-pdf-without-breaking-its-signatures).
