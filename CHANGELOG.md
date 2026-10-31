# FreePDF changelog

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
