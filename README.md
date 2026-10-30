# FreePDF

**PDF creation and document layout for .NET, under the MIT licence.**

FreePDF is io7's independent continuation of **PDFsharp and MigraDoc 6.2.4**.
PDFsharp provides PDF drawing, reading, and modification; MigraDoc provides
document layout with paragraphs, tables, headers, and footers.

The starting point is empira's MIT-licensed `v6.2.4` release. FreePDF preserves
that source history and attribution. It is maintained independently by io7
and is not an official empira release.

## Project status

This repository establishes the fork. No rendering fixes or new features have
been added yet, and no FreePDF NuGet packages have been published.

Existing `PdfSharp` and `MigraDoc` namespaces, assembly names, and upstream build
files remain in place. The inherited packaging projects still contain upstream
package identities; do not publish their output. Distinct FreePDF package
identities and reviewed dependency metadata are required before a package release.

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
