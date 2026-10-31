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
