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
synthetic self-signed certificates and, since 7.10.0, a test PKI with an
in-process OCSP responder, CRLs and an offline time-stamp authority. They do
not cover real certificate authorities' and time-stamp authorities' services,
EU trusted lists or a legal signature status. No FIPS certification is claimed. MD5 exists only for
legacy PDF-format operations and checksums, never as the recommended signature
digest.

Since 7.10.0, `PdfSharpDefaultSigner` writes PAdES B-B and B-T signatures
(ETSI.CAdES.detached) on request. Regression tests check the CMS attributes
(content-type, message-digest, signing-certificate-v2 with the certificate's
hash, issuer and serial number, no signing-time) and the signature time-stamp
against an offline test authority. pyHanko validated FreePDF's B-B and B-T
files, SHA-256 and SHA-512, also countersigned in Append mode and countersigned
after a certification for form filling (the update that adds the ESIC extension
is rated "form filling"), as valid with valid time-stamps; poppler's pdfsig
judged the same signatures valid but does not report time-stamps. These checks
cover CMS validity and the attributes above, not full ETSI profile conformance:
no conformance checker was run. Signature time-stamps need .NET 8 or later.
Since 7.10.0, FreePDF also writes validation data into the document security
store and adds document time-stamps (B-LT, B-LTA), in Append mode and also under
a "no changes" certification. Regression tests check the DSS entries, that the
signed bytes stay as they were, the document time-stamp token against the covered
bytes, and the permission rules at P=1. pyHanko 0.38.1 rated the DSS and document
time-stamp updates as LTA updates (also after a P=1 certification), judged the
document time-stamps valid and trusted, and its AdES LTA validation (EN 319 102-1
5.6.3) passed offline with the test certificates as trust anchors.
`AddValidationDataAsync` was tested against a test PKI (root, intermediate,
signer, time-stamp authority) whose OCSP responder and CRLs answer in-process:
chains, OCSP responses, the CRL fallback, revoked and unreachable cases. With
only that PKI's root trusted, revocation data required and online fetching off,
pyHanko judged the B-LT file VALID from the DSS alone, and its AdES LTA
validation of the B-LTA file passed now and when simulated 30 days later, after
the signer's 2-day certificate had expired; the B-T file without a DSS found no
chain. Real CAs' responders, delegated OCSP responders and partitioned or delta
CRLs were not tested. poppler's pdfsig does not recognise document time-stamps.

Since 7.10.0, `PdfSignatureVerifier` checks signatures. Regression tests cover
valid, untrusted and tampered signatures, signature time-stamps, B-LTA in the
test PKI with revocation from the DSS, an expired signer validated at its
time-stamp, a revoked signer, missing revocation data, online revocation,
countersignatures, and later updates allowed at P=2 and forbidden at P=1. On a
corpus of such files pyHanko 0.38.1 gave the same verdicts, with two
differences: after a P=1 certification, an update that adds document
information is accepted by pyHanko (its policy allows such updates) and is a
forbidden change for FreePDF, which follows ISO 32000-2 ("no changes" but
validation data and document time-stamps); and a signer revoked before the
signing time is "indeterminate" in pyHanko's AdES validation and "Invalid" in
FreePDF. Tests also cover attacks found in review: a time-stamp from an
untrusted authority (its time is not used, so it cannot rescue an expired
signer), a forged delegated OCSP responder (the responder certificate must be
signed by the CA, carry id-kp-OCSPSigning and id-pkix-ocsp-nocheck, and be valid
when the response was produced), a later trailer that points at another
catalog, changed page content or a page-covering comment after an approval
signature, a visible "document time-stamp" field after a "no changes"
certification, a later signature field without an intact signature, a
signature dictionary copied into a later update, a signing-certificate
attribute for another certificate, a signature removed from the form, "revoked"
in the DSS next to an older "good", a CRL issued after the certificate expired,
a decompression bomb in the DSS, a malformed signature dictionary, and a
certificate not meant for signatures, a later field that points at a signed
dictionary, a time-stamp authority revoked later for compromise, and a
"document time-stamp" widget that is NoView but prints. Four review rounds (two
by Claude agents, two by Codex) found these. Files signed and countersigned by
pyHanko 0.38.1 verify as Valid. The verifier is conservative: SHA-1 and MD5 digests, unverifiable time-stamps, data after the
signature in /Contents, delta and partitioned CRLs and unknown critical CRL
extensions make a result Indeterminate or are not used, and a PAdES level is
reported only for valid signatures. A visible empty signature field added or
changed after a signature makes that signature Indeterminate until the field
is signed. The appearance streams that filling in a form field writes (which
ISO 32000 permits after a signature) are not inspected for what they draw.
Since 7.10.1 an object that a page draws, or that decides how every page shows
(the catalog's optional content properties and output intents), gets no
exemption for later changes, whatever other role it also has, and an exemption
for a role (an appearance, an annotation, the document information or
metadata, validation data) holds only for an object the document reaches
through that role alone. Actions and JavaScript are not judged for what they
would show or hide when run.
The check of later changes has two known limits. First, it assumes that an
ordinary producer made the signed revision. A file that was built before
signing so that one object has two roles can pass a later change to that
object. Two cases are known. One is an array that is a page's /MediaBox and
also its /Annots. The other is a bookmark that a form widget also lists as an
appearance. Second, the check does not compare the values that FreePDF sets
when it opens a file. These are the /Parent of a page, the /Parent, /Prev and
/Last of a bookmark, and the /SigFlags of a form that is an object of its own.
A page's place in the page tree comes from /Kids, which the check does
compare. In a test, poppler 25.03.0 and MuPDF 1.25.1 took the values that a
page inherits through /Kids and did not use a changed /Parent. Other viewers
were not tested. Where no change after the signature is acceptable, require
`CoversWholeFile`, or compare the revisions yourself.
Since 7.10.1 a file with more than `MaxRevisions` revisions or `MaxSignatures`
signatures (64 each by default) throws instead of being checked, earlier
revisions are read one at a time and only where a revision ends, and DSS
inflation counts against its budget, which bounds the cost of a hostile file.
Outline items more than 200 levels deep are not read, so a deeply nested outline
cannot overflow the stack. Since 7.10.1 the reader also refuses arrays and
dictionaries nested more than 256 levels and form fields nested more than 100,
does not follow a chain of indirect stream lengths, and bounds every walk up
through a field's /Parent; a signature check throws when an earlier revision
is refused that way. XMP metadata over 16 MiB is not read. Known effects of
these limits: revisions are counted by the `%%EOF` markers in the file, so a
file that embeds many other PDFs can exceed `MaxRevisions` with one revision;
field names in results are cut at 1,000 characters; in a signed file with a
form field name over 4,096 characters, updates after a signature are not
compared, so that signature is not reported Valid, and FreePDF does not save
such a file in Append mode; and bookmarks more than 200 levels deep are left out when
pages are merged or extracted. Since 7.10.1 the
cross-reference and object streams the reader decodes while opening a file
decode to at most 64 MiB each, and to at most 256 MiB or 64 times the file's
size together, counting every filter stage's output, also output a predictor
then drops or that damaged data cuts short; a file beyond that throws
`PdfReaderException`. Other streams
(page content, images, fonts) are decoded only when they are used, without
such a limit. It
is a technical check: EU trusted lists,
qualified status, signature policies and ETSI validation reports are not
covered, and RSASSA-PSS signatures on certificates, OCSP responses and CRLs are
not verified. Since 7.10.1 an OCSP response or CRL that FreePDF cannot read,
parse or verify, and that could revoke a certificate, keeps that certificate
from being shown as not revoked, so the signature is at best Indeterminate.
With `PdfRevocationCheck.Online`, a current answer fetched from the issuer's
responder or CRL can still show a certificate not revoked next to DSS data
FreePDF could not read (too large, or in a form it does not decode), but not
next to data it read and could not use.

Filling or flattening form fields with text the form's fonts cannot encode in WinAnsi, incremental updates of
documents encrypted with a public-key or other non-standard security handler, PDF/UA for documents drawn without the
layout engine, arbitrary PDF/A conversion, form fields created in PDF/UA documents, text and choice fields created in PDF/A documents, and complex script shaping are not promised in 7.10. Placeholder inherited types are not
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
