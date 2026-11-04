# Migrating to FreePDF 7

FreePDF 7 follows the reviewed MIT PDFsharp/MigraDoc v7 Preview 1 API. It is a
major-version upgrade from 6.2.4. Keeping `PdfSharp`/`MigraDoc` namespaces and
assembly names does **not** make existing v6 binaries compatible.

## Packages and rebuilding

Replace `PDFsharp` with `io7.FreePDF`, and `PDFsharp-MigraDoc` with
`io7.FreePDF.Layout`. The latter depends on the former. Windows applications on
the GDI or WPF builds replace `PDFsharp-GDI`/`PDFsharp-MigraDoc-GDI` with
`io7.FreePDF.Gdi`/`io7.FreePDF.Layout.Gdi`, and `PDFsharp-WPF`/`PDFsharp-MigraDoc-WPF`
with `io7.FreePDF.Wpf`/`io7.FreePDF.Layout.Wpf` (since 7.1). FreePDF 7.0.0 named
the layout package `io7.FreePDF.MigraDoc`; it was renamed before the first
NuGet release. Remove upstream
packages from the entire dependency graph, including transitive references,
and rebuild consumers. Do not load both implementations in one application:
they intentionally use the same assembly names.

Since 7.0.1, FreePDF assemblies are strong-named with io7's key (public key
token `77af05c50284aa70`). 7.0.0 used empira's key (`f94615aa0424f9eb`), so
rebuild anything compiled against 7.0.0 and do not mix the two versions.

The packages contain the core, barcodes, charts, cryptography, document model,
PDF renderer and RTF renderer for their variant. Upstream's Quality, Snippets and
WPFonts helper assemblies are not shipped. The GDI and WPF packages keep upstream's
assembly names (`PdfSharp-gdi`, `MigraDoc.Rendering-wpf` and so on), so binding
redirects and reflection that name them keep working. The portable packages find
installed fonts without a font resolver; see [Fonts](#fonts). Test fixtures' fonts
are not product dependencies.

## Common source changes inherited from v7

| v6 assumption | v7 migration |
| --- | --- |
| Metadata dates and `DocumentRenderer.PrintDate` are `DateTime` | Use nullable `DateTimeOffset`; choose an explicit offset when converting an old value. |
| `PdfDocument` is a `PdfObject` and `ICloneable` | Treat it as the document owner; rework casts, cloning and helpers that require the old base class. |
| `PdfDocument.Internals` returns `PdfInternals` | Its type is `PdfDocumentInternals`; recompile and check explicit type declarations. |
| Legacy `PdfSharp.Pdf.AcroForms` types are the form model | v7 exposes `PdfForm` through `document.Catalog.GetAcroForm()` or `GetOrCreateAcroForm()`. Since 7.1 its fields are found by name and filled through typed members, and `document.Flatten()` draws them into the pages; see [Filling forms](#filling-forms). |
| A one-argument dictionary getter has the old binary signature | Several getters gained optional arguments. Source calls can still compile while precompiled binaries require rebuilding. |
| Font, glyph, image and content-parser implementation types are unchanged | Review explicit references against the API report; Imaging/OpenType now have separate assemblies. |
| Standard-target helper types are stable | `PdfSharp.SystemStringExtensions` is removed in v7's Standard build; review direct calls to inherited helper types as well as the main PDF APIs. |

For example, `document.Info.CreationDate = DateTimeOffset.UtcNow` expresses a
known instant. Do not silently interpret a legacy `DateTimeKind.Unspecified`
value as UTC; preserve the application's intended timezone.

## What real 6.2.4 applications hit

FreePDF compiled 26 public projects that use PDFsharp or PDFsharp-MigraDoc 6.2.4
against FreePDF 7 (method and results: [consumer survey](research/consumer-survey/README.md)).
23 compiled with no new error. The breaks in the other three, and what to do:

| 6.2.4 code | With FreePDF |
| --- | --- |
| `elements.GetDateTime(key, DateTime default)` returning `DateTime` | Compiles again since 7.1: the overload returns the stored date as UTC, as 6.2.4 did. `GetDateTime(key)` returns `DateTimeOffset?`. |
| `new CString()`, `new CInteger()`, `new CReal()`, or a subclass filled with an object initializer | Compiles again since 7.1. |
| `document.SetPdfA()` | Compiles again since 7.1 and declares PDF/A-1b, which FreePDF's release checks validate. 6.2.4 declared PDF/A-1a without creating the tagged structure that level requires. Call it before adding pages; FreePDF refuses PDF/A settings after content exists. |
| `byte[] bytes = sequence.ToContent()` | `ToContent()` returns the content stream as a string with one character per byte. Use `PdfSharp.Pdf.Internal.PdfEncoders.RawEncoding.GetBytes(sequence.ToContent())`, as FreePDF does internally. |
| `DateTime created = document.Info.CreationDate` | Read `document.Info.CreationDate?.UtcDateTime` (or `LocalDateTime`). Assigning a `DateTime` still compiles. |
| `using PdfSharp.Snippets;`, `PdfSharp.Quality`, `PdfSharp.WPFonts` | These upstream helper assemblies are not shipped. Remove unused usings. `PdfSharp.WPFonts` embeds Microsoft's Segoe WP fonts, which FreePDF has no licence to redistribute: give your `IFontResolver` font files you are licensed to embed. |

Legacy forms (`PdfSharp.Pdf.AcroForms`, `document.AcroForm.Fields["name"]`) did not
appear in the survey, but some applications fill forms this way; see below.

## Fonts

PDFsharp 6.2.4's portable build needs an `IFontResolver` before it can draw text,
on Windows as on Linux, macOS and in containers. FreePDF's portable packages use the
fonts installed on the machine when no `GlobalFontSettings.FontResolver` is set:

- FreePDF asks the Windows font table (`UseWindowsFontsUnderWindows`,
  `UseWindowsFontsUnderWsl2`) and your `FallbackFontResolver` first, then the
  folders added with `GlobalFontSettings.AddFontDirectory`, then the system's
  font folders (Windows `Fonts` folders; `~/.local/share/fonts`, `~/.fonts`,
  `/usr/local/share/fonts`, `/usr/share/fonts`; macOS `Library/Fonts` folders).
- A common family that is not installed is replaced, and a warning is logged:
  Arial and Helvetica by Liberation Sans or Arimo, Times New Roman by Liberation
  Serif or Tinos, Courier New by Liberation Mono or Cousine, Calibri by Carlito,
  Cambria by Caladea. These have the same widths, so line breaks match. Without
  them FreePDF uses DejaVu or Noto fonts of the same kind, whose widths differ.
  In a Debian or Ubuntu container, `apt-get install fonts-liberation` provides
  the first group.
- Only TrueType fonts that FreePDF can embed are used: single-font `.ttf` and
  `.otf` files with TrueType outlines whose licence bits allow embedding a subset.
  Font collections (`.ttc`) and CFF `.otf` fonts are skipped.

An application with its own `IFontResolver` behaves exactly as before. Set
`GlobalFontSettings.UseSystemFonts = false` before the first font is used to
require a font resolver as 6.2.4 did. Output depends on the fonts installed, so a
font resolver remains the way to identical output on every machine.

## Filling forms

FreePDF carries the v7 form model (`PdfSharp.Pdf.Forms`). Since 7.1 a 6.2.4
form-filling program moves over with renames:

| 6.2.4 (`PdfSharp.Pdf.AcroForms`) | FreePDF 7.1 (`PdfSharp.Pdf.Forms`) |
| --- | --- |
| `document.AcroForm` | `document.Catalog.GetAcroForm()` (null when the file has no form) |
| `form.Fields["name"]`, `form.Fields["address.city"]` | The same; null when there is no such field. Also `TryGetField` and `Count`. |
| `PdfTextField`: `Text = "Ada"` or `Value = new PdfString("Ada")` | `PdfFormTextField.Text = "Ada"` |
| `PdfCheckBoxField.Checked` | `PdfFormCheckBoxField.Checked` |
| `PdfRadioButtonField.SelectedIndex` | `PdfFormRadioButtonField.SelectedIndex`, `SelectedValue`, `Options` |
| `PdfComboBoxField` / `PdfListBoxField`: `SelectedIndex` | `PdfFormComboBoxField` / `PdfFormListBoxField`: `SelectedIndex`, `SelectedValue` (export value), `Options`, `ExportValues` |
| `field.ReadOnly` | `field.IsReadOnly` |
| `form.Elements.SetBoolean("/NeedAppearances", true)` | Not needed: FreePDF draws the filled appearance. `form.NeedAppearances` remains for special cases. |
| `document.Flatten()` (made the top-level fields read-only) | `document.Flatten()` draws every visible field into its page and removes the form, so the values can no longer be edited or read as form data. Read values you still need before flattening. 7.0 threw `InvalidOperationException` here. |

FreePDF draws text and choice fields with the font, size, colour and alignment
of the field's default appearance (`/DA`, `/Q`) and the widget's border and
background. Check boxes and radio buttons switch to the on and off appearances
the form already contains, under whatever on-state name it uses. Limits:

- Text is written in the WinAnsi encoding of the form's font. Text outside it
  (for example Japanese) is left to the viewer through NeedAppearances; a PDF/A
  document refuses such a value, because PDF/A does not allow NeedAppearances.
- Centred and right-aligned text, line wrapping and automatic font sizes use the
  font's `/Widths`. Since 7.1.1, the twelve Latin standard fonts (and Arial,
  Times New Roman and Courier New) use Adobe's metrics when they list no widths;
  other fonts without widths use an estimate.
- Saving a document opened in `Modify` mode rewrites the file, which invalidates
  its signatures. Open a signed form with `PdfDocumentOpenMode.Append` to fill it
  in; see [Signed documents](#signed-documents).
- `Flatten()` draws each visible widget's appearance where the widget sits and
  removes the form and its widgets; other annotations stay. Text and choice fields
  without an up-to-date appearance (for example a `/V` written directly with
  NeedAppearances set) get one first. If a value cannot be drawn, such as
  Japanese text in a WinAnsi font, `Flatten()` throws and changes nothing.

## Merging and splitting

6.2.4's way of merging, `output.AddPage(input.Pages[i])` on documents opened with
`PdfDocumentOpenMode.Import`, works as it did. It copies page content and
annotations, but not the form, so the fields arrive as widgets that are no longer
a form; not the bookmarks; and a link to another page leads to a hidden copy of
that page, which also comes along when you split. Since 7.3, `AppendPages` and
`ExtractPages` keep all three:

| 6.2.4 | FreePDF 7.3 |
| --- | --- |
| `foreach (var page in input.Pages) output.AddPage(page);` | `output.AppendPages(input);` or `output.AppendPages(input, startIndex, pageCount);` |
| A new document plus `AddPage(input.Pages[i])` | `using var part = input.ExtractPages(i, 1);` |

- Form fields with a widget on a copied page join the document's form and can be
  filled or flattened, with the font, size and colour their own form gave them
  (a form font whose name the document already uses for another font is added
  under a new name). A field whose name is already used is renamed `name_2`,
  `name_3` and so on; `AppendPages` returns the new names in `RenamedFields`.
  To merge filled forms into a file that cannot be edited, call `Flatten()` on
  the result.
- Links, the GoTo actions of buttons, and bookmarks lead to the copied pages.
  Named destinations are written as the explicit destinations they name. A link
  or action that leads to a page that was not copied is removed; so is such a
  bookmark, unless it holds bookmarks that were kept. Links and bookmarks that
  lead elsewhere, such as a web address, are kept.
- Append each source in one call. Pages appended in separate calls are separate
  copies: a field with widgets in both becomes two fields, and links and
  bookmarks between them are removed.
- Signatures are copied as they are, but a signature cannot be valid in a new
  file; viewers report it as invalid. Sign the merged file.
- If copying fails on a damaged source, the document's pages, form and bookmarks
  are left as they were.
- `new PdfPageImportOptions { BookmarkTitle = "invoice.pdf" }` puts the copied
  bookmarks under one bookmark that opens the first copied page;
  `KeepBookmarks = false` leaves them out.
- Fonts and images are shared between the copied pages and their annotations,
  and between repeated copies of the same source.
- Not carried: tags (the structure tree), page labels, optional content (layer)
  settings, XFA, named destinations as names, and fields without a widget on a
  copied page. JavaScript that refers to a renamed field by name is not updated.
- A PDF/UA document refuses copied pages, as it refuses `AddPage(page)`.

## Signed documents

PDFsharp 6.2.4 always writes a new file, so saving a signed document after any
change breaks its signatures, and a signed document cannot be signed a second
time. Since 7.4, FreePDF has `PdfDocumentOpenMode.Append` for this:

```csharp
using var document = PdfReader.Open("signed.pdf", PdfDocumentOpenMode.Append);
((PdfFormTextField)document.Catalog.GetAcroForm()!.Fields["name"]!).Text = "Ada Lovelace";
document.Save("signed-filled.pdf");

using var approved = PdfReader.Open("signed-filled.pdf", PdfDocumentOpenMode.Append);
DigitalSignatureHandler.ForDocument(approved, signer, options);
await approved.SaveAsync("approved.pdf");
```

To sign a form's empty signature field instead of adding a new one, name it
(since 7.6):

```csharp
DigitalSignatureHandler.ForDocument(approved, signer,
    new DigitalSignatureOptions { FieldName = "Approval", Reason = "Approved" });
```

- Saving writes the original file byte for byte, then an incremental update with
  only the new and changed objects, their cross-reference section (a table or a
  stream, as the file uses) and a trailer that points back to the previous one.
  Object numbers and the page tree stay as they are in the file.
- The signatures in the file stay valid. A new signature covers the whole file,
  earlier signatures included.
- A signature in an existing field (`DigitalSignatureOptions.FieldName`) is shown
  in that field's widget at its size, so leave `Rectangle` and `PageIndex` unset;
  the field keeps its name and flags, and the form gets no new field. A field
  lock (`/Lock`) is recorded in the signature (FieldMDP, as Acrobat and pyHanko
  record it), so validators lock its fields from then on. Required seed values
  (`/SV`) are checked: a required reason must be given as `Reason`, or left empty
  where the field allows none (no reasons or a single "." listed), and
  requirements FreePDF cannot meet or check (a signing certificate, a digest
  method, a timestamp, a certification, revocation information, locking the
  document, a named appearance) make `ForDocument` throw `NotSupportedException`. A field that is
  already signed cannot be signed again, and one that an earlier signature locks
  is refused when saving. It works in `Modify` mode too.
- A save that would change more than the document's signatures permit throws
  `InvalidOperationException` before anything is written, and saving to a path
  leaves the file as it was. The message names the permission and the change.
  The document is then marked saved; open the file again to try a different change.
  - Signed signature fields cannot change, in any signed document: the field, the
    fields above it, its widget and everything they refer to (the signature, its
    lock, the appearance and its resources). They cannot be removed, renamed by
    moving them in the field tree, or moved to another page.
  - Fields that a signature locks (FieldMDP, or the `/Lock` of its signature
    field) are protected the same way. A `/Lock` with `/P` also limits later
    changes like a certification.
  - A certification signature (DocMDP, ISO 32000-1 12.8.2.2) with "no changes"
    (P=1) permits no update at all; with "form filling and signing" (P=2) it
    permits filling in fields, adding signature fields and signing, and changing
    the document information; with P=3 also adding, changing and removing
    comments and other annotations. Page content, pages, bookmarks and other
    parts of the document cannot change under any level.
  - Without a certification, everything else can change, as before.
- Whether a validator accepts what you changed after a signature still depends on
  the validator. Filling in fields and adding signatures are permitted changes for
  an ordinary (approval) signature. Acrobat permits added comments and new visible
  signature fields where the permissions allow them; pyHanko's default policy
  reports any added comment, and after a certification it accepts only invisible
  new signature fields (`DigitalSignatureOptions.Rectangle` left empty) and
  signing existing ones (`DigitalSignatureOptions.FieldName`). pyHanko accepts
  signing an existing field when the field is its own widget, as pyHanko and most
  tools make them; when its widget is a separate object, or it has several
  widgets or shares its appearance with another widget, pyHanko reports the
  earlier signatures as changed (its own signer refuses fields with several
  widgets and does not update a separate widget's appearance). After an
  ordinary signature, pyHanko also reports
  changes to pages, which no permission forbids and FreePDF saves; and after a
  signature that locks all fields, pyHanko counts signature fields added later as
  locked too, while FreePDF applies the lock to the fields the document had.
- A document with no changes is saved as it was. Otherwise the document
  information gets a new modification date (unless you set one) and FreePDF as
  its producer. An update never adds a `/PageMode` to the catalog (a full save
  makes a document with bookmarks open with them shown). Reading
  `page.Annotations`, `document.Outlines`, `document.ViewerPreferences` or a form
  field's `Kids` creates them when the file has none, and saving writes that as a
  change, which a signed or certified document may refuse; read a field's kids
  with `GetKids()` instead.
- An encrypted document keeps its encryption: the update's objects are encrypted
  with the file's key (RC4 and AES-128/256 of the standard security handler), and
  the original's passwords keep working. Open it with the owner password, or with
  the user password and `PdfReaderOptions.AllowModifyWithoutOwnerPassword`, as in
  `Modify` mode; with that option, as in `Modify` mode, the file's permission flags
  (printing, changing, commenting) are not enforced. The encryption, including its
  permissions, cannot be changed or removed in `Append` mode, and an unencrypted
  document cannot be given a password. A changed save of a file that carries an
  integrity code over its encryption (PDF MAC, ISO/TS 32004; pyHanko adds one to
  AES-256 by default) is refused, because an update cannot extend the code.
- Files whose cross-reference table names the wrong objects cannot be opened in
  `Append` mode; open those with `Modify` to save a new file.
- Pages can be added and removed when all pages hang directly under the page
  tree's root, which is the common case; when the page tree is nested, inserting,
  removing or moving pages throws `NotSupportedException`.

## Repeatable API evidence

`eng/Test-PublicApi.ps1` compares all eleven packaged assemblies for all four
target frameworks with hash-pinned MIT packages. Microsoft ApiCompat 10.0.100
also checks public parameter names. Any incompatible change from the published
MIT v7 Preview 1 contract fails the gate; there are no blanket suppressions.
Since 7.0.1, each comparison must also report exactly one `CP0003` finding: the
public key token change from empira's `f94615aa0424f9eb` to io7's
`77af05c50284aa70`. Both tokens are pinned in the script, so a missing or
different identity change fails the gate as well.

The same audit produces v6.2.4 migration diagnostics as information, not a v6
compatibility promise. The initial net8.0 comparison reports 163 diagnostics;
one property can produce several diagnostics. See the full report in
`research/api-6.2.4-to-7.0.0-net8.0.txt` and CI artifacts for each target.

The legacy dictionary extension explicitly preserves the published method's
nullable metadata across SDKs, including nullable dictionary values. Its
compiler-generated extension marker is preserved as well.

## Behaviour to verify in your application

Saving a document opened in `Modify` mode rewrites the file and does not
preserve existing digital signatures; open it in `Append` mode to keep them (see
[Signed documents](#signed-documents)). Use a fresh document per concurrent operation; sharing a mutable document
between threads is unsupported. Retest pagination and font selection when
upgrading: reference output containing a known defect is not the correct output.

Documents name FreePDF, not PDFsharp, as their Producer and Creator (since
7.3.0; see the [changelog](../CHANGELOG.md)). Code or tests that
recognise their own output by a Producer starting with "PDFsharp", or that compare
it with `PdfSharpProductVersionInformation.Title`, should expect "FreePDF".

The original installed-package workflows cover a multipage invoice, RTF,
merge/stamp, attachments, AES-128/256, PDF/A-1b/2b/3b and SHA-256/384/512
signatures. They contain synthetic data and do not certify a customer's document.
