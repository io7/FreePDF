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
No such material has been imported into this baseline.

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
