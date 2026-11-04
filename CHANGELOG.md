# FreePDF changelog

## Unreleased

- Saving a document opened with `Append` refuses changes that its signatures do
  not permit, with `InvalidOperationException` before anything is written:
  changes to signed signature fields and to fields locked by FieldMDP or a
  signature field's `/Lock` (the field, the fields above it, its widget and what
  they refer to, also removing or moving them), and what a certification
  signature's DocMDP permission or a `/Lock` with `/P` does not permit (P=1 no
  changes; P=2 filling in fields, signing and the document information; P=3 also
  annotations). Page content, pages, bookmarks and other catalog entries cannot
  change in a certified document. Without a certification, everything but
  signed and locked fields can change, as before. `Save(path)` in `Append` mode
  opens the file first, saves in memory and replaces the file through a
  temporary one, so a refused or failed save leaves the file as it was, also
  when it is the file the document was opened from.
- An `Append`-mode update no longer adds `/PageMode /UseOutlines` to the catalog
  of a document with bookmarks; it changed the catalog, which validators report.
- Selecting an option of a combo box or a single-selection list box no longer
  adds the selected index (`/I`); ISO 32000-1 says it should not be used for such
  fields. Adding it changed the field's keys, and pyHanko judged a signature on
  the form invalid after a choice field was filled. `/I` is still written for
  multiple-selection lists, when options share an export value (the index then
  identifies the option, and `SelectedIndex` reads it), and when the field
  already has one.
- A new signature field's widget gets the annotation flags Print and Locked
  (`/F 132`). They were written as field flags (`/Ff 132`), so the signature's
  appearance was not printed.
- An `Append`-mode update no longer writes the values a page inherits from the
  page tree (`/Resources`, `/MediaBox`, `/CropBox`, `/Rotate`) into the pages it
  updates, for example when a signature or a comment is added. Opening a
  document copies these values onto its pages; writing them changed the page's
  keys, and pyHanko then judged the earlier signatures invalid ("Dict keys
  differ"). Values the application changes are still written. Saving 15 of
  2,950 corpus files with a change on the first page wrote such keys in 7.4.0.
- Drawing on a page whose inherited `/Resources` is a direct dictionary threw
  `NullReferenceException` in `Modify` and `Append` mode; the copy on the page
  now belongs to the document.
- Saving no longer adds a `/Dest` to bookmarks that lead to their page through
  a GoTo action (`/A`); ISO 32000-1 permits only one of the two. A bookmark keeps
  its action while its destination is as read from the file. Setting a new
  `DestinationPage`, view or position writes a `/Dest` and removes the action
  read from the file, whatever kind of action it was. An action the application
  puts into `/A` is kept and the bookmark gets no `/Dest`; a bookmark whose
  action the application removed gets a `/Dest` to its page.

## 7.4.0

- `PdfDocumentOpenMode.Append` opens a document so that saving it writes an
  incremental update: the original bytes unchanged, then only the new and
  changed objects, a cross-reference table or stream (as the file uses) and a
  trailer pointing back to the previous one. Signatures in the file stay valid,
  so a signed form can be filled in and a signed document signed again; the new
  signature covers the whole file. Object numbers, identifiers and the page tree
  are kept; a document with no changes is saved as it was. Encrypted documents
  and files whose cross-reference table names the wrong objects are refused, a
  password cannot be added, and pages cannot be inserted, removed or moved when
  the page tree is nested. PDFsharp 6.2.4 has no incremental saving.
- Opening a form no longer sets a new modification date (`/M`) on fields that
  are also widgets. The date was written on the next save although the field had
  not changed, and signature validators count it as an unexplained change.
- Pages of a document opened with `Append` get no transparency group (`/Group`)
  added when saved; other documents keep getting one, as before.

## 7.3.0

- Saved documents name FreePDF as the application that wrote them. The
  Producer is now `FreePDF 7.x.y+<commit>` and the default Creator
  `FreePDF 7.x.y+<commit> (https://github.com/io7/FreePDF)`; documents from the
  layout engine get the Creator `FreePDF Layout 7.x.y+<commit> (…)`. The XMP
  metadata, the file's closing comment, the verbose layout header and a
  signature's application name (`DigitalSignatureOptions.AppName`) follow. Up to
  7.2.0 they said `PDFsharp 7.2.0`, `empira MigraDoc 7.2.0` and
  `www.pdfsharp.com`, which reads like empira's AGPL PDFsharp 7. The
  `PdfSharpProductVersionInformation` and `MigraDocProductVersionInformation`
  constants (title, product, company, URL, copyright) changed with them; empira's
  copyright is kept.
- Saving a file another PDFsharp version wrote now records FreePDF as the
  Producer and keeps the old value, as for any other application:
  `FreePDF 7.x.y+<commit> (Original: PDFsharp 6.2.4 (www.pdfsharp.com))`. Before, a
  Producer starting with "PDFsharp" was left as it was.
- `PdfDocument.AppendPages(source[, startIndex, pageCount][, options])` merges
  and `PdfDocument.ExtractPages(startIndex, pageCount[, options])` splits, with
  form fields, bookmarks and links. Fields with a widget on a copied page join
  the document's form with their own default appearance and fonts (a name
  already in use is renamed `name_2` and reported in
  `PdfPageImportResult.RenamedFields`); links, button GoTo actions and bookmarks
  lead to the copied pages, with named destinations written out, and those that
  lead to pages left out are removed, while those that lead elsewhere are kept;
  `PdfPageImportOptions.BookmarkTitle` groups each source under one bookmark.
  Annotations no longer pull in hidden copies of other pages, fonts and images
  are shared between pages, annotations and repeated copies, and the pages keep
  their transparency group, tab order and user unit. Everything is copied before
  anything is attached, so a failure on a damaged source leaves the document as
  it was. `AddPage(page)` and `Pages.InsertRange` are unchanged: they drop the
  form and the bookmarks, as in 6.2.4.
- An annotation whose `/Subtype` is a number no longer stops the document from
  opening; it is read as an unknown annotation.
- A bookmark colour (`/C`) outside 0 to 1, which some producers write as 0 to
  255, is clamped instead of throwing `ArgumentException` when the outline is
  read, and colours are rounded so they read back as written.

## 7.2.0

- Text works without a font resolver in the portable packages. With no
  `GlobalFontSettings.FontResolver` set, FreePDF uses the TrueType fonts
  installed on Windows, Linux and macOS, after the Windows font table and the
  fallback font resolver. Missing common families get a substitute with the same
  widths (Liberation Sans, Serif and Mono for Arial, Times New Roman and Courier
  New; Carlito and Caladea for Calibri and Cambria), else a DejaVu or Noto font
  of the same kind, with a logged warning. Fonts whose licence forbids embedding
  are skipped, and the error says so. New: `GlobalFontSettings.AddFontDirectory`
  for fonts deployed with the application, and `GlobalFontSettings.UseSystemFonts`
  (set it to false for 6.2.4's behaviour). Before, MigraDoc stopped with "The
  font 'Courier New' cannot be resolved" on a machine without Microsoft's fonts.
- `GlobalFontSettings.ResetFontManagement()` now also forgets resolved typefaces
  and font families; before, a typeface resolved before the reset was still used
  after it, even with a different font resolver.
- `PdfDocument.Flatten()` flattens the form: every visible field is drawn into
  its page as it appears, placed on the widget's rectangle as ISO 32000-1
  12.5.5 describes, and the form and its widgets are removed; other annotations
  stay. Fields without an up-to-date appearance get one first, and a value that
  cannot be drawn stops the call before anything changes. PDFsharp 6.2.4's
  `Flatten()` only marked the top-level fields read-only, and FreePDF 7.0 threw
  `InvalidOperationException`, so 6.2.4 code that called it compiled and then
  failed at run time.
- Tagged PDF (`PdfDocumentRenderer.TaggedPdf`): a table that runs over several
  pages is one Table element, and the heading rows repeated on later pages are
  artifacts, so a screen reader meets the table and its headers once. Before,
  each page's part was a separate Table with its own header cells.

## 7.1.1

- Filled form fields whose font is a standard font without a `/Widths` array
  (the usual `/Helv`) now measure text with Adobe's metrics for the twelve Latin
  standard fonts instead of an estimated width, so centred, right-aligned,
  wrapped and auto-sized text is placed as a viewer would place it. Arial, Times
  New Roman and Courier New, with or without a subset prefix or style suffix,
  use the metrics of Helvetica, Times and Courier, with which they share widths.
  Adobe's notice ships in each package as `THIRD-PARTY/Adobe-Core14-AFM.txt`.

## 7.1.0

- Fill forms through the v7 form model: `PdfFormFields["address.city"]`,
  `TryGetField` and `Count`; `PdfFormTextField.Text`, `PdfFormCheckBoxField.Checked`,
  `PdfFormRadioButtonField.SelectedIndex`/`SelectedValue`/`Options` and
  `PdfFormChoiceField.SelectedIndex`/`SelectedValue`/`Options`/`ExportValues`;
  `PdfForm.NeedAppearances`. Filling draws the appearance of text and choice
  fields from the field's default appearance (font, size, colour, alignment) and
  the widget's border and background, and switches check boxes and radio buttons
  to the appearance states the form defines. Named kids now get the field type
  they inherit (a `city` under a text field `address` is a `PdfFormTextField`).
  The migration guide maps the 6.2.4 `PdfSharp.Pdf.AcroForms` calls (issue #9).
- Fixes from a third fuzzing campaign (46,516 inputs, 4 crashing): outline
  destinations with missing or non-numeric parameters, an unknown type, a page
  number or a non-page object no longer fail the document, and no longer turn
  the named object into a page; `/Fields` and `/Kids` entries that cannot be
  fields are dropped, and a field listed as its own kid no longer recurses
  without end; a number beyond the single-precision range is clamped to it
  (ISO 32000-1 Annex C) instead of failing the reader.
- Add a shared FreePDF package icon for NuGet and Visual Studio, with an SVG
  source, reproducible PNG generation and package-content checks. The README
  shows it, and `src/packages/social-preview.png` is the matching GitHub social
  preview card.
- Publish the GDI and WPF builds as packages for Windows desktop and .NET
  Framework apps: `io7.FreePDF.Gdi` and `io7.FreePDF.Layout.Gdi` replace
  `PDFsharp-GDI` and `PDFsharp-MigraDoc-GDI`; `io7.FreePDF.Wpf` and
  `io7.FreePDF.Layout.Wpf` replace `PDFsharp-WPF` and `PDFsharp-MigraDoc-WPF`.
  They target Windows .NET 8, 9, 10 and .NET Framework 4.6.2, keep upstream's
  assembly names, and are installed and run on Windows in CI.
- Import PNG images with 16 bits per sample (grayscale, gray with alpha, RGB,
  RGBA). The importer refused them with "Unsupported bit depth 16", although
  its decoder reads them; they are now embedded with 8 bits per component,
  which every PDF/A part allows. Photo editors and imaging tools write 16-bit
  PNGs when a project uses 16 bits per channel.
- Add `FacturXManager` for Factur-X / ZUGFeRD hybrid invoices.
  `SetInvoice(xml, profile)` embeds the Cross Industry Invoice as `factur-x.xml`
  (`xrechnung.xml` for XRechnung) with MIME type `text/xml` and the profile's
  `/AFRelationship` (Data for MINIMUM and BASIC WL, Alternative otherwise, or the
  caller's choice), lists it in the catalog `/AF`, makes the document PDF/A-3B
  if it has no PDF/A format yet, and adds the Factur-X extension schema and
  properties to the XMP metadata. It accepts only well-formed Cross Industry
  Invoice XML. The package release check validates a Factur-X file with veraPDF.
- Write tagged PDF for PDF/UA-1 from the layout engine:
  `PdfDocumentRenderer.TaggedPdf`. Paragraphs become P and outline levels 1 to 6
  H1 to H6; consecutive list paragraphs become one L of LI items with Lbl and
  LBody, and list levels 2 and 3 nest inside the item before them; tables become Table, TR, TH (with `/Scope`) and TD, each page's part
  repeating its header cells; images and charts become a Figure with alternative
  text; hyperlinks become Link elements that own their annotations; headers,
  footers, borders, shading and text-frame outlines are artifacts. The title
  comes from `Document.Info.Title` (required) and the language from
  `Document.Culture`. New `Shape.AlternativeText` and `Shape.Decorative`; an
  image or chart with neither stops tagged rendering. Tagged and untagged output
  look the same. The package release check validates a tagged multi-page
  invoice with veraPDF's PDF/UA-1 profile.
- `Document.Clone()` returns a document that is not bound to a renderer. The
  copy kept the original's binding, so rendering it failed with "The document
  is already bound to another renderer", whose advice is to clone.
- Tagged documents built with `UAManager` now get XMP metadata that declares
  PDF/UA-1 (`pdfuaid:part`), with the PDF/A extension schema for it when the
  document is also PDF/A, and saving a figure tagged through `StructureBuilder`
  no longer fails ("A direct object can only be added once to a container").
  New `StructureBuilder.SetScope`, `BeginLinkElement` and `AddLinkAnnotation`.
  A tagged Factur-X invoice declares its extension schemas in one list and
  passes PDF/UA-1 and PDF/A-3b.
- `EmbeddedFilesManager.GetEmbeddedFileInfo` now reports the file's
  `AFRelationship` (it always reported none) and its MIME type without the
  leading slash of the PDF name, as they were set.
- Fix the BMP importer's file-size check, which was inverted since the v7 import
  refactoring: it rejected bitmaps followed by extra bytes and accepted truncated
  ones. GDI+ and WPF images (`XImage.FromGdiPlusImage`, `XImage.FromBitmapSource`)
  in true-colour formats reach it with trailing buffer bytes, so drawing an
  ordinary `System.Drawing.Bitmap` threw "ReadTrueColorMemoryBitmap: unsupported
  format". Found by the new GDI package consumer; PDFsharp 6.2.4 parsed these
  bitmaps inline and was not affected.
- Restore PDFsharp 6.2.4 members that real applications still use, found by
  compiling 26 public 6.2.4 projects against FreePDF (`docs/research/consumer-survey`):
  `DictionaryElements.GetDateTime(string, DateTime)` returning UTC as 6.2.4 did,
  the parameterless `CString`, `CInteger` and `CReal` constructors, and
  `PdfDocument.SetPdfA()` (declares PDF/A-1b). The migration guide lists the
  remaining breaks and their fixes. The survey tool is `eng/research/Survey-Consumers.ps1`.
- Fix `PdfAFormats.PdfA_2u`, which described PDF/A-2b.
- Read encrypted files whose trailer holds the encryption dictionary directly
  rather than by reference, as MuPDF writes them. The reader ignored such a
  dictionary: it opened password-protected files without asking for the password,
  could not decrypt their content, and saving wrote the still-encrypted streams
  into an unencrypted file. These files now require the password and decrypt
  like any other. Found by the corpus round trip (`eng/research/CorpusCheck`).
- Open documents with annotations of a subtype FreePDF does not model (private,
  misspelled or missing subtypes). One such annotation made the whole document
  fail with "Invalid annotation subtype"; it is now read as `PdfUnknownAnnotation`
  and saved unchanged. Found by the corpus round trip, including files that pass
  PDF/UA-1.
- Open files whose header names a later PDF 2.x minor version (`%PDF-2.1` to
  `%PDF-2.9`, which PDF/A-4 allows); they are read and saved as PDF 2.0. A file
  without a readable version header now fails with `PdfReaderException` instead
  of `InvalidOperationException`.
- Open documents containing streams whose data is in an external file (`/F` in
  the stream dictionary). The reader threw `NotImplementedException: File streams
  are not yet implemented` for the whole document (upstream issue 389); such
  streams are now parsed by `/Length` and saved with their `/F` entry. FreePDF
  deliberately never loads the external file, so a PDF cannot make it read local
  files.
- Treat a trailer `/Info` entry that references a missing object as absent
  (ISO 32000 treats such references as null); opening threw
  `ArgumentNullException`. References to missing or misplaced objects now fail
  with `PdfReaderException` instead of `AggregateException` (upstream issue 73).
- Save pages whose `/Annots` array holds annotations directly rather than by
  reference. Opening converted them to typed annotations but left the retired
  dictionaries in the array, so saving failed with "This dictionary cannot be
  used anymore". `/Annots` entries that are not dictionaries (null, numbers,
  strings, missing objects) are dropped when the document opens; they stopped it
  from opening, and iterating `PdfPage.Annotations` threw on them.
- The verbose writer layout's statistics comment no longer has fields for the
  user and owner passwords; they read "(never written)". The security manager
  behind them is still a stub that printed "(TODO)", so no password has been
  written so far, but the comment is plain text in an encrypted file.
- `OpenTypeFontFace.GetOrCreateFrom` returns the cached face of a font source
  that already has one instead of failing a Debug assertion.
- Write the outline root when it has no items. The writer skipped its body but
  kept it in the cross-reference table, so the saved file could not be reopened
  ("Unexpected token 'xref'"). This hit every new document that read
  `document.Outlines` without adding an item, and every opened file with an empty
  `/Outlines` dictionary. Found by mutation fuzzing (`CorpusCheck fuzz`).
- Treat a dictionary entry whose value is `null`, or a reference to a missing
  object, as absent when a typed value is read (ISO 32000-1 7.3.9). A catalog
  `/Metadata` or `/Outlines` like that made opening or saving fail with a type
  mismatch; it now reads as absent and is created on demand.
- Refuse a document whose catalog has no page tree (`/Pages` absent, `null` or
  dangling) with `PdfReaderException`. Such a file opened as an empty document,
  and saving it dropped every page in the file.
- Read an outline only as far as its `/First` and `/Next` links lead to outline
  items. A link back to an earlier item made opening loop until memory ran out;
  a link to the catalog or a page converted that object into an outline item, so
  saving failed; a link to a number threw `InvalidCastException`. The outline now
  ends at such a link.
- Resolve references to missing objects as `null` at any depth (ISO 32000-1
  7.3.10). Inside a direct array or dictionary, such as a page's `/Annots`, they
  stayed references without a value, and reading them threw
  `InvalidOperationException`.
- Read a page without a `/MediaBox`, own or inherited, as US Letter
  (612 x 792 points) as Poppler and MuPDF do, and write that box on save.
  Opening threw `InvalidOperationException`.
- Recover the length of a stream whose indirect `/Length` is not an integer by
  searching for `endstream`, as for a missing `/Length`; the parser threw
  `InvalidCastException`.
- Damaged files now fail with `PdfReaderException` instead of
  other exception types from inside the reader: cross-reference streams whose
  `/Size`, `/W`, `/Index` or `/Prev` is missing, mistyped or larger than the
  data; object streams with an invalid `/N`, `/First` or header, or whose
  container is missing or not a stream; cross-reference and object streams that
  cannot be decoded (unknown filter, corrupt Flate data); a trailer `/Root` that
  refers to a missing object; a `stream` keyword inside an array or dictionary;
  a number with two decimal points; an AES-256 `/Perms` that is not 16 bytes.
  Before, these threw `InvalidOperationException`, `IndexOutOfRangeException`,
  `OverflowException`, `NullReferenceException`, `ArgumentNullException`,
  `NotImplementedException`, `InvalidDataException`, `CryptographicException` or
  `ContentReaderException`. A damaged object stream `/N` also no longer
  allocates its header before anything is read. All found by mutation fuzzing.
- Read cross-reference stream entries without a type field (`/W` starting with
  0) as in-use objects, as ISO 32000-1 Table 17 specifies; they were read as
  free.
- A typed read of a direct dictionary or array (for example
  `GetDictionary<PdfEmbeddedFiles>`) now converts it even after an untyped read
  of the same entry. The untyped read marked it as tried, and the typed read
  failed with a type mismatch; Debug builds hit this when saving a read document
  with embedded files.
- A typed read no longer converts an object that already has an unrelated type.
  In a damaged file where `/Outlines` or `/Info` names the catalog or a
  cross-reference stream, the conversion destroyed that object and opening or
  saving failed with "This dictionary cannot be used anymore"; the entry now
  reads as absent. An untyped read of an indirect number, name or string where
  metadata expects a dictionary returns it instead of throwing, so a `/Pages`
  that names one is reported as a missing page tree. The parser's error message
  no longer overflows for offsets far outside the file.
- Further damaged input from a second fuzzing campaign: a `/Pages` that names
  the catalog itself, or a `/Root` that is not a dictionary, is a reader error;
  an encryption dictionary whose values do not fit together (crypt filters on a
  revision without them, a wrong `/Length`) fails with `PdfReaderException`
  instead of `InvalidOperationException`; a `/MediaBox` that is not four numbers
  reads as US Letter like a missing one; an outline `/Count` or `/C` of the
  wrong type is ignored. `TryGetRectangle` no longer throws for an array that
  holds a non-number.

## 7.0.1

- Give FreePDF its own assembly identity. Sign with io7's strong-name key
  (public key token `77af05c50284aa70`) instead of empira's (`f94615aa0424f9eb`),
  and set the assembly company to io7 and the product to FreePDF. The 7.0.0
  assemblies carried empira's key, company and product names, so loaders and
  licence scanners could not tell them apart from empira's PDFsharp builds.
- Keep empira Software GmbH's copyright in every assembly and package, next to
  io7's. NuGet lists io7 as the only author.
- Rename the layout package from `io7.FreePDF.MigraDoc` to `io7.FreePDF.Layout`
  before the first NuGet release, so no package ID carries empira's product
  names. Assembly names and `MigraDoc` namespaces are unchanged.
- Add regression tests that pin the assembly identity. Run CI on pushes to
  `main`, the default branch that publication requires.
- No library behaviour or public API changes. Rebuild code compiled against
  7.0.0, and do not mix 7.0.0 and 7.0.1 assemblies. See the
  [release notes](docs/releases/7.0.1.md).

## 7.0.0

- Promote the reviewed RC2 implementation to the first stable FreePDF version.
  Preserve the MIT source ancestry, licence notices, PdfSharp/MigraDoc namespaces
  and the verified v7 Preview 1 public API.
- Ship portable PDF creation/editing and MigraDoc PDF/RTF layout as
  `io7.FreePDF` and `io7.FreePDF.MigraDoc` for .NET 8/9/10 and Standard 2.0.
- Add explicit, manual publication of verified CI packages through NuGet Trusted
  Publishing. No library implementation changes are included in the promotion.
- Document migration requirements, supported profiles and feature limits in the
  [release notes](docs/releases/7.0.0.md) and [support policy](docs/support-policy.md).

## 7.0.0-rc.2 — candidate, unpublished

- Correct partial-stream reads during CMS signing and reject unexpected EOF.
- Bound short-file trailer scans and report missing cross-reference markers as
  PDF-reader errors. Add a deterministic mutation corpus with process budgets.
- Preserve the MIT v7 package's legacy dictionary-extension generic contract.
- Replace the inherited MD5 helper with original RFC 1321 code after finding
  incomplete historical third-party licence evidence. Preserve PDF compatibility
  through published-vector, chunking, padding, reuse and independent PDF tests.
- Add four-target API gates, dependency inventories/advisory gates, migration
  guidance, support policy and a plain `7.0.0` package-install rehearsal.
- Exercise multipage invoices/RTF, merge/stamp, attachments, AES-128/256,
  PDF/A-1b/2b/3b and SHA-256/384/512 signatures through installed packages.
  Check outputs with qpdf, Poppler and veraPDF, including tampered signatures.
- Correct the original semantic fixture's missing form default appearance.
  Update the lab's private runtime to .NET 10.0.14.

## 7.0.0-rc.1 — candidate, unpublished

- Preserve each active compressed object selected by incremental cross-reference
  entries; reject object indices that disagree with their stream header (#302 category).
- Reuse typed shared content streams when another page accesses them, avoiding
  invalidation of a caller's existing content object (#369 category).
- Add semantic save/reopen checks for text, links, annotations, named destinations,
  form values and embedded-file bytes, plus three comparison-lab cases.
- Prepare `io7.FreePDF` and `io7.FreePDF.MigraDoc` packages with .NET 8/9/10 and
  .NET Standard 2.0 assets, retained notices, correct internal dependencies and
  package-only consumer checks. Preserve API namespaces and assembly names.
- Block inherited upstream package identities and use the publisher's verified
  direct GitHub asset for the external Windows renderer.

## 7.0.0-dev.1 — unreleased

- Adopt the complete verified MIT Preview 1 source and its v7 API changes,
  including attachment/XMP/PDF-A management and the refactored object model.
  Some typed form APIs are placeholders; imported features are not all certified.
- Retain FreePDF's zlib checksum, transparency/gradient and inline-image fixes.
- Independently tolerate string/integer dictionary keys, fall back from invalid
  UTF-8 PDF names to the current culture's ANSI code page, propagate PNG memory
  exhaustion, avoid signature-field name collisions, and publish the MigraDoc
  font cache atomically with mutation-aware invalidation.
- Fix disconnected Core path figures (#396), fully merged table rendering
  (#358), and truncated hexadecimal-string termination using original reproducers.
- Pin the cryptography dependency to patched 10.0.13 for the two October 2026
  PKCS denial-of-service advisories. Review remaining dependency graph before release.
- Generate FreePDF build versions without GitVersion, PowerShell, or automatic
  external-asset downloads during ordinary builds.
- Add a comparison lab with separate MIT reference/FreePDF generators, fixed
  fonts, pixel differences, structural checks and a read-only browser viewer.
  Include Windows installation scripts and Linux/Windows/Framework comparison CI.

The reviewed Preview 2 changelog enumerates licensing changes, not a separate
functional feature list. No AGPL implementation was imported or consulted.
See [provenance](PROVENANCE.md), [feature tracking](docs/upstream-feature-tracking.md)
and [validation](docs/7.0.0-validation.md). No NuGet release has been published.

## 6.2.5 — unreleased

- Set the development version to 6.2.5 and resolve GitVersion configuration from
  the current checkout so shared-object-store worktrees use the same policy.

- Correct initial transparent strokes and fills without changing public APIs.
  Adapted from the MIT PDFsharp 7.0.0 Preview 1 implementation, with solid-color
  restoration after a gradient retained for compatibility.
- Reject truncated inline-image headers instead of looping indefinitely in the
  content reader. Adapted from the MIT preview's EOF handling.
- Write complete zlib streams, including Adler-32 checksums and empty-input
  framing. This is an independent correction against RFC 1950.
- Add synthetic regressions, expanded platform CI, and an
  [upstream feature matrix](docs/upstream-feature-tracking.md).

This is development work, not a published package or a stable-release claim.

## Initial fork

Based on the MIT PDFsharp/MigraDoc 6.2.4 source, with original history and
notices preserved. See [PROVENANCE.md](PROVENANCE.md).
