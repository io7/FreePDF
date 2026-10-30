# Upstream feature tracking

Reviewed 2026-10-30. This matrix guides FreePDF's work; it is not a claim of
feature parity or a promise to ship on upstream's schedule.

**Release policy:** 6.2.5 is a compatible maintenance release. FreePDF 7.0.0 is
reserved for an explicitly reviewed adoption of the MIT preview's broader
architecture and API changes. Neither version is released by this change.

## Sources and limits

- [General history](https://docs.pdfsharp.net/General/History.html)
- [PDFsharp history](https://docs.pdfsharp.net/PDFsharp/History.html)
- [MigraDoc history](https://docs.pdfsharp.net/MigraDoc/History.html)
- Source snapshots: `v6.2.4` at `9c3863a71b12067ff36e68a92c1a1f260114e2d1`
  and `v7.0.0-preview-1` at `8d818d5f2ad5087201c4a54be3ab489b4920a6d2`.

Only those MIT source snapshots were inspected. Later public changelogs supply
behavioural requirements, not implementation inputs. An entry first documented
later may still have an earlier MIT implementation; establish that separately
before importing code. The two source snapshots differ in 885 files, with
46,590 insertions and 14,098 deletions, so a wholesale merge is not a patch release.

Scope: all listed feature/fix/breaking-change headings from 6.2.4 through the
current 6.5.0 and 7.0 previews, plus key inherited capabilities relevant to
compatibility. Earlier release histories remain references rather than a new
certification of the entire library.

## Current 6.2.5 work

| Item | Origin | Status and validation |
| --- | --- | --- |
| Initial transparent strokes and fills | MIT Preview 1; upstream #281 | Backported selectively. Test RGB/CMYK, empty colors, transitions, save/restore, gradients, and PDF 1.3 compatibility. |
| Truncated inline-image header termination | MIT Preview 1 scanner EOF guard, found in source review | Adapted to throw the existing content-reader exception. Test malformed headers, truncated data, and valid binary/ASCII85 inputs. |
| Complete zlib output | Independent finding in the MIT baseline; unchanged in Preview 1 | Add the RFC 1950 checksum and an empty final block. Test known vectors, all compression modes, and platform zlib compatibility. |

## Inherited from 6.2.4

These rows record inherited implementation, not fresh acceptance of every path.

| Area | Inherited capability/fix | Additional release evidence needed |
| --- | --- | --- |
| Dependencies | Logging abstraction dependency | Review package graph and licences before packaging. |
| Encryption | `/Length` compatibility workaround | Encrypted-document viewer checks. |
| Content arrays | Element separators | Edit/save/reopen fixture. |
| JPEG | Stuffed-byte handling | Synthetic JFIF regression. |
| Filters | Indirect decoding parameters | Indirect-object fixture. |
| MigraDoc images | Missing-image diagnostics | Layout check with a synthetic missing path. |
| MigraDoc lifetime | Release the previous document reference | Bounded GC/lifetime regression. |
| Platforms | .NET 8/9/10, .NET Standard and Windows variants | The initial build checks covered core 8/10/Standard; this change expands runtime and Windows checks. .NET 9 is not in the CI matrix yet. |
| Established features | Text/layout, tables, images, merging, signatures, PDF/A support with upstream limitations | No new guarantee of PDF/A conformance, signature validity, or every renderer's layout. |

## MIT Preview 1 feature and compatibility candidates

Source availability permits review; it does not establish production readiness.

| Changelog area | FreePDF direction | Acceptance requirement |
| --- | --- | --- |
| PDF object model and dictionary nullability | 7.0 review | Public API comparison and migration tests. |
| Tolerant reading | Isolate bounded 6.2.x fixes where practical | Malformed fixtures must terminate; valid inputs must retain semantics. |
| PDF/A manager | 7.0 candidate | Independent conformance validation per claimed profile. |
| Form model | 7.0 candidate | Fill, appearance, save/reopen and viewer fixtures. |
| Attachment management | 7.0 candidate | Inspect `Pdf.Attachments/EmbeddedFilesManager.cs`; verify attach/extract metadata. The changelog calls this “FileManager”, but that exact class name is absent in the snapshot. |
| Annotation model | 7.0 candidate | Rendering/interactivity checks and API migration coverage. |
| XMP metadata policies | 7.0 candidate | Preserve/update/remove metadata fixtures and conformance checks. |
| Dates and nullable values | 7.0 API change; isolate compatible fixes if possible | Offset, timezone, null and cross-runtime fixtures. |
| PDF names | Isolate encoding fixes; reserve new `Name` API for 7.0 | UTF-8, delimiter escaping, empty-name and read/write cases. |
| Page boxes and rectangle semantics | 7.0 review | Inheritance, rotation, missing-box and compatibility fixtures. |
| Annotation constructors, keys and renamed types | 7.0 review | Compile representative 6.2 consumers and document migrations. |
| MigraDoc date/build-information APIs | 7.0 review | Preserve current APIs in 6.2.5; assess replacements together. |
| GitVersion replacement | Build-system candidate, not needed for these fixes | Reproducible assembly/package versions across worktrees and CI. |
| Framework/obsolete-API updates | Review target by target | Actual runtime checks; framework entries already present in 6.2.4 are not new FreePDF features. |
| Automatic assets, fewer-framework and slow-test switches | Build/test candidates | Explicitly pinned assets with provenance; reliable offline regression subset. |
| Broad refactoring/extensibility | 7.0 review | API and behaviour evidence before adoption. |
| PsX Forms / PsX Annotations references | Outside the FreePDF library scope | Separate products; their promotion in a changelog grants no rights to their implementation. |

## Later changelog requirements

These are candidates for independently implemented work, not imported patches.
No 6.5.0 or Preview 2 implementation was read.

| Published area | MIT Preview 1 evidence | FreePDF target / gate |
| --- | --- | --- |
| Non-name dictionary keys | `Parser.cs` still rejects keys that are not `PdfName`. | 6.2.x candidate; define safe coercion and malformed-input limits. |
| Legacy ANSI names | Inspected `Name.cs` decoding uses UTF-8. | 6.2.x candidate; specify deterministic encoding policy and fallback fixtures. |
| PNG out-of-memory propagation | `PngImageImporter.TryImportImage` still catches all exceptions. | 6.2.x candidate; exception-injection test, preserving ordinary format fallback. |
| Unique signature-field names | `DigitalSignatureHandler.cs` still writes `Signature1`. | 6.2.x candidate; existing-field and nested-field collision tests plus signature verification. |
| Concurrent MigraDoc font caching | `FontHandler.cs` retains the separate static weak-reference pair. | High-priority 6.2.x candidate; deterministic concurrent-render regression. |
| 6.5 / Preview 2 licence activation | Changelog describes upstream's AGPL/commercial switch. | Excluded: FreePDF retains its MIT policy. |
| Preview 2 feature sections | The reviewed pages point to general/licence changes and inherited PDFsharp benefits. | No additional separately enumerated library feature found on those pages at review time. |

## Release gates

Before a stable package: finish the chosen fix set, run the expanded CI matrix,
verify generated PDFs independently, review public API compatibility and package
dependencies, and assign distinct FreePDF package IDs. Candidate rows are not
silently treated as complete. Update this matrix with evidence when a row moves.
