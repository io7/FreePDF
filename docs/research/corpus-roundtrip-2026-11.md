# Corpus round trip and mutation fuzzing (November 2026)

Question: which real files does FreePDF fail to open, save or reopen, and where does malformed input crash it
instead of being refused with `PdfReaderException`?

## Tool

`eng/research/CorpusCheck` (references the FreePDF sources directly):

- `run <corpus> <out>` opens every PDF for modification, saves it, reopens the result and compares page counts.
  Each file runs in its own child process, so a stack overflow or hang fails that file only. Page 1 of the original
  and the re-saved file is rendered with Poppler (`pdftoppm`, 40 dpi, grey) and compared; more than 0.5 % changed
  pixels is flagged. Outcomes: ok, rejected (`PdfReaderException`), password, crash-open/save/reopen,
  pages-changed, render-diff, timeout. `summary.md` groups failures by exception and first FreePDF frame.
- `fuzz <seeds> <out>` mutates seed PDFs (bit flips, syntax characters, deleted, duplicated or inserted ranges,
  truncation, boundary numbers next to `obj`, `/Length`, `xref`, `startxref`, `/Size`, `/Prev`, `/W`, `/Index`)
  and runs them through the same child check with a 10 s timeout and a 512 MB GC heap limit. Anything other than
  ok or a reader refusal is a finding; the smallest reproducer per signature is kept.

```sh
dotnet build eng/research/CorpusCheck -c Release
dotnet eng/research/CorpusCheck/bin/Release/net10.0/CorpusCheck.dll run <corpus-dir> <output-dir>
dotnet eng/research/CorpusCheck/bin/Release/net10.0/CorpusCheck.dll fuzz <seed-dir> <output-dir> --minutes 30
```

## Corpora

| Corpus | Files | Source |
| --- | --- | --- |
| veraPDF corpus | 2,906 | https://github.com/veraPDF/veraPDF-corpus at `bb75f4f0073d9350dfd058c0162a367e6fadf25e` (CC BY 4.0) |
| Producer set | 44 | `eng/research/make_producer_corpus.py`: original sources through pdfLaTeX, XeLaTeX, Chrome headless, LibreOffice (docx, PDF/A-2 tagged, HTML), Ghostscript (default, 1.4, ebook, PDF/A-2, owner-encrypted), MuPDF (clean, decompressed ASCII, AES-256, AES-128 with user password, RC4), cairo (`pdftocairo`, Inkscape) and Poppler (`pdfunite`, `pdfseparate`) |

Neither corpus is committed.

## Results

| Corpus | Before (main, 2026-11-02) | After the fixes below |
| --- | --- | --- |
| veraPDF corpus | 2,878 ok, 22 crashes, 3 rejected, 3 password | 2,899 ok, 0 crashes, 4 rejected, 3 password |
| Producer set | 32 ok, 3 password, 6 render-diff, 3 render-failed | 32 ok, 12 password |

All 22 crashes and all 9 producer-set anomalies traced to six defects, each fixed with original regression tests:

| Defect | Files | Fix |
| --- | --- | --- |
| Encryption dictionary stored directly in the trailer (MuPDF) was ignored: protected files opened without a password, content could not be decrypted, saving wrote encrypted streams into an unencrypted file | 9 producer files | Reader prepares a direct `/Encrypt` like an indirect one |
| Unknown annotation subtype (private, `/FREETEXT`, missing) failed the whole document | 7 veraPDF, incl. PDF/UA-1 passes | `PdfUnknownAnnotation` keeps it as read |
| Stream with external file data (`/F`) threw `NotImplementedException` (upstream issue 389) | 10 veraPDF | Parsed by `/Length`; the external file is never loaded |
| `%PDF-2.n` header above 2.0 refused; unreadable header threw `InvalidOperationException` | 2 veraPDF (one a PDF/A-4 pass) | 2.1-2.9 read and saved as 2.0; `PdfReaderException` for unreadable headers |
| Trailer `/Info` referencing a missing object threw `ArgumentNullException`; missing objects threw `AggregateException` (upstream issue 73) | 1 veraPDF | Dangling `/Info` treated as absent; `PdfReaderException` |
| Annotations stored directly in `/Annots` left retired dictionaries behind, so saving failed ("This dictionary cannot be used anymore") | 2 veraPDF | Typed annotation replaces the direct entry |

The "render-failed" files were the AES-128 files with a user password: Poppler could not render the original
without the password, while FreePDF had opened them without one (the first defect).

Reading the code around these defects also turned up a latent leak: the verbose writer layout had fields for the
user and owner passwords in its plain-text statistics comment (the security manager is still a stub that printed
"(TODO)"), now removed.

## Mutation fuzzing

Campaign 1 (2026-11-03, seed 1, 30 minutes, 3 workers, seeded from both corpora): 51,281 inputs. 44,630 were
refused with `PdfReaderException` and 6,291 opened, saved and reopened; 273 crashed while opening, 39 while saving
and 2 on reopening, in 20 distinct signatures. All 20 reproducers now open and save (7) or are refused with
`PdfReaderException` (13).

Defects where a file opened but could not be saved, or saved into a file that could not be reopened:

| Defect | Fix |
| --- | --- |
| An outline root without items wrote nothing but stayed in the cross-reference table. Any new document that read `document.Outlines` without adding an item saved a file FreePDF itself could not reopen | The empty root is written |
| Outline `/First` and `/Next` links were followed and converted without checks: a loop back to an earlier item exhausted memory, a link to the catalog or a page turned that object into an outline item | The outline ends at a link that does not lead to an untyped dictionary |
| A catalog `/Metadata` or `/Outlines` that is `null` or dangling failed with a type mismatch | Typed reads treat `null` as absent (ISO 32000-1 7.3.9) |
| A catalog without a page tree opened as an empty document; saving dropped every page | `PdfReaderException` |

Defects where damaged input escaped as the wrong exception type, now `PdfReaderException` or recovered:

| Input | Was | Now |
| --- | --- | --- |
| Cross-reference stream `/Size`, `/W`, `/Index`, `/Prev` missing, mistyped or beyond the data | `InvalidOperationException`, `IndexOutOfRangeException` | `PdfReaderException`; a huge `/Index` is refused before the loop |
| Object stream `/N`, `/First` or header entries invalid; container missing, not a stream or already typed | `OverflowException`, `NullReferenceException`; `/N` header slots allocated up front; the named object converted | `PdfReaderException` |
| Cross-reference or object stream with an unknown filter or corrupt Flate data | `NotImplementedException`, `InvalidDataException` | `PdfReaderException` with the filter error inside |
| Trailer `/Root` referring to a missing object | `ArgumentNullException` | `PdfReaderException` |
| Reference to a missing object inside a direct array or dictionary (a page's `/Annots`) | `InvalidOperationException` on access | `null` (7.3.10); non-dictionary `/Annots` entries dropped |
| Page without `/MediaBox` | `InvalidOperationException` | US Letter, as Poppler and MuPDF show it |
| Indirect `/Length` that is not an integer | `InvalidCastException` | Recovered from `endstream` |
| `stream` keyword inside an array or dictionary; number with two decimal points | `NotImplementedException`, `ContentReaderException` | `PdfReaderException` |
| AES-256 `/Perms` not 16 bytes | `CryptographicException` | `PdfReaderException` |

Reading the cross-reference stream code also showed that entries without a type field (`/W` starting with 0) were
read as free objects; ISO 32000-1 Table 17 makes them in-use objects.

After these fixes the veraPDF corpus and the producer set give the same results as before them (2,899 ok, 3
password, 4 rejected; 32 ok, 12 password).

## Debug-only test failures on main

- `CompositeGlyphTests` failed a Debug assertion in `OpenTypeFontFace.GetOrCreateFrom` for a font source that already
  had a face; it now returns the cached face.
- `SemanticRoundTripTests` failed in Debug builds because a Debug-only path read the direct `/EmbeddedFiles`
  dictionary untyped first. That marked it "transformation tried", and the typed read that counts embedded files then
  failed with a type mismatch. Any caller could trigger the same with an untyped read, in any build. A type the caller
  asks for explicitly is now always tried, as it already was for indirect objects. Debug and Release both pass all
  298 tests.
