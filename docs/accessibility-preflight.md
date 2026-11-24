# Accessibility preflight

FreePDF includes a native, offline **PDF/UA-1 preflight**, with explicitly partial
coverage. It inspects the original bytes without repairing or saving the PDF.
It does **not** certify ADA, WCAG, Section 508 or complete PDF/UA compliance.
There is deliberately no `IsAdaCompliant` property or overall `Passed` outcome.

```csharp
using PdfSharp.UniversalAccessibility;

var report = PdfAccessibilityScanner.Scan("document.pdf");
foreach (var finding in report.Findings)
    Console.WriteLine($"{finding.RuleId}: {finding.Message} (page {finding.Page})");

// A completed scan can still contain failures and always requires review.
Console.WriteLine($"{report.Status}; coverage: {report.Coverage}");
```

The stream overload reads from the current position to EOF, leaves the stream open
and restores its original position when seekable. Reports contain the SHA-256 of
the inspected bytes, a versioned rule set, check outcomes, locations, guidance and
the author-supplied structure order. Report text, including alternative text, is
untrusted document content and can also contain confidential information.

## Implemented checks

All IDs have the prefix `FPDF-A11Y-`. An individual `Passed` means only that the
named check passed for its inspected occurrences. `NotApplicable` means no
applicable occurrence was found. A failure takes precedence over other occurrences;
the findings still record unassessed occurrences and `ScanCompleted` remains false
if any implemented check could not finish. When a limit stops the scan, every check
that had not inspected all its occurrences becomes `NotChecked`; failures already
found stay `Failed`, and `HasFailures` reports them.

| ID | Scope |
| --- | --- |
| READ | Readable without repair: a rebuilt or corrected cross-reference table, or a stream length recovered from `endstream`, makes it `NotChecked`; also bounded inspection and unreadable/password-protected inputs |
| LANG | Presence of a nonempty catalog language |
| TITLE | Nonempty XMP `dc:title` |
| UAID | XMP `pdfuaid:part=1`; PDF/UA-2 is reported as unsupported |
| DISPLAYTITLE | `ViewerPreferences/DisplayDocTitle=true` |
| MARKED / SUSPECTS | Marked-content declaration and suspect-tagging flag |
| TREE | Nonempty structure root, child types, parent references, cycles and duplicate elements |
| ROLE | Standard PDF 1.7 structure roles and bounded custom role-map resolution |
| ALT | Presence of `Alt` or `ActualText` for figures and formulas |
| NESTING | Basic list/item/label/body and table/row/cell parent relationships |
| MCID | IDs within each page or form, structure references, ParentTree entries and repeated use of a form with its own IDs |
| CONTENT | Painting inside tags or artifacts and balanced marked-content scopes in pages and invoked forms |
| FONT | Embedded font programs for visible text in pages and invoked forms, except in text rendering mode 3 (invisible) |
| UNICODE | Unicode mapping for character codes shown on pages and in invoked forms, including invisible text |
| TABS | Structure tab order on annotated pages |
| ANNOT | Structure object references for links/widgets |
| REVIEW | Required human accessibility review |

The Unicode rule checks the codes used by `Tj`, `TJ`, `'` and `"` with the
active font. Selecting an unused font or showing an empty string needs no
mapping. The rule reads one-byte simple fonts and two-byte Identity-H/V codes.
It recognizes explicit WinAnsi, MacRoman and StandardEncoding, the built-in
encodings of unembedded standard Type 1 fonts, and declarative ToUnicode maps
with one code-space range and UTF-16BE `bfchar` or `bfrange` destinations.
For an Adobe-Identity descendant using Identity-H/V, a used code with no map
entry produces a failure with the first missing code, page and font path.

An embedded encoding CMap, an inherited ToUnicode map, a map over 256 KiB,
or another mapping the checker cannot establish produces `NotChecked` for
affected text. The report locates the first unassessed occurrence on each page.
The checker reports a missing code as a failure only when it
can also rule out an alternative mapping. A mapped code still needs a correct
glyph and meaningful text. The check does not evaluate either.

Starting with 7.13.0, the scanner follows each `Do` that draws a local
Form XObject. A form can inherit the caller's tag or artifact, or contain its
own tagged content. The scanner matches each form's IDs to its structure
references and parent tree. A finding includes the page and resource path,
for example `/Resources/XObject/Outer/Resources/XObject/Inner`. A resource name
longer than 127 bytes is cut and ends with `...`. A location longer than 1,024
characters keeps its start and its end, joined by `/...`.

The scanner looks up named property lists in the active resources. A legacy
form that omits its resource dictionary uses the page resources. An unresolved
property, a recursive form or a complex inline property that the scanner
cannot interpret produces `NotChecked`. A definite failure elsewhere in the
document still gives exit code 1.

`PdfAccessibilityScanOptions` sets the scan limits. `MaxDecodedBytes` covers
metadata, page streams and each form invocation. `MaxDepth` bounds nested
forms and XMP elements. `MaxNodes` covers the structure tree, role map and number
tree. Equal consecutive saved graphics states share one entry, and more than
65,536 distinct saved states stop the scan. `MaxDuration` (default 2 minutes,
`null` for no limit) ends a scan that takes longer; the failures found until then
stay in the report. `MaxDuration` and the cancellation token also stop the
reader while it opens the PDF. A limit produces `Incomplete`. Cancellation
propagates to the caller. The metadata reader disables XML DTD processing.

The scanner reads content one operator at a time. If a stream cannot be parsed
to its end, failures found before the error remain in the report, and the scan
is `Incomplete`.

The checks are a starting point, not the complete Matterhorn/PDF/UA test set. They
do not validate all Unicode mappings, embedded glyph correctness, all heading/table
semantics, widget accessible names, annotation appearance streams, optional content,
all parent-tree rules, security permissions or PDF/UA-2. Language presence does not
prove its correctness; alternative-text presence does not prove its usefulness.
Use an independent validator such as veraPDF, document review and actual
assistive-technology testing before making a conformance claim. The HTML structure
listing is a review aid, not a screen-reader transcript.

The library's limits are not a security sandbox. Process untrusted files in an
isolated worker with external time/memory limits. The regression tests scan six
hostile inputs that each once needed between 1 and 5 GB; each must now finish
within a 256 MiB .NET heap. That covers known cases, not every possible file.
Opening a PDF keeps all its objects in memory, about 500 bytes each. A file
within `MaxFileBytes` that packs millions of small objects into compressed
object streams can therefore need several GB: 19 MiB with 3 million objects
used 1.5 GB and opened in 6 s, well within the default `MaxDuration`.
The corpus runner demonstrates
per-file process isolation, timeouts and a .NET GC heap limit; its inputs are
synthetic, and a GC heap limit is not a total process-memory limit.

## Command line and reports

Start with a PDF and the .NET 10 SDK. From the repository root:

```sh
dotnet run --project src/tools/FreePDF.Accessibility -c Release -- document.pdf
```

The default text summary says whether it found problems, needs human review or
could not finish. Findings explain the problem and the next step. A result of
"Human review required" means the implemented checks found no failures; it does
not mean the document has passed all accessibility requirements. For example,
software can find a missing image description, but a person must decide whether
an existing description explains the image correctly.

Build the .NET 10 CLI from source:

```sh
dotnet build src/tools/FreePDF.Accessibility/FreePDF.Accessibility.csproj -c Release
dotnet src/tools/FreePDF.Accessibility/bin/Release/net10.0/FreePDF.Accessibility.dll \
  document.pdf --format html --output audit.html
```

Formats: `text` (default), `json`, `sarif` (2.1.0) and script-free `html`. The text
summary shows up to 20 findings, prioritizing failures and incomplete checks.
It escapes terminal control characters from filenames and document text. Use
HTML or JSON for all findings. HTML escapes
document strings and loads no external resources. SARIF includes rule IDs,
guidance, document hashes, page/path locations, review tasks and coverage metadata.
JSON and HTML reports cut alternative text in the structure order after 1,000
characters and end it with `...`, because many elements can share one long text.
`PdfAccessibilityStructureEntry.AlternativeText` in the API keeps the whole text.
Output paths must be new files: the CLI refuses to overwrite an existing report or
PDF. Use `--password-env NAME` for an environment variable containing the password.
`--timeout SECONDS` (1 to 3600, default 120) sets `MaxDuration` for the native scan.
The password is never included in the report.

| Exit | Meaning |
| --- | --- |
| 0 | Explicit `--policy automated` satisfied; human review still required |
| 1 | At least one implemented check failed, also when the scan is incomplete |
| 2 | Human review required (default policy for otherwise successful scans) |
| 3 | Incomplete scan without a detected failure: unsupported content, a repaired file or limits |
| 64 | Invalid command/options or input/output error |

A detected failure decides the exit code. In the report, `status` stays
`Incomplete` when some content could not be checked, `hasFailures` says whether a
check failed, and `scanCompleted` says whether every check finished. SARIF carries
both values in the run properties. `--policy automated` never turns an incomplete
scan into success.

The CLI is published on NuGet.org as the .NET tool `io7.FreePDF.Accessibility.Tool`
(command `freepdf-a11y`) since 7.11.0. The scanner itself ships in the existing
library assemblies, including the Core, GDI and WPF variants.

```sh
dotnet tool install --global io7.FreePDF.Accessibility.Tool
freepdf-a11y document.pdf --format html --output audit.html
```

To use a build from a source checkout instead, pack and install it into a new
local directory (run from the repository root):

```sh
dotnet pack src/tools/FreePDF.Accessibility -c Release -o artifacts/a11y-package
dotnet tool install io7.FreePDF.Accessibility.Tool --tool-path artifacts/a11y-tool \
  --source artifacts/a11y-package
artifacts/a11y-tool/freepdf-a11y document.pdf
artifacts/a11y-tool/freepdf-a11y document.pdf --format html --output audit.html
```

Open `audit.html` in a browser. The PDF stays on your machine and is not modified.
The report can include document content, so handle it with the same care as the PDF.

## Validation and next coverage

FreePDF 7.12.0 adds an optional [veraPDF adapter](https://github.com/io7/FreePDF/blob/main/docs/accessibility-verapdf.md)
and a [complete Matterhorn inventory](https://github.com/io7/FreePDF/blob/main/docs/accessibility/README.md). Use
`freepdf-a11y --requirements` to inspect the inventory. Independent machine
results remain separate from native findings and human review.

The regression suite includes positive, negative, cyclic, encrypted, malformed,
resource-limit and original-byte-preservation cases. CLI contracts test exit
policies, JSON/SARIF structure, HTML escaping and output overwrite protection.
CI runs the installed CLI on nine synthetic files with veraPDF 1.30.3. The
adapter tests record each validator's result, including the
[repeated-form difference](https://github.com/io7/FreePDF/blob/main/docs/accessibility-verapdf.md#validation).

Future releases can extend the versioned rules with additional encodings and glyph validation,
complete annotation/form/table rules,
then generation preflight and reviewed remediation. Automatic repair, AI-written
alternative text, PDF/UA-2 and organizational review sign-off are not part of this
first implementation.

See the [accessibility roadmap](https://github.com/io7/FreePDF/blob/main/docs/accessibility-roadmap.md) for the proposed path
to complete automated coverage and a documented human review process.

References: [Matterhorn Protocol](https://pdfa.org/resource/the-matterhorn-protocol/),
[W3C PDF techniques](https://www.w3.org/WAI/WCAG22/Techniques/pdf/),
[veraPDF validation scope](https://docs.verapdf.org/validation/).
