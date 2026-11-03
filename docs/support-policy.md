# FreePDF 7 support and release policy

## Runtime and package scope

Use a maintained, patched .NET 10 runtime for new applications. The portable
packages contain `net8.0`, `net9.0`, `net10.0` and `netstandard2.0` assets.
CI executes package consumers on Linux and Windows for .NET 8/9/10 and on
Windows for Framework 4.6.2/4.7.2 targets. Framework execution uses the runner's
maintained .NET Framework 4.8 runtime; this does not certify historical CLR
installations. Prefer Framework 4.8/4.8.1 on an OS supported by Microsoft.

As checked on 2026-10-31, Microsoft ends .NET 8 and 9 support on 2026-11-10;
.NET 10 is supported through 2028-11-14. The compatibility assets can remain
available after runtime support ends, but FreePDF cannot provide security
servicing for Microsoft's runtime. Consult the current policy before deployment:
https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

Tested OS profiles are Ubuntu 24.04 and Windows Server 2022 in CI, plus a
dedicated Windows lab host. macOS, browser/WASM, mobile, Native AOT and
trimming are not validated release profiles.

The GDI and WPF packages (`io7.FreePDF.Gdi`, `io7.FreePDF.Layout.Gdi`,
`io7.FreePDF.Wpf`, `io7.FreePDF.Layout.Wpf`) contain `net8.0-windows7.0`,
`net9.0-windows7.0`, `net10.0-windows7.0` and `net462` assets. CI executes their
package consumers on Windows for `net8.0-windows` and Framework 4.7.2, including
an image drawn from the platform's own bitmap type. Their drawing results follow
GDI+ or WPF on the host and are not covered by the cross-platform pixel comparisons.

## Supported feature claims

The release gates cover drawing and text with explicit font resolution and
with installed TrueType fonts found without a font resolver (Core build),
paragraph/table layout and PDF/RTF generation, page import/merge/stamping,
attachments, AES-128/256 password encryption, and new detached CMS signatures
using SHA-256/384/512. Original PDF/A-1b, 2b and 3b fixtures are independently
validated. PDF/A conformance is a property of each output document; validate
your own output, fonts and images before archival use.

Factur-X / ZUGFeRD output (`FacturXManager`) is checked for the PDF side: a
PDF/A-3b file validated by veraPDF, with the invoice XML embedded, listed in
`/AF` and declared in the XMP extension schema. FreePDF only checks that the XML
is a well-formed Cross Industry Invoice; it does not validate invoice content
against EN 16931, a profile or a national CIUS such as XRechnung.

Tagged PDF/UA-1 output from the layout engine (`PdfDocumentRenderer.TaggedPdf`)
covers headings (outline levels 1 to 6), paragraphs, lists, tables with heading
rows, images and charts with alternative text, hyperlinks and text frames;
headers, footers, borders and shading are artifacts. A multi-page fixture with
all of these is validated by veraPDF's PDF/UA-1 profile in CI. veraPDF checks
what a machine can check; reading order, alternative text quality and heading
structure need human review (the Matterhorn Protocol lists these checks). A
table split across pages is one Table element per page, and PDF/UA-2 is not
covered.

The signing checks verify cryptographic integrity and tamper detection using
synthetic self-signed certificates. They do not establish trusted identity,
revocation checking, timestamp-authority behaviour, PAdES/LTV or a legal
signature status. No FIPS certification is claimed. MD5 exists only for legacy
PDF-format operations and checksums, never as the recommended signature digest.

Filling or flattening form fields with text the form's fonts cannot encode in WinAnsi, preservation of existing signatures,
incremental saving, PDF/UA for documents drawn without the layout engine, arbitrary PDF/A conversion and complex
script shaping are not promised in 7.1. Placeholder inherited types are not
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
