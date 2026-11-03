# FreePDF changelog

## Unreleased

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
