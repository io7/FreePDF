# FreePDF 7 support and release policy

## Runtime and package scope

Use a maintained, patched .NET 10 or .NET 11 runtime for new applications. The
portable packages contain `net8.0`, `net9.0`, `net10.0` and `netstandard2.0`
assets; .NET 11 applications use the `net10.0` assets. CI executes package
consumers on Linux and Windows for .NET 8/9/10/11 and on Windows for Framework
4.6.2/4.7.2 targets, and runs the regression tests of the `net10.0` build on the
.NET 11 runtime. The .NET 11 consumers install the same candidate packages that
are published; the packages are built with the .NET 10 SDK. Framework execution
uses the runner's maintained .NET Framework 4.8 runtime; this does not certify
historical CLR installations. Prefer Framework 4.8/4.8.1 on an OS supported by
Microsoft.

As checked on 2026-11-10, .NET 8 and 9 reached the end of Microsoft's support on
2026-11-10; .NET 10 (LTS) and .NET 11 (STS, released 2026-11-10) are supported
through 2028-11-14. The compatibility assets can remain available after runtime
support ends, but FreePDF cannot provide security servicing for Microsoft's
runtime. Consult the current policy before deployment:
https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

Tested OS profiles are Ubuntu 24.04 and Windows Server 2022 in CI, plus a
dedicated Windows lab host. macOS, browser/WASM, mobile, Native AOT and
trimming are not validated release profiles.

The GDI and WPF packages (`io7.FreePDF.Gdi`, `io7.FreePDF.Layout.Gdi`,
`io7.FreePDF.Wpf`, `io7.FreePDF.Layout.Wpf`) contain `net8.0-windows7.0`,
`net9.0-windows7.0`, `net10.0-windows7.0` and `net462` assets. CI executes their
package consumers on Windows for `net8.0-windows`, `net11.0-windows` and Framework 4.7.2, including
an image drawn from the platform's own bitmap type. Their drawing results follow
GDI+ or WPF on the host and are not covered by the cross-platform pixel comparisons.

## Supported feature claims

The release gates cover drawing and text with explicit font resolution and
with installed TrueType fonts found without a font resolver (Core build),
paragraph/table layout and PDF/RTF generation, page import/merge/stamping,
attachments, AES-128/256 password encryption, and new detached CMS signatures
using SHA-256/384/512. Original PDF/A-1b, 2b and 3b fixtures are independently
validated. PDF/A conformance is a property of each output document; validate
your own output, fonts and images before archival use. Since 7.8.0, PDF/A-1 CID
fonts carry a CIDSet, and saving an opened file whose XMP claims PDF/A keeps its
XMP in line with the document information and adds no page transparency group;
veraPDF validated such files in the 7.8.0 release checks.

Merging and splitting with `PdfDocument.AppendPages` and `ExtractPages` carry
form fields, bookmarks, links and GoTo actions; regression tests cover them, and
a corpus of 2,943 veraPDF and producer files was appended and split without
errors before the 7.3.0 release. Tags (the structure tree), page labels,
optional content settings and XFA are not carried, and copied signatures cannot
be valid in the new file.

Saving a document opened with `PdfDocumentOpenMode.Append` writes an incremental
update and keeps existing signatures valid. Regression tests check that the
original bytes stay unchanged, that only new and changed objects are written, and
that a signature made before filling in a field or adding a second signature
still verifies. The release checks also validated signed, filled and
countersigned files with poppler's pdfsig and pyHanko (whose difference analysis
rated the changes as signing and form filling), countersigned a file that pyHanko
had signed, and saved each of 2,930 veraPDF and producer files unchanged (same
bytes) and with one change (only the changed objects written) before the 7.4.0
release. Since 7.5.0, encrypted documents keep their encryption: regression tests cover RC4
and AES-128/256 files made by FreePDF, a form that pyHanko encrypted with AES-256
and signed was filled in and countersigned by FreePDF with both signatures valid
in pyHanko and pdfsig, and the 13 encrypted corpus files were saved unchanged
(same bytes) and with one encrypted change. Since 7.5.1, empty streams and strings in AES-encrypted
documents are encrypted too (32 bytes), so pyHanko can read them. Since 7.5.2, documents with
`EncryptMetadata` false whose metadata streams are encrypted (as pyHanko writes them) open and read
correctly: regression tests cover RC4 and AES, and 400 pyHanko files were read. Since 7.6.1,
FreePDF writes only the document's own metadata unencrypted when `EncryptMetadata` is false, as
ISO 32000-1 specifies, and pyHanko reads the page metadata of such files. Since 7.7.0, pages
can be added, removed and moved in Append mode also when the page tree is nested: regression
tests check the objects each update writes and the tree's counts and parents, and nested files
that pyHanko signed were edited with page sizes and rotation intact in poppler and MuPDF and
every signature valid in pdfsig.

Since 7.5.0, an Append-mode save refuses changes to signed signature fields and to fields a
signature locks (FieldMDP, `/Lock`), and changes a certification signature
(DocMDP) does not permit. Regression tests cover each permission level against
filling in fields, signing, the document information, comments, page content,
pages and the catalog; changing, removing and moving signed and locked fields and
the objects they refer to; and saving to a path that cannot be written. pyHanko's
difference analysis was run on forms that pyHanko certified (P=1, 2, 3) or signed
with and without a field lock, also with nested fields, and that FreePDF then
changed: every change FreePDF saved for which pyHanko has a rule was accepted, and
every change FreePDF refused was rejected by pyHanko too. pyHanko's default policy
is stricter than the permissions in six places, which FreePDF follows the
permissions in: added comments, new visible signature fields after a
certification, any change to pages after an ordinary signature, new signature
fields after a signature that locks all fields, new fields other than signature
fields after an ordinary signature (checked with 7.9.0's `PdfForm.Add…` methods),
and selecting a radio button after a certification that permits form filling
(the group's value and the button's appearance state change; pyHanko rejects
pypdf's update with the same two objects too).

Since 7.6.0, `DigitalSignatureOptions.FieldName` signs an existing, empty signature
field. Regression tests cover a field that is its own widget, one with a separate
widget and an invisible one; a field lock recorded as FieldMDP and then enforced;
certification levels; fields locked by an earlier signature; and seed values.
Forms that pyHanko made with an empty field (plain, with a field lock, with a
required reason, certified with P=2) and FreePDF signed are VALID in pyHanko, the
certification also in poppler's pdfsig; after pyHanko changed the field that the
lock names, pyHanko judged FreePDF's signature INVALID, as it must. pyHanko's
default policy reports earlier signatures as changed when the signed field's
widget is a separate object, or the field has several widgets or shares an
appearance; fields that are their own widget pass.

Since 7.9.0, `PdfForm` creates text, check box, radio button, combo box, list box
and signature fields with appearances. Regression tests read created fields back
with their types, widgets, rectangles and appearances, fill them after reopening,
flatten, merge and sign them. Before the release, MuPDF and pypdf read the type,
value and on states of every created field in new, filled and reopened-then-filled
files, and MuPDF and poppler rendered them. Text and choice fields use Helvetica,
which is not embedded, so PDF/A documents (new, or opened with an XMP PDF/A
claim) refuse them. Fields are not tagged yet, so tagged and PDF/UA documents
refuse all of them. `DigitalSignatureOptions.Certification` writes a
certification signature (DocMDP), only as the document's first signature, and
honours a signature field's seed value for the signature type (/MDP); regression
tests check the signature reference, the catalog's /Perms and FreePDF's own
enforcement, and pyHanko read levels 1 to 3 from FreePDF's files and judged
filling text fields, check boxes and combo boxes after certification at levels 2
and 3 as permitted (VALID); radio buttons are one of the places listed above.

Factur-X / ZUGFeRD output (`FacturXManager`) is checked for the PDF side: a
PDF/A-3b file validated by veraPDF, with the invoice XML embedded, listed in
`/AF` and declared in the XMP extension schema. FreePDF only checks that the XML
is a well-formed Cross Industry Invoice; it does not validate invoice content
against EN 16931, a profile or a national CIUS such as XRechnung.

Since 7.9.0, QR codes (`CodeQR`), Data Matrix ECC 200 (`CodeDataMatrix`),
Code 128 (`Code128`, also GS1-128), EAN-13, EAN-8 and UPC-A are encoded from
their ISO/IEC standards and drawn as vector shapes, also by MigraDoc's
`Barcode` element. Regression tests compare QR
codewords with a published example, check the Reed-Solomon codewords of
interleaved blocks and the format and version information, and compare Code 128
symbol values and EAN/UPC check digits with worked examples. Before the release,
every QR version 1 to 40 at every error correction level and in each mode, at full
capacity, and text with UTF-8, control characters and payment-code content, was
decoded by an independent reader (zxing-cpp), also with one copy of the format or
version information destroyed; drawn codes of each type were decoded from pages
rendered by MuPDF and poppler. Data Matrix regression tests compare the
codewords of the standard's encodation example and of ASCII, Upper Shift, ECI,
FNC1 and Base256 encodation, and check finders, clocks and the Reed-Solomon
blocks of 52 x 52 and 144 x 144; before the release, symbols of all 30 sizes at
full capacity and with Latin-1, UTF-8, GS1 and Base256 content were decoded by
zxing-cpp, and drawn symbols from MuPDF and poppler renders. Whether a printed
code scans also depends on its size, contrast, quiet zone and print quality.

Tagged PDF/UA-1 output from the layout engine (`PdfDocumentRenderer.TaggedPdf`)
covers headings (outline levels 1 to 6), paragraphs, lists, tables with heading
rows, images and charts with alternative text, hyperlinks and text frames;
headers, footers, borders and shading are artifacts. A multi-page fixture with
all of these is validated by veraPDF's PDF/UA-1 profile in CI. veraPDF checks
what a machine can check; reading order, alternative text quality and heading
structure need human review (the Matterhorn Protocol lists these checks). A
table split across pages is one Table element, with heading rows repeated on
later pages as artifacts. PDF/UA-2 is not covered.

The signing checks verify cryptographic integrity and tamper detection using
synthetic self-signed certificates. They do not establish trusted identity,
revocation checking, timestamp-authority behaviour, PAdES/LTV or a legal
signature status. No FIPS certification is claimed. MD5 exists only for legacy
PDF-format operations and checksums, never as the recommended signature digest.

Filling or flattening form fields with text the form's fonts cannot encode in WinAnsi, incremental updates of
documents encrypted with a public-key or other non-standard security handler, PDF/UA for documents drawn without the
layout engine, arbitrary PDF/A conversion, form fields created in PDF/UA documents, text and choice fields created in PDF/A documents, and complex script shaping are not promised in 7.9. Placeholder inherited types are not
evidence that those features are complete.

## Maintenance and security

io7 maintains FreePDF independently. Updates need not match PDFsharp's cadence.
Reproduce issues on the latest FreePDF patch and a supported runtime. Routine
bug/security fixes preserve the supported public API within 7.x; incompatible
changes require a major version, with an explicit exception only when a security
fix cannot safely preserve behaviour. Release notes must explain any exception.
Maintenance is best effort; this policy does not promise an SLA.

Keep documents and passwords out of public issues. Submit an original minimal
reproducer where possible; follow `SECURITY.md` for sensitive reports. Process
untrusted documents with application-level time and memory limits. The bounded
mutation corpus is a regression gate, not proof against every malformed PDF.
Since 7.8.0, a file whose cross-reference information cannot be read is rebuilt
from its objects, as viewers do; a rebuilt file may lack objects its damage hid,
and Append mode refuses it.

## Promotion to stable

Every candidate requires green regression, API, package-install, advisory,
independent-validator and comparison-lab checks. The exact candidate packages,
source revision, tool versions, baseline hashes and evidence must be retained.
Known visual changes need an explanation and regression coverage; a green pixel
match alone is insufficient. Review the migration guide and third-party audit.

The plain `7.0.0` package rehearsal installs from an isolated local source and
verifies versions in all eleven assemblies. Rehearsal artifacts are unpublished
and must not be mistaken for a released package. A maintainer reviews the final
candidate and explicitly authorizes public publication; the checks contain no
NuGet publishing step. The separate manual `Publish verified packages` workflow
requires successful checks at its exact source revision and only publishes when
its explicit publish input is enabled. See `releasing.md` for setup and verification.
