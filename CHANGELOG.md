# FreePDF changelog

## Unreleased

- Add a shared FreePDF package icon for NuGet and Visual Studio, with an SVG
  source, reproducible PNG generation and package-content checks. The README
  shows it, and `src/packages/social-preview.png` is the matching GitHub social
  preview card.
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
  used anymore". `/Annots` entries that are not dictionaries no longer stop the
  document from opening.
- The verbose writer layout's statistics comment no longer has fields for the
  user and owner passwords; they read "(never written)". The security manager
  behind them is still a stub that printed "(TODO)", so no password has been
  written so far, but the comment is plain text in an encrypted file.
- `OpenTypeFontFace.GetOrCreateFrom` returns the cached face of a font source
  that already has one instead of failing a Debug assertion.

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
