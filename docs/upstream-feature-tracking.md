# Upstream feature tracking

Reviewed 2026-10-30. This matrix guides FreePDF's work; it is not a claim of
feature parity or a promise to ship on upstream's schedule.

**Release policy:** FreePDF 7.0.0 is the next major release. The current
`7.0.0-dev.1` adopts the reviewed MIT Preview 1 source and carries the earlier
6.2.5 development fixes. It is not a stable package or a promise to follow
upstream release dates. Preview 2 licensing activation is excluded.

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

## Earlier fixes retained in 7.0.0

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

## MIT Preview 1 source now imported

The complete reviewed MIT snapshot is imported. The last column remains a
release gate unless explicitly evidenced below; import does not certify behavior.

| Changelog area | FreePDF direction | Acceptance requirement |
| --- | --- | --- |
| PDF object model and dictionary nullability | Imported in v7 | Public API comparison and migration tests. |
| Tolerant reading | Imported; independent tolerance fixes added | Malformed fixtures must terminate; valid inputs must retain semantics. |
| PDF/A manager | Imported in v7 | Independent conformance validation per claimed profile. |
| Form model | Imported in v7; several typed classes are placeholders | Lab tests a low-level checkbox appearance/readback. Full fill/flatten/typed-field support is not claimed. |
| Attachment management | Imported in v7 | Inspect `Pdf.Attachments/EmbeddedFilesManager.cs`; verify attach/extract metadata. The changelog calls this “FileManager”, but that exact class name is absent in the snapshot. |
| Annotation model | Imported in v7 | Rendering/interactivity checks and API migration coverage. |
| XMP metadata policies | Imported in v7 | Preserve/update/remove metadata fixtures and conformance checks. |
| Dates and nullable values | Imported v7 API changes | Offset, timezone, null and cross-runtime fixtures. |
| PDF names | Imported `Name` API; ANSI fallback added | UTF-8, delimiter escaping, empty-name and read/write cases. |
| Page boxes and rectangle semantics | Imported in v7 | Inheritance, rotation, missing-box and compatibility fixtures. |
| Annotation constructors, keys and renamed types | Imported in v7 | Compile representative 6.2 consumers and document migrations. |
| MigraDoc date/build-information APIs | Imported in v7 | Review v7 replacements and publish migration guidance. |
| GitVersion replacement | Replaced by FreePDF version generation | Reproducible assembly/package versions across worktrees and CI. |
| Framework/obsolete-API updates | Core/Standard/Windows targets retained | Actual runtime checks; framework entries already present in 6.2.4 are not new FreePDF features. |
| Automatic assets, fewer-framework and slow-test switches | Imported switches; automatic download is opt-in | Explicitly pinned assets with provenance; reliable offline regression subset. |
| Broad refactoring/extensibility | Imported in v7 | API and behaviour evidence before adoption. |
| PsX Forms / PsX Annotations references | Outside the FreePDF library scope | Separate products; their promotion in a changelog grants no rights to their implementation. |

## Later changelog requirements

The five functional requirements below are independently implemented in v7, not imported patches.
No 6.5.0 or Preview 2 implementation was read.

| Published area | MIT Preview 1 evidence | FreePDF target / gate |
| --- | --- | --- |
| Non-name dictionary keys | `Parser.cs` still rejects keys that are not `PdfName`. | Implemented: string/integer keys coerced to names; other types rejected. Synthetic read/save/reopen tests. |
| Legacy ANSI names | Inspected `Name.cs` decoding uses UTF-8. | Implemented: strict UTF-8, then current culture ANSI code page; CP1252/Shift-JIS/valid UTF-8 tests. Encoding is inherently ambiguous without external metadata. |
| PNG out-of-memory propagation | `PngImageImporter.TryImportImage` still catches all exceptions. | Implemented in importer and outer fallback; impossible-array-length PNG test without allocating gigabytes. |
| Unique signature-field names | `DigitalSignatureHandler.cs` still writes `Signature1`. | Implemented: scan root/nested fields and choose unused name; actual field insertion tested. Cryptographic validity remains a separate gate. |
| Concurrent MigraDoc font caching | `FontHandler.cs` retains the separate static weak-reference pair. | Implemented: atomic immutable weak-reference pair with property validation; 40,000 parallel conversions and mutable-font test. Full concurrent layout coverage remains open. |
| 6.5 / Preview 2 licence activation | Changelog describes upstream's AGPL/commercial switch. | Excluded: FreePDF retains its MIT policy. |
| Preview 2 feature sections | The reviewed pages point to general/licence changes and inherited PDFsharp benefits. | No additional separately enumerated library feature found on those pages at review time. |

## Additional evidence and release gates

The comparison lab covers twelve feature cases, including two-page layout,
attachment extraction and a checkbox appearance. Locally, nine cases match
exactly and three expose independently fixed reference defects. Core StartFigure
(#396), full-width merged tables (#358), and truncated hex strings also have
original unit regressions. See `7.0.0-validation.md` for the exact scope.


Before a stable package: finish the chosen fix set, run the expanded CI matrix,
verify generated PDFs independently, review public API compatibility and package
dependencies, and assign distinct FreePDF package IDs. Candidate rows are not
silently treated as complete. Update this matrix with evidence when a row moves.
