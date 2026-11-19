# Source provenance

## Initial baseline

| Item | Value |
| --- | --- |
| Upstream repository | https://github.com/empira/PDFsharp |
| Upstream release tag | `v6.2.4` |
| Source commit | `9c3863a71b12067ff36e68a92c1a1f260114e2d1` |
| Source tree | `dccbb969ab76eee471f664799a1fafa734a37ffc` |
| Upstream release date | 2026-01-06 |
| Baseline verified | 2026-10-30 |
| Licence | MIT; original `LICENSE` preserved byte for byte |
| `LICENSE` SHA-256 | `3c648a5072bd7f7386ca28687780489a708a57ed75f13927bc16dd3e34af3bf7` |
| Preserved tags in this repository | `v6.2.4`, `upstream/v6.2.4` |

The initial import fetched only `refs/tags/v6.2.4` and its reachable history.
It did not import the current upstream branch or later release tags. FreePDF's
initial branch descends directly from that commit. The original README is
preserved byte for byte at `docs/upstream/README-v6.2.4.md`.

Both preserved tag names resolve to the baseline commit. The original `v6.2.4`
name is retained so the inherited GitVersion build can identify its base version.

This is an independent GitHub repository preserving the MIT baseline's Git
ancestry, rather than a GitHub fork-network copy of the current upstream branch.
The retained original notices identify the original authors; io7's changes
do not transfer ownership of their work.

## Scope of the initial change

The initial FreePDF change adds project documentation, contribution rules,
issue and pull-request templates, and core build CI. Library implementation
and upstream dependency versions are unchanged. There are no backported patches
in this initial change.

Downloaded test assets, new NuGet releases, and externally sourced patches are
not covered by the baseline verification. Review their provenance separately.

## Future changes

For an independent fix, record the factual issue description or specification,
our reproducer, expected behaviour, and implementation rationale in the PR.
Keep upstream AGPL implementation material out of the implementation inputs.

For an imported change, record the exact source URL, immutable commit or archive
hash, applicable licence and notices, and evidence that the imported material
was made available under that licence. A release date or a package-level label
alone is not sufficient provenance for an individual source file or patch.

MIT-era material from another upstream release, including any proposed
`7.0.0-preview-1` backport, requires its own source-level review before import.
The sections below record subsequently reviewed imports separately.

## 6.2.5 development changes

The selected preview source is `empira/PDFsharp` tag `v7.0.0-preview-1`, commit
`8d818d5f2ad5087201c4a54be3ab489b4920a6d2`. Its root MIT licence was read at that
immutable commit before fetching the tag. The licence blob is
`03b0a374b7474be41a56aa0eda52f2fa9efa2aae`, identical to the baseline licence.
The imported paths refer to that root licence and have no separate licence
override in their directories. Their original file headers are preserved.

| Input file in that commit | Source blob | Adaptation |
| --- | --- | --- |
| `src/foundation/src/PDFsharp/src/PdfSharp/Drawing.Pdf/PdfGraphicsState.cs` | `fa894a0151fbf0ec883efa468959534a1ba958c7` | Take the unrealized-color/opacity correction only. Reset the cached fill to null after gradients; leave unrelated formatting and refactoring out. |
| `src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Content/CLexer.cs` | `85102697a309c900e1155e17a2aa8b15e5dcffa1` | Adapt the EOF check to the 6.2 scanner and throw its existing exception. Do not import the new inline-image object model. |

The zlib correction was implemented from the existing 6.2.4 encoder and
[RFC 1950](https://www.rfc-editor.org/rfc/rfc1950.html), using independent checksum
vectors and the platform zlib implementation as checks. Preview 1's encoder
was inspected and does not contain this correction. No later implementation
was consulted. The new tests and documentation are MIT-licensed FreePDF work.

The coding agent's inputs were the two MIT snapshots, their changelogs,
the public history pages listed in the feature matrix, RFC 1950, and synthetic
tests. It did not read 6.5.0 or Preview 2 implementation code. The later public
changelogs were used only to build the feature/requirements inventory.

## 7.0.0 development import and independent work

The complete MIT Preview 1 source at
`8d818d5f2ad5087201c4a54be3ab489b4920a6d2` is merged with preserved ancestry.
Its root licence blob remains `03b0a374b7474be41a56aa0eda52f2fa9efa2aae`;
FreePDF's root LICENSE still has the SHA-256 recorded above. Existing file
headers and embedded third-party notices are retained. The v7 import is an
explicitly reviewed source version, not a fetch/merge of upstream's current branch.

Independent additions used the MIT sources, factual release-history descriptions,
public issue titles/symptoms, RFC 1950 and original synthetic reproducers. No
later PDFsharp AGPL code, patches, tests, attachments or decompilation were inputs.
The requirements and tests are mapped in `docs/7.0.0-validation.md`. This is
documented input separation, not a claim of a staffed legal clean-room process.

Pdfxport.Net's licence, README, commit metadata and merged-PR titles were reviewed;
none of its implementation was imported or used as a coding input. Its individual
patch provenance remains a prerequisite to any future import.

The comparison reference package is `PDFsharp-MigraDoc 7.0.0-preview-1` from
NuGet, with an MIT licence expression and original empira attribution. Its
downloaded nupkg SHA-256 is
`618a240e62edb34efe42d50fb1d2e06a2a1c4d7303471f521de7b3e93e84c5ee`.
The PDFsharp library dependencies are the matching pinned MIT version. Package
content hashes are also retained in the reference `packages.lock.json`.
`System.Security.Cryptography.Pkcs` is explicitly overridden to 10.0.13 in both
runners; the reference environment is therefore not an untouched upstream install.

New unit-test code and the small `synthetic.ttf` generated by
`src/tests/FreePDF.RegressionTests/Assets/generate-font.py` are original MIT work.
The lab's unmodified DejaVu font has its own retained permissive font licence;
see `lab/Assets/DejaVuSans.LICENSE.txt` and `lab/README.md`. The four-color PNG
is original synthetic material. No customer documents are included.

MuPDF is an external, separately licensed AGPL renderer used by the lab, not
a FreePDF library dependency. Its notices, exact Windows archive hash and source
location are documented in `lab/README.md`. No later AGPL PDFsharp runner is
included in this change.

## Independent compatibility and reader fixes (2026-10-31)

Inputs for issue categories #266, #361, #394, #375, #388, #345, #392 and #348
were public issue titles/factual symptoms, the permitted MIT source, and the
following specifications:

- PDF cross-reference, stream and page-tree rules in
  [ISO 32000-1:2008](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf),
  including hybrid cross-reference ordering in section 7.5.8.4.
- Indexed palette alpha in the [PNG specification](https://www.w3.org/TR/png-3/#11tRNS).
- Component glyphs in Microsoft's [OpenType glyf specification](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf).

All new PDF/PNG fixtures and regression code are original FreePDF work. No
upstream issue implementation snippets, attachments, patches or later AGPL
source were read or reused. The nested-component `composite.ttf` is original
rectangle geometry generated by `generate-composite-font.py` using fonttools
4.60.1, with fixed timestamps and no external font input. Its SHA-256 is
`7faa14d7e7a089b5989269a7f14c4cb909198f5ec146a350c0207e86e9c8148d`.
The generator and output are covered by this repository's MIT licence.

## Preservation and candidate packaging (2026-10-31)

Issue titles #302 (content loss with incremental updates) and #369 (dictionary
ownership exception) prompted original fixtures. Implementation inputs were
only the reviewed MIT/FreePDF source and PDF cross-reference semantics already
recorded above. The fixes follow active object-stream entries and preserve
existing typed content objects. No upstream implementation, issue attachment or
test was read. Additional original fixtures check text, navigation, annotations,
form values and attachments through repeated save/reopen cycles.

The candidate packages preserve the original MIT notice and add the inherited
BigGustave Unlicense text and an attribution/terms reference for the unchanged
ICC sRGB2014 profile. The ICC profile matches the reviewed MIT tag byte for byte;
its SHA-256 is `384b832de3412066743b52a75ee906b6fb9fb8d9e09e936fc2c43223815c6e0a`.
The package consumer's DejaVu font is test-only and retains its existing notice.
No external test assets, renderer, or PDFsharp reference package are included in
the FreePDF packages. External Microsoft dependencies remain separate NuGet
packages with their own licences.

On 2026-10-31 the official MuPDF download endpoint redirected to
`https://github.com/ArtifexSoftware/mupdf-downloads/releases/download/1.28.5/mupdf-1.28.5-windows.zip`.
The direct archive was downloaded and matched the existing pinned SHA-256
`13476a53e84f5527983158acb6442c7853b3cdc022f3b24880780dcfba1fcc99`.
CI and new installations now use that publisher-owned URL; renderer version and
licence remain unchanged.

Windows reference restores added `win-x86` runtime lock sections implicitly.
The lockfile now records both `win-x86` and deployment's `win-x64` explicitly,
without changing any package version or content hash. CI uses locked restores
and rejects changed checkouts after generation or bundle publication.

## RC2 release hardening (2026-10-31)

The signer partial-read correction and short-file trailer correction use the
reviewed MIT/FreePDF source, the platform Stream.Read contract and original
reproducers. The deterministic 32-input mutation corpus modifies only original
PDF fixtures; it uses no third-party documents. Package-consumer invoices,
attachments, merge/stamp, encryption, archival and signing workflows are also
original. Signing identities are generated for tests and never represent a user.

The inherited MD5 helper named a historical Silverlight archive and a Microsoft
Mobile repository as source locations. The latter's root licence blob
`02fa4857ddeec8f868d90906b3442822c62e0810` contains device-limited terms. This did
not establish the original helper's unrestricted licence. The entire helper was
replaced with original code derived from the algorithm in RFC 1321 sections
3.1-3.5, not its reference implementation. The retired source was seen during
the audit; this is documented original replacement work, not a claim of a
staffed clean room. Published digest vectors, platform MD5 comparisons,
padding/chunking/offset/reuse cases, PDF encryption and attachment checks verify
the replacement. It does not claim FIPS compliance or make MD5 a secure signature
algorithm. Original notices remain in history; the old implementation is absent
from the new source file and package binaries.

Inherited inactive Adobe glyph-list source has an added BSD notice from the
publisher's `adobe-type-tools/agl-aglfn` commit
`4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6`. Only copyright/licence notices were
added; no glyph implementation was imported. The retained notice's SHA-256 is
`b4881ceee6880f7067d76065d3945218b67b20275372463068b104b81d983ef1`.

API comparison reads metadata from exactly the four MIT NuGet archives pinned
in `eng/api-baselines.json`, not later upstream packages. Independent qpdf,
Poppler and veraPDF processes validate authored output; their implementation is
not imported into the product. The veraPDF 1.30.3 installer SHA-256 is
`b512bf24945503a33fc10738ecffc1f2dccaaf8593a448e94ee9ac86a9559a59`; its publisher
signature was checked against fingerprint
`13DD102B4DD69354D12DE5A83184863278B17FE7`. The dependency inventory records all
resolved package identities, licences and archive hashes. See
`docs/dependency-audit.md` for the product/tool boundary and audit limitations.

## io7 assembly identity (2026-11-01)

FreePDF 7.0.0 still signed its assemblies with the inherited empira strong-name
key (public key token `f94615aa0424f9eb`) and named empira as company and PDFsharp
as product. For 7.0.1, `src/StrongnameKey.snk` was replaced with a newly generated
io7 1024-bit RSA key pair under the same file name, so the inherited project
references stay unchanged. Its public key token is `77af05c50284aa70`, and the
`PDFsharpStronNamePublicKey` property holds the matching public key. It was
generated with .NET's `RSACryptoServiceProvider` and tagged as a signature key,
as `sn -k` does. empira's key remains only in the repository history.

Strong naming is an assembly identity, not a security boundary, so the full key
pair is committed, as upstream's was. Assemblies now name io7 as company and
FreePDF as product. Their copyright attribute keeps empira Software GmbH next to
io7. No implementation or third-party material was added by this change.

## Package icon (2026-11-02)

`src/packages/icon.svg` is original, AI-assisted FreePDF artwork on a rounded
tile in io7's maintainer-supplied magenta (`#A11F63`). The original white F was
replaced on 2026-11-05 with three flowing white sheets, following the maintainer's
selection of the "Flowing Sheets" concept. The SVG is a font-free geometric
reconstruction of that concept, rather than an embedded raster preview.
The inputs were the maintainer's brand brief, original AI-generated design
drafts and their small-size renders. The selected draft and generation prompt
are retained at
https://artifacts.acetate.io7.net/a/683b23246772f3efc002d60ce8f06847.
No upstream or Adobe artwork, fonts or external image assets were used.
The SVG, generated PNG and regeneration script are covered by this repository's
MIT licence.

`eng/Update-PackageIcon.ps1` renders the committed 128x128 `icon.png` using
Inkscape 1.4 (`e7c3feb100`, 2024-10-09), an isolated profile and explicit PNG
export settings. Inkscape is an external development tool, not a packaged
dependency. All six FreePDF packages include the same PNG at the package root;
the GitHub README uses the SVG source.

## Standard font widths (2026-11-03)

Filled form fields measure text in standard fonts with Adobe's Core14 font
metrics. `src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Forms/StandardFontWidths.cs`
holds the advance widths of the WinAnsiEncoding characters 32 to 255 in the
twelve Latin standard fonts (Helvetica, Times and Courier with their bold and
italic styles). Symbol and ZapfDingbats are left out; they do not use
WinAnsiEncoding.

`eng/Update-StandardFontWidths.ps1` generates the file from:

- the twelve AFM files and `MustRead.html` in Apache PDFBox at tag `3.0.3`
  (`pdfbox/src/main/resources/org/apache/pdfbox/resources/afm`), which
  redistributes Adobe's Core14 AFM files with their accompanying notice;
- the Adobe Glyph List at the immutable publisher commit
  `4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6`, already recorded above, to match
  glyph names to characters.

The script pins every input by SHA-256 and stops on a mismatch. Only Adobe's
data is used; no PDFBox code was read or reused. ISO 32000-1 Annex D supplies the
two WinAnsiEncoding codes the glyph list does not name (240 octal draws the
space glyph and 255 octal the hyphen).

Adobe's `MustRead.html` permits use, copying and distribution for any purpose,
with or without modification, provided the copyright notices are retained, the
notice accompanies the data, modifications are noted and its paragraph is not
modified. The generated source carries the AFM copyright notices, the unmodified
paragraph and a description of FreePDF's modification (only the widths are kept).
The same text is in `src/packages/Adobe-Core14-AFM.txt`, which every FreePDF
package carries as `THIRD-PARTY/Adobe-Core14-AFM.txt`; `eng/Test-Packages.ps1`
fails a package without it. The generator, the measuring code and the tests are
MIT-licensed FreePDF work.

## Installed fonts without a font resolver (2026-11-03)

`Fonts/SystemFontResolver.cs` is original FreePDF code. Its inputs were the
OpenType specification's table directory, `name` (name ID 1 and its platform
encodings), `OS/2` (`usWeightClass`, `fsType` embedding permissions,
`fsSelection`) and `head` (`macStyle`) tables
(https://learn.microsoft.com/en-us/typography/opentype/spec/), the XDG Base
Directory specification for the Linux user font folder, the operating systems'
documented font folders, and the published fact that Liberation, Arimo/Tinos/
Cousine, Carlito and Caladea share the widths of the Microsoft fonts they
replace. No code from fontconfig, other PDF libraries or later PDFsharp releases
was read or reused. The inherited, unused `CoreBuildFontResolver_REMOVE` class
was not used.

The test fonts in `src/tests/FreePDF.RegressionTests/Assets/SystemFonts` are
original geometric fonts generated by `generate-system-fonts.py` with fonttools
4.60.1, with fixed timestamps and no external font input. One is named
"Liberation Sans" only to exercise the substitution; it contains no Liberation
data. The generator and fonts are covered by this repository's MIT licence.

## Barcodes and low-bit PNG (2026-11-10)

`CodeQR`, `QREncoder`, `Code128`, `Ean13`, `Ean8`, `UpcA`, `EanUpcSymbology`,
`LinearCodeRenderer`, `QRErrorCorrectionLevel` and MigraDoc's `BarcodeRenderer`
are original FreePDF code. Their inputs were ISO/IEC 18004:2015 (QR code
model 2: data modes, character count lengths, error correction table 9,
function pattern positions and annex E alignment positions, BCH format and
version information, data masks and penalty rules), ISO/IEC 15417 (Code 128
symbol character table, code sets, check character) and ISO/IEC 15420 / the
GS1 General Specifications (EAN/UPC character sets, parity patterns, check
digits, guard bars and quiet zones), and the published "HELLO WORLD" 1-M
codewords used in QR tutorials. No source code of QRCoder, ZXing, zxing-cpp
or other barcode libraries was read or reused. zxing-cpp 3.1.1 (Apache-2.0)
was used only as an external decoder during validation; it is not part of the
repository, its tests or its packages.

The 1-, 2- and 4-bit grayscale change to the inherited BigGustave decoder
(`RawPngData.GetPixel`, Unlicense) and the importer is original code from the
PNG specification's packing and sample scaling rules
(https://www.w3.org/TR/png-3/). The test PNGs in `PngLowBitDepthTests` are
original 2-row images written with Python's zlib module and checked with Pillow.

## Data Matrix replacement (2026-11-10)

The inherited `DataMatrixImage.cs` (PDFsharp's Data Matrix encoder, whose class
comment called the open-source version "a fake") is removed. Its structure,
function names (`Iec16022Ecc200`, `Ecc200Encode`), comments and commented-out
`fprintf` calls match the iec16022 C library by Adrian Kennard, Andrews & Arnold
Ltd, which its distributors license as GPL-2.0-or-later (Debian's copyright file
for the `iec16022` package; Arch and SUSE packages). The PDFsharp v6.2.4 file
carried no notice of that origin. FreePDF does not ship GPL-licensed code, so
the file is replaced rather than relicensed.

`DataMatrixEncoder.cs` and the new rendering in `CodeDataMatrix.cs` are original
FreePDF code written from ISO/IEC 16022:2006: the ECC 200 symbol size table,
ASCII and Base256 encodation with the 253- and 255-state randomizing (annex B),
ECI and FNC1, Reed-Solomon error correction with the field polynomial 0x12D,
block interleaving, the module placement of annex F and the finder and clock
patterns. The retired file's encode function and size table were read during the
audit that found the problem (to test what it produced); the replacement does
not follow its structure or code, and no other Data Matrix implementation was
read. This is documented replacement work, as for the MD5 helper above, not a
claim of a staffed clean room. zxing-cpp 3.1.1 was used only as an external
decoder in validation.

## ToUnicode and MigraDoc tabs (2026-11-10)

The ToUnicode, default tab stop and tab probing corrections (upstream issues
#401, #404 and #405) were written from the factual issue descriptions, the
CMap rules of Adobe technical note #5099 and ISO 32000-1 9.10.3, and original
reproducers; the upstream pull request diffs were not read. The test font
`src/tests/FreePDF.RegressionTests/Assets/supplementary.ttf` is original
geometry generated by `generate-supplementary-font.py` with fonttools 4.60.1,
fixed timestamps and no external font input. Its SHA-256 is
`0613d555528d40524961ca017da2b408bbbb92a3335f3cfff3ca813a436264cd`.

## Form field creation and certification (2026-11-10)

`PdfFormCreation.cs`, `PdfFormFieldStyle.cs`, `ButtonAppearance.cs`, the
certification writer in `DigitalSignatureHandler.cs` and the signature type check
in `SignatureSeedValues.cs` are original FreePDF code. Their inputs were ISO
32000-1 sections 12.5.6.19 (widget annotations), 12.7 (interactive forms: field
dictionaries, button and choice fields, variable text, default resources) and
12.8.2.2 (DocMDP), ISO 32000-2 Table 237 (seed value /MDP), and FreePDF's
existing form-filling, appearance and signing code. No other PDF library's
implementation was read. pyHanko, MuPDF (PyMuPDF), pypdf and poppler were used as
external readers and validators during validation; they are not part of the
repository or packages.

## PAdES baseline signatures (2026-11-10)

The PAdES format and time-stamp client in `PdfSharpDefaultSigner.cs`, `PdfSignatureFormat.cs`,
`IDigitalSignerFormat.cs`, the /SubFilter choice in `DigitalSignatureHandler.cs` and the test time-stamp
authority `TestTimeStampAuthority.cs` are original FreePDF code. Their inputs were ETSI EN 319 142-1 (PAdES
baseline signatures), ETSI EN 319 122-1 (CAdES), RFC 5652 (CMS), RFC 5035 (ESS signing-certificate-v2), RFC 3161
(time-stamp protocol), ISO 32000-2 12.8.3 and the documentation of .NET's System.Security.Cryptography.Pkcs and
System.Formats.Asn1 types. No source of iText, PDFBox, pyHanko, EU DSS or other signing libraries was read;
pyHanko and poppler's pdfsig were used only as external validators, and only the documentation (signatures and
docstrings) of pyHanko's validation API was consulted to call it.

## PAdES long-term validation data (2026-11-10)

The document security store writer `PdfDocumentSecurityStore.cs`, the validation data collector
`PdfValidationData.cs` (chains, OCSP requests and responses, CRL distribution points), the test PKI `TestPki.cs`
with its in-process OCSP responder and CRLs, the document time-stamp signer
`PdfTimeStampSigner.cs` and its HTTP helper `TimeStampAuthority.cs`, the document time-stamp dictionary in
`DigitalSignatureHandler.cs`, the rules for validation data and document time-stamps in
`PdfModificationPermissions.cs` and `PdfIncrementalUpdate.cs`, and the tests in `PadesLongTermTests.cs` are original
FreePDF code. Their inputs were ISO 32000-2 12.8.2.2 (DocMDP), 12.8.4.3 (document security store) and 12.8.5
(document time-stamps), ETSI EN 319 142-1 (B-LT and B-LTA), RFC 3161, RFC 5280, RFC 6960 and RFC 5019, and the documentation
of .NET's System.Security.Cryptography.Pkcs types. No source of iText, PDFBox, pyHanko, EU DSS or other signing
libraries was read; pyHanko 0.38.1 and poppler's pdfsig were used only as external validators, and only the
documentation (signatures and docstrings) of pyHanko's validation API was consulted to call it.

## Signature verification (2026-11-10)

The verifier `PdfSignatureVerifier.cs` and its result types `PdfSignatureVerification.cs` (byte ranges and revisions,
CMS and time-stamp checks, chain building, OCSP response and CRL parsing and signature checks, the PAdES level), the
change description carried by the permission engine's refusals, and the tests in `SignatureVerifierTests.cs` are
original FreePDF code. Their inputs were ISO 32000-2 12.8 (signatures, DocMDP, FieldMDP, DSS, document time-stamps),
ETSI EN 319 142-1 (PAdES baseline levels), ETSI EN 319 102-1 (the order of validation steps and the idea of proof of
existence; FreePDF does not implement its full process), RFC 5652, RFC 5035, RFC 3161, RFC 6960 and RFC 5280, and
the documentation of .NET's System.Security.Cryptography.Pkcs, X509Certificates and System.Formats.Asn1 types. No
source of iText, PDFBox, pyHanko, EU DSS or other signing or validation libraries was read; pyHanko 0.38.1 was used
only as an external validator to compare verdicts, called through its documented API.

## Accessibility preflight and producer corpus (2026-11-16)

`PdfAccessibilityScanner.cs`, `PdfAccessibilityContent.cs`, their public result
types, the accessibility CLI/report writers and their regression tests are original
FreePDF code. Inputs were the public PDF structure concepts, the PDF Association's
Matterhorn Protocol, W3C PDF accessibility techniques and the existing reviewed
FreePDF reader and tagged-PDF writer. This is explicitly partial PDF/UA-1 preflight
coverage; no implementation from another accessibility checker was imported.

`eng/corpus` contains original deterministic scenario recipes and test contracts.
ReportLab 4.4.3 and Playwright 1.58.0/Chromium are separate test-only producers, used
through their public APIs. Their implementation source was not consulted or copied.
The corpus uses the existing lab DejaVu font (with its existing separate license)
and bitmap asset. It contains no customer documents or copied commercial corpus.
qpdf, Poppler and SHA-256-pinned veraPDF 1.30.3 are external validation tools only.
No later AGPL PDFsharp source was fetched or consulted.

The PDF real/rectangle serialization precision fix was derived from an original
Chromium-generated scenario. Independent qpdf inspection showed the page box was
rounded while its content matrix remained unchanged; restoring the original page
coordinates removed the Poppler rendering difference. The regression is expressed
as a small original programmatic fixture, not an imported producer implementation.

The follow-up expands the original recipe count to 28,000 scenarios / 84,000 PDFs
without importing another corpus. Its plain-text CLI summary and terminal-text
escaping are original code using the existing report model and .NET character
categories. Local tool installation follows Microsoft's .NET tool documentation.
The accessibility roadmap uses the public DOJ Title II fact sheet, W3C's guidance
on evaluation-tool limits and veraPDF's documented validation scope. These sources
inform requirements and scope; no validator implementation was copied.

## Accessibility tool release packaging (7.11.0)

The tool uses the existing `io7.FreePDF*` package identity and release workflow.
The new package/install guards and tests are original code based on the existing
FreePDF package checks and Microsoft's documented .NET tool commands.

The tool bundles Microsoft's logging/DI abstractions 10.0.5 and PKCS 10.0.13.
Their own `THIRD-PARTY-NOTICES.TXT` files are included from the resolved NuGet
packages. `src/packages/DotNet-MIT-LICENSE.txt` preserves the .NET Foundation
licence from `dotnet/runtime` tags `v10.0.5` and `v10.0.13`, whose `LICENSE.TXT`
files share git blob `984713a49622a96da110443c15477613bc12656b`. The copied file
has that same blob hash. Only these licence texts were read; no runtime source
was used as implementation input. FreePDF's existing source and embedded-asset
notices are also included in the tool package.

### Release-review corrections (7.11.0)

An independent review of the candidate found the defects corrected here. The fixes
are original FreePDF code and tests, written from original synthetic reproducers:
the reader's existing internal cross-reference repair flag now also marks recovered
stream lengths and reaches the scanner (READ); font embedding is checked where text
is shown, following the text rendering modes of ISO 32000-1 9.3.6 and the
"fonts used for rendering" wording of PDF/UA-1 7.21.4.1; page content no longer
counts against the structure-node limit; the writer keeps reals within the
ISO 32000-1 Annex C range; the corpus render sample is chosen by scenario hash.
Pinned veraPDF 1.30.3 and qpdf were run only as external tools to compare verdicts
on those reproducers. No validator or later PDFsharp implementation was consulted.

## Requirements inventory and optional validator adapter (7.12.0)

The adapter, bounded subprocess runner, snapshot handling, report composition,
CLI options and synthetic regression controls are original FreePDF code. Inputs
were the existing reviewed FreePDF code, the published veraPDF CLI contract at
https://docs.verapdf.org/cli/validation/ and https://docs.verapdf.org/cli/help/,
and observations of the pinned veraPDF 1.30.3 executable on original fixtures.
No veraPDF implementation source or later AGPL PDFsharp material was consulted.
veraPDF remains a separately installed external tool, not a bundled dependency.

`docs/accessibility/requirements.json` adapts condition text, identifiers and
references from the PDF Association's Matterhorn Protocol 1.1 (April 2021):
https://pdfa.org/download-area/publications/Matterhorn-Protocol-1-1.pdf
The publication's copyright page grants CC-BY-4.0. Its attribution, license link
and the changes made by FreePDF are retained in the data and in
`docs/accessibility/Matterhorn-NOTICE.txt`. That notice is included in the tool
package. The data is available under CC-BY-4.0; FreePDF's software remains MIT.
The original PDF is not bundled. Notes and qualifications remain at the source.

The inventory includes all 137 entries in the source tables, including 13-008
added in 1.1. It does not repeat the introduction's stale count of 136 as the
table count. Coverage annotations and procedures are original FreePDF work.
Informative WCAG mappings use the dated W3C recommendations
https://www.w3.org/TR/2025/REC-WCAG21-20250506/ and
https://www.w3.org/TR/2024/REC-WCAG22-20241212/.
Those mappings are not equivalence assertions or legal-conformance conclusions.

## Native page and form content inspection (7.13.0-alpha.1)

The implementation and synthetic fixtures are original FreePDF work. Inputs
were the reviewed FreePDF 7.12.0 source and ISO 32000-1:2008, sections 7.8.3,
8.10, 14.6.2 and 14.7.4. They describe resource lookup, form invocation,
property lists and the structure references for pages and forms. The existing
Matterhorn inventory supplies condition 30-002 on repeated use of tagged forms.
The independent checks use the published veraPDF 1.30.3 executable.

Specification: https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf

### Release-review corrections (7.13.0)

A review of the 7.12.0 and 7.13.0 changes found the defects corrected here. The
fixes and their tests are original FreePDF code, written from original synthetic
reproducers: bounded finding locations (the 127-byte name limit of ISO 32000-1
Annex C), deduplication by page and PDF object, Type0 descendant fonts limited to
CIDFonts (ISO 32000-1 9.7.6), alternative text cut in written reports, the
adapter's handling of validator output limits, and a changed or unreadable
snapshot that keeps no validator verdict. No validator or later PDFsharp
implementation was consulted.

## Bounded memory and time in the accessibility scan (7.14.0)

The streaming operator reader added to the inherited MIT `CParser` (`ReadOperators`,
with `ParseObject` returning after each operator on request), the run-length store
of saved graphics states, the XMP depth check and one-pass summary of element text,
the per-object checks of shared strings, MCIDs and
glyph dictionaries, the `MaxDuration` option and the hostile-input tests are
original FreePDF code. Inputs were the reviewed FreePDF source, ISO 32000-1 Annex C
(implementation limits) and 8.4.2 (the graphics state stack), and original synthetic
reproducers found by the 7.13.0 review. `ReadContent` keeps its behaviour. No other
parser, validator or later PDFsharp implementation was consulted.

## Interruptible reading in the accessibility scan (7.14.1)

`ReadInterrupt`, its checks in the inherited MIT `Lexer`, `Parser` and `PdfReader`,
and the tests that stop the reader by `MaxDuration` and by cancellation are original
FreePDF code. Inputs were the reviewed FreePDF source and original synthetic
reproducers (many small objects, with and without a cross-reference table, in object
streams, as pages and as incremental updates). No other parser or later PDFsharp
implementation was consulted.

## Text extraction (7.15.0)

`Pdf.Content.Text/*.cs` (except the generated `GlyphTables.g.cs`), the `Extracting` mode of `CLexer`, the
extraction mode of `CParser` and `OpCodes.IsKnown` are original FreePDF code. Their inputs were ISO 32000-1
sections 7.2, 7.3.4, 7.7.3.4, 7.8, 8.3.4, 8.4.5, 8.5.2, 8.5.3, 9.2 to 9.4, 9.6, 9.7, 9.10 and Annexes C and D,
Adobe Technical Note #5099
(CMap and CIDFont files) and the Adobe Glyph List specification, section 2
(https://github.com/adobe-type-tools/agl-specification). No other PDF library's implementation was read
(iText, PdfPig, PDFBox, pdf.js, Poppler, MuPDF). Poppler's `pdftotext` is used as an external oracle in the
corpus check; it is not part of the repository or packages.

`GlyphTables.g.cs` is generated by `eng/update_text_tables.py` from data only, each download pinned by SHA-256:
the Adobe Glyph List and the ITC Zapf Dingbats Glyph List at the publisher's commit
`4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6` (BSD 3-clause, Copyright 2002-2019 Adobe), and the built-in
encodings of the Helvetica, Symbol and ZapfDingbats AFM files in Apache PDFBox at tag `3.0.3` (Adobe's AFM
terms; no PDFBox code). WinAnsiEncoding and MacRomanEncoding come from Python's `cp1252` and `mac_roman`
codecs with the Annex D differences noted in the script. The generated source and
`src/packages/Adobe-Glyph-Tables.txt`, which every package carries as `THIRD-PARTY/Adobe-Glyph-Tables.txt`,
hold the notices.

## Guide, samples and documentation site (2026-11-19)

The guide (`docs/guide`), the site pages (`docs/index.md`, `docs/reference.md`), the examples
(`src/samples/FreePDF.Samples`) and `eng/sync_doc_snippets.py` are original io7 work, written from FreePDF's
source, its XML comments and the examples' test results. empira's documentation site and the PDFsharp.Samples
repository were not used. The API reference is generated by docfx 2.81.0 (MIT; a build tool pinned in
`.config/dotnet-tools.json`, not shipped) from FreePDF's XML comments. The site logo `docs/images/freepdf.svg`
is the package icon `src/packages/icon.svg` at a smaller size. The examples use the regression tests'
generated font stand-in and the original PNG generator in `lab/Shared`.
