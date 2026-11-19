# FreePDF guide

This guide shows how to make and change PDF files with FreePDF's drawing API, the `PdfSharp.Drawing`
and `PdfSharp.Pdf` namespaces that PDFsharp users know. None of it needs MigraDoc.

Every example on these pages is a test in
[src/samples/FreePDF.Samples](https://github.com/io7/FreePDF/tree/main/src/samples/FreePDF.Samples):
CI compiles and runs it and checks the PDF it writes.

1. [Getting started](getting-started.md): install the package, make a PDF, save it to a file or a stream.
2. [Pages and coordinates](pages.md): page sizes, orientation, units, and where (0, 0) is.
3. [Text](text.md): alignment, measuring text, paragraphs that wrap.
4. [Fonts](fonts.md): where fonts come from, fonts you ship, font resolvers.
5. [Shapes and colours](shapes.md): lines, rectangles, ellipses, polygons, transparency, rotation.
6. [Images](images.md): JPEG, PNG and BMP files, scaled to fit.
7. [Documents with many pages](multi-page.md): a table that continues on new pages, page numbers.
8. [Existing PDF files](existing-pdfs.md): draw on the pages of a file, combine files.
9. [Links and bookmarks](links-and-bookmarks.md): web links, links to pages, the bookmark panel.
10. [Metadata and passwords](security.md): document properties, passwords and permissions.

## More

- The [API reference](https://io7.github.io/FreePDF/reference.html) lists every public type and member,
  with the same text IntelliSense shows.
- [Text extraction](../text-extraction.md) reads the text of existing pages.
- [Migrating from PDFsharp 6.2.4](../migrating-to-7.md) lists what changes in an upgrade.
- Forms, signatures, Factur-X invoices, accessible PDF (PDF/UA), barcodes, merging and splitting: see the
  sections of the [README](https://github.com/io7/FreePDF#readme).
- MigraDoc (`io7.FreePDF.Layout`) lays out paragraphs, tables, headers and footers for you; its types are in
  the API reference under `MigraDoc.DocumentObjectModel` and `MigraDoc.Rendering`.
