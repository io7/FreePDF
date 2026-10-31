# Release dependency and provenance audit

Scope: the two `io7.FreePDF*` packages, their resolved dependency graphs for all
four shipped targets, installed-package consumers, and embedded product assets.
The audit does not relabel development tools or inherited demo/test dependencies
as part of the product. Review date: 2026-10-31.

## Product source and embedded material

| Material | Evidence and disposition |
| --- | --- |
| PDFsharp/MigraDoc source | Reviewed MIT commits and original unchanged root notice in `PROVENANCE.md`; no later AGPL implementation input. |
| FreePDF changes and fixtures | Original MIT work with behavioural/specification inputs recorded in provenance. |
| BigGustave PNG reader | Inherited Unlicense text retained in source and both package notice folders. |
| ICC sRGB2014 profile | Unchanged profile, SHA-256 `384b832de3412066743b52a75ee906b6fb9fb8d9e09e936fc2c43223815c6e0a`; ICC ownership and permissive embedding/redistribution terms recorded in `src/packages/ICC-sRGB2014.txt`. |
| Legacy MD5 helper | The inherited helper cited an unavailable Silverlight archive and a Microsoft Mobile repository with device-limited root terms. Those links were insufficient evidence of an unrestricted licence for that helper. Its implementation was replaced with original code from RFC 1321 sections 3.1-3.5, checked against published vectors and the platform implementation. This is a provenance gap finding, not a legal conclusion about the old release. |
| Adobe glyph-list data | Inherited conditional source files are excluded from shipped builds. Adobe's BSD notice is retained in `third-party/Adobe-Glyph-Lists.LICENSE.txt`, from immutable publisher commit `4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6`. |

ICC terms: https://www.color.org/profileview/#licensing

The root `LICENSE` retains SHA-256
`3c648a5072bd7f7386ca28687780489a708a57ed75f13927bc16dd3e34af3bf7`.
The imported MIT source and notices remain in repository history. The removed
MD5 implementation is not part of new package binaries; this change does not
rewrite the historical Git record.

## Resolved NuGet dependencies

`eng/Write-PackageInventory.ps1` writes package identity/version, archive SHA-256,
licence evidence, runtime target membership and retained notice hashes. It audits
the complete four-target library graph and each consumer's resolved graph.
An unreviewed licence fails the gate. Dependencies stay in separate NuGet
packages; their DLLs are not repackaged inside FreePDF.

The shipped runtime dependency families are Microsoft's logging/DI abstractions,
code-page encodings, PKCS/CMS cryptography, ASN.1 and their BCL compatibility
dependencies. The checked runtime packages declare MIT. The inventory is the
authoritative version list and may differ by target framework.

Two old build inputs need explicit treatment rather than a blanket MIT label:

- `NETStandard.Library 2.0.3` has a verified embedded MIT licence rather than a
  modern NuGet licence expression.
- `Microsoft.NETCore.Platforms 1.1.0` contains runtime-selection metadata and
  no runtime assembly in these graphs. Its Microsoft .NET Library terms are
  retained and identified separately; it is not classified as MIT.

Framework reference packages are development-only, checked against their
publisher's licence URL and excluded from runtime target membership.

`eng/Test-PackageAdvisories.ps1` queries NuGet's current advisory feed for direct
and transitive dependencies. Any reported advisory or query error fails the gate.
A clean report is a dated feed result, not a claim that software has no unknown
vulnerabilities. CI artifacts retain the JSON query and graph inventories.

## Test and deployment tools

The DejaVu font is test-only with its retained permissive notice; original test
fonts and PDFs contain no customer material. MIT reference packages are pinned
by version and SHA-256 in `eng/api-baselines.json` and the lab lockfile.

MuPDF, Poppler, qpdf and veraPDF run as external test tools. They are not linked
into or distributed inside FreePDF NuGet packages. Their own licences and release
terms apply to deployments of those tools; using one in validation does not make
it part of the library's licence statement. The existing lab records its MuPDF
distribution separately. veraPDF 1.30.3 is downloaded from the publisher, pinned
by SHA-256, and its archive signature was verified against publisher fingerprint
`13DD102B4DD69354D12DE5A83184863278B17FE7` during this audit.

The lab's private .NET runtime is upgraded to 10.0.14. The patched PKCS package
remains pinned to 10.0.13, including the explicit override in the MIT reference;
runtime and NuGet package versions are separate inputs.
