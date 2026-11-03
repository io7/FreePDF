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
redirects and reflection that name them keep working. Configure an application-owned
font resolver for the portable packages; test fixtures' fonts are not product dependencies.

## Common source changes inherited from v7

| v6 assumption | v7 migration |
| --- | --- |
| Metadata dates and `DocumentRenderer.PrintDate` are `DateTime` | Use nullable `DateTimeOffset`; choose an explicit offset when converting an old value. |
| `PdfDocument` is a `PdfObject` and `ICloneable` | Treat it as the document owner; rework casts, cloning and helpers that require the old base class. |
| `PdfDocument.Internals` returns `PdfInternals` | Its type is `PdfDocumentInternals`; recompile and check explicit type declarations. |
| Legacy `PdfSharp.Pdf.AcroForms` types are the form model | v7 exposes `PdfForm` through `document.Catalog.GetAcroForm()` or `GetOrCreateAcroForm()`. Since 7.1 its fields are found by name and filled through typed members; see [Filling forms](#filling-forms). Flattening is not supported. |
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
| `document.Flatten()` | Not supported. |

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
- Saving rewrites the file, so filling a signed form invalidates its signatures.

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

Saving rewrites the file. Importing incremental PDFs does not implement
incremental saving or preserve existing digital signatures. Sign the final
output. Use a fresh document per concurrent operation; sharing a mutable document
between threads is unsupported. Retest pagination and font selection when
upgrading: reference output containing a known defect is not the correct output.

The original installed-package workflows cover a multipage invoice, RTF,
merge/stamp, attachments, AES-128/256, PDF/A-1b/2b/3b and SHA-256/384/512
signatures. They contain synthetic data and do not certify a customer's document.
