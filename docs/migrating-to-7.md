# Migrating to FreePDF 7

FreePDF 7 follows the reviewed MIT PDFsharp/MigraDoc v7 Preview 1 API. It is a
major-version upgrade from 6.2.4. Keeping `PdfSharp`/`MigraDoc` namespaces and
assembly names does **not** make existing v6 binaries compatible.

## Packages and rebuilding

Replace `PDFsharp` with `io7.FreePDF`, and `PDFsharp-MigraDoc` with
`io7.FreePDF.MigraDoc`. The latter depends on the former. Remove upstream
packages from the entire dependency graph, including transitive references,
and rebuild consumers. Do not load both implementations in one application:
they intentionally use the same assembly names.

The packages contain the portable core, barcodes, charts, cryptography, document
model, PDF renderer and RTF renderer. Upstream's Quality, Snippets and WPFonts
helper assemblies are not shipped. GDI/WPF source variants are build-checked
separately; these are not GDI/WPF NuGet packages. Configure an application-owned
font resolver; test fixtures' fonts are not product dependencies.

## Common source changes inherited from v7

| v6 assumption | v7 migration |
| --- | --- |
| Metadata dates and `DocumentRenderer.PrintDate` are `DateTime` | Use nullable `DateTimeOffset`; choose an explicit offset when converting an old value. |
| `PdfDocument` is a `PdfObject` and `ICloneable` | Treat it as the document owner; rework casts, cloning and helpers that require the old base class. |
| `PdfDocument.Internals` returns `PdfInternals` | Its type is `PdfDocumentInternals`; recompile and check explicit type declarations. |
| Legacy `PdfSharp.Pdf.AcroForms` types are the form model | v7 exposes `PdfForm` through `document.Catalog.GetAcroForm()` or `GetOrCreateAcroForm()`. Typed field classes are incomplete; general filling/flattening is outside the stable feature scope. |
| A one-argument dictionary getter has the old binary signature | Several getters gained optional arguments. Source calls can still compile while precompiled binaries require rebuilding. |
| Font, glyph, image and content-parser implementation types are unchanged | Review explicit references against the API report; Imaging/OpenType now have separate assemblies. |
| Standard-target helper types are stable | `PdfSharp.SystemStringExtensions` is removed in v7's Standard build; review direct calls to inherited helper types as well as the main PDF APIs. |

For example, `document.Info.CreationDate = DateTimeOffset.UtcNow` expresses a
known instant. Do not silently interpret a legacy `DateTimeKind.Unspecified`
value as UTC; preserve the application's intended timezone.

## Repeatable API evidence

`eng/Test-PublicApi.ps1` compares all eleven packaged assemblies for all four
target frameworks with hash-pinned MIT packages. Microsoft ApiCompat 10.0.100
also checks public parameter names. Any incompatible change from the published
MIT v7 Preview 1 contract fails the gate; there are no blanket suppressions.

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
