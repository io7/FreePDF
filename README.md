# FreePDF

**PDF creation and document layout for .NET, under the MIT licence.**

FreePDF is io7's independent MIT continuation of **PDFsharp and MigraDoc**.
PDFsharp provides PDF drawing, reading, and modification; MigraDoc provides
document layout with paragraphs, tables, headers, and footers.

The starting point is empira's MIT-licensed `v6.2.4` release. FreePDF preserves
that source history and attribution. It is maintained independently by io7
and is not an official empira release.

## Project status

FreePDF **7.0** adopts the verified MIT `7.0.0-preview-1` source, carries
our earlier fixes, and adds independently implemented reader, image, signature,
font-cache and layout corrections. 7.0.1 gives the assemblies io7's own
strong-name key and FreePDF/io7 product metadata; FreePDF is not an empira
release. See the release notes for [7.0.1](docs/releases/7.0.1.md) and
[7.0.0](docs/releases/7.0.0.md), the [changelog](CHANGELOG.md) and
[upstream feature tracking](docs/upstream-feature-tracking.md).
Published versions are listed in [GitHub Releases](https://github.com/io7/FreePDF/releases)
and NuGet; CI artifacts alone are not a public release.

The packages are `io7.FreePDF` and `io7.FreePDF.Layout` (the MigraDoc layout
engine). Their namespaces and assembly names remain `PdfSharp` and `MigraDoc`. Build and test them with
PowerShell 7:

```powershell
./eng/Test-Packages.ps1 -OutputDirectory /absolute/path/to/candidate-packages
# Windows also supports installed-package checks for Framework consumers:
./eng/Test-Packages.ps1 -OutputDirectory D:/test/freepdf-packages -Frameworks net8.0,net9.0,net10.0,net462,net472
```

The `Candidate packages` workflow publishes build artifacts, not NuGet releases.
See [package and migration notes](src/packages/README.md). The inherited upstream
packaging projects are blocked to prevent output under upstream identities.
The [v7 migration guide](docs/migrating-to-7.md),
[support policy](docs/support-policy.md) and
[dependency audit](docs/dependency-audit.md) define the release's scope.
CI additionally rehearses unpublished plain `7.0.0` packages, checks public APIs
against pinned MIT baselines, and validates generated output with qpdf, Poppler
and veraPDF.
The manual [publication workflow](docs/releasing.md) promotes verified CI
artifacts without rebuilding them and authenticates through NuGet Trusted Publishing.

## Licence and provenance

- The inherited code remains under the [MIT licence](LICENSE).
- New contributions to FreePDF are accepted under MIT.
- Original copyright notices and third-party licences remain intact.
- [PROVENANCE.md](PROVENANCE.md) records the exact source baseline and checksums.
- [CONTRIBUTING.md](CONTRIBUTING.md) explains the contribution policy.

Later upstream releases use different licensing. Their changes are not
automatically eligible for inclusion here. Each imported change must have
documented MIT-compatible provenance; otherwise, fixes must be implemented
independently from behavioural requirements and the permitted source baseline.

## Build the core libraries

Install the .NET 10 SDK. From the repository root:

```sh
dotnet build src/foundation/src/MigraDoc/src/MigraDoc.Rendering/MigraDoc.Rendering.csproj --configuration Release -p:TargetFrameworks=net10.0 -p:TargetFramework=net10.0
dotnet build src/foundation/src/MigraDoc/src/MigraDoc.RtfRendering/MigraDoc.RtfRendering.csproj --configuration Release -p:TargetFrameworks=net10.0 -p:TargetFramework=net10.0
```

These builds include the PDFsharp and MigraDoc dependencies. CI also compiles
the core libraries for .NET 8 and .NET Standard 2.0. Compilation does not prove
PDF conformance or layout correctness.

Run the synthetic regression suite without external fonts or documents:

```sh
dotnet test src/tests/FreePDF.RegressionTests/FreePDF.RegressionTests.csproj --configuration Release --framework net10.0
```

CI runs these regressions on Linux and Windows with .NET 8/10 and on Windows
with .NET Framework 4.7.2 (the test runner's minimum). The comparison lab also
executes console consumers targeting Framework 4.6.2 on Windows. CI compiles
the GDI and WPF variants for Framework 4.6.2 and modern Windows .NET. These checks
do not certify every inherited feature; the feature matrix records remaining gates.
See [7.0 validation](docs/7.0.0-validation.md) for the observed checks and limits.

## Comparison lab and roadmap

The [comparison lab](lab/README.md) generates identical authored fixtures in
separate MIT PDFsharp and FreePDF processes. A read-only browser workbench shows
the PDFs, raster comparisons, overlays, differences and structural checks.
Known reference defects remain visible for review. The lab can run permanently
on Windows alongside other applications; its installer requires operator approval.

The [research and roadmap](docs/research/7.0-roadmap.md) records the reviewed
upstream issue inventory, Pdfxport.Net findings, competitor features and release
priorities. Source availability and pixel similarity are not conformance certification.

The full upstream solution includes Windows projects and tests requiring
external assets. See the archived [upstream build notes](docs/upstream/README-v6.2.4.md).
External assets are not part of this source baseline and require a separate
licence/provenance check before importing or redistributing them.

## Contribute

Open issues and pull requests at https://github.com/io7/FreePDF.
Include a small, synthetic reproduction and the expected result. Do not upload
customer documents, credentials, or other confidential information.

## Credits

PDFsharp and MigraDoc were created and maintained by empira Software GmbH and
their contributors. Their original author credits and release README are
preserved in [docs/upstream/README-v6.2.4.md](docs/upstream/README-v6.2.4.md).
The embedded BigGustave PNG reader retains its own licence in the source tree.
