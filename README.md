<img src="src/packages/icon.svg" alt="" width="72" height="72" align="right">

# FreePDF

**PDF creation and document layout for .NET, under the MIT licence.**

FreePDF is io7's independent MIT continuation of **PDFsharp and MigraDoc**.
PDFsharp provides PDF drawing, reading, and modification; MigraDoc provides
document layout with paragraphs, tables, headers, and footers.

The starting point is empira's MIT-licensed `v6.2.4` release. FreePDF preserves
that source history and attribution. It is maintained independently by io7
and is not an official empira release.

## Coming from PDFsharp 6.2.4 or 6.5?

PDFsharp 6.5.0 and 7.0.0 Preview 2 (October 2026) are licensed under the AGPL or
a commercial licence, and the application must select a licence in code before
first use. FreePDF continues the MIT line from 6.2.4: replace the PDFsharp
packages with the matching `io7.FreePDF` packages and rebuild.

In a [survey of 26 public projects](docs/research/consumer-survey/README.md)
that use PDFsharp 6.2.4, 23 compiled against FreePDF with no changes. The
[migration guide](docs/migrating-to-7.md#what-real-624-applications-hit) lists
what the other three needed, each a one-line change or a font file you supply.
Applications that fill forms through the legacy `PdfSharp.Pdf.AcroForms` model
move over with renames; see [Filling forms](docs/migrating-to-7.md#filling-forms).

## Factur-X and ZUGFeRD invoices

One call turns a document into a hybrid e-invoice: PDF/A-3 with your UN/CEFACT
Cross Industry Invoice XML embedded as `factur-x.xml` and declared the way
Factur-X 1.0 and ZUGFeRD 2.x require (associated file, XMP extension schema).

```csharp
using var document = new PdfDocument();
FacturXManager.ForDocument(document).SetInvoice(invoiceXml, FacturXProfile.EN16931);
var page = document.AddPage();   // then draw the human-readable invoice
document.Save("invoice.pdf");
```

Call `SetInvoice` before adding pages. FreePDF checks that the XML is a
well-formed Cross Industry Invoice; checking its content against EN 16931 and
the profile's business rules is up to your invoicing software. CI validates
the generated PDF/A-3 file with veraPDF.

## Accessible PDF (PDF/UA-1)

The layout engine writes tagged PDF for PDF/UA-1, for documents that must be
accessible, for example under the European Accessibility Act:

```csharp
document.Info.Title = "Invoice 2026-001";                 // required
document.Culture = CultureInfo.GetCultureInfo("nb-NO");   // the document language
logo.AlternativeText = "Example Ltd logo";                // every image and chart, or Decorative = true
var renderer = new PdfDocumentRenderer { Document = document, TaggedPdf = true };
renderer.RenderDocument();
```

Headings (paragraphs with an outline level), paragraphs, lists, tables, images,
charts and links are tagged; headers, footers, borders and shading become
artifacts that screen readers skip. Rendering stops with an explanation if an
image has neither alternative text nor `Decorative`, rather than writing a file
that does not conform. CI validates a tagged multi-page invoice with veraPDF.
See the [support policy](docs/support-policy.md) for what is covered.

## Merge and split with forms, bookmarks and links

```csharp
using var merged = new PdfDocument();
foreach (var path in files)
{
    using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    merged.AppendPages(source, new PdfPageImportOptions { BookmarkTitle = Path.GetFileName(path) });
}
merged.Save("merged.pdf");

using var report = PdfReader.Open("report.pdf", PdfDocumentOpenMode.Import);
using var summary = report.ExtractPages(0, 2);   // the first two pages
summary.Save("summary.pdf");
```

Form fields stay a form you can fill or flatten; a field name that is already
taken gets a suffix (`name_2`), and `AppendPages` tells you which. Links,
buttons and bookmarks lead to the pages in the new file, and a split file does
not carry hidden copies of the pages it left out. `AddPage(page)` keeps PDFsharp 6.2.4's
behaviour; see [Merging and splitting](docs/migrating-to-7.md#merging-and-splitting).

## Fill in or sign a signed PDF without breaking its signatures

```csharp
using var document = PdfReader.Open("signed.pdf", PdfDocumentOpenMode.Append);
((PdfFormTextField)document.Catalog.GetAcroForm()!.Fields["name"]!).Text = "Ada Lovelace";
DigitalSignatureHandler.ForDocument(document, signer, options);   // optional: sign it again
document.Save("signed-filled.pdf");
```

In `Append` mode, saving keeps the original file byte for byte and adds only the
new and changed objects after it, as an incremental update. The signatures
already in the file stay valid, and a new signature covers the whole file,
earlier signatures included. A document with no changes is saved as it was.
A change that a certification signature or a field lock does not permit is
refused before anything is written. An encrypted document (opened with its
owner password) keeps its encryption, and the update is encrypted the same way.
See
[Signed documents](docs/migrating-to-7.md#signed-documents).

## Project status

FreePDF **7.0** adopts the verified MIT `7.0.0-preview-1` source, carries
our earlier fixes, and adds independently implemented reader, image, signature,
font-cache and layout corrections. 7.0.1 gives the assemblies io7's own
strong-name key and FreePDF/io7 product metadata; FreePDF is not an empira
release. 7.1.0 adds the GDI and WPF packages, form filling, Factur-X / ZUGFeRD
invoices, tagged PDF/UA-1 output and the fixes from a real-PDF corpus round trip
and parser fuzzing; 7.1.1 measures filled form text with Adobe's standard font
metrics. 7.2.0 draws text with installed fonts when no font resolver is set,
flattens forms and tags multi-page tables as one table. 7.3.0 merges and splits
documents with their form fields, bookmarks and links, and names FreePDF as the
producer of the files it writes. 7.4.0 saves documents as incremental updates,
so signed PDFs can be filled in and signed again; 7.4.1 fixes cases where that
broke earlier signatures and opens encrypted files with incremental updates.
See the release notes
for [7.4.1](docs/releases/7.4.1.md), [7.4.0](docs/releases/7.4.0.md), [7.3.0](docs/releases/7.3.0.md), [7.2.0](docs/releases/7.2.0.md), [7.1.1](docs/releases/7.1.1.md), [7.1.0](docs/releases/7.1.0.md),
[7.0.1](docs/releases/7.0.1.md) and [7.0.0](docs/releases/7.0.0.md), the
[changelog](CHANGELOG.md) and
[upstream feature tracking](docs/upstream-feature-tracking.md).
Published versions are listed in [GitHub Releases](https://github.com/io7/FreePDF/releases)
and NuGet; CI artifacts alone are not a public release.

Replace each upstream package with its FreePDF counterpart. Namespaces and assembly names remain `PdfSharp`
and `MigraDoc`, so an application references exactly one implementation:

| Upstream package (6.2.4) | FreePDF package | Targets |
| --- | --- | --- |
| `PDFsharp` | `io7.FreePDF` | .NET 8, 9, 10, .NET Standard 2.0 |
| `PDFsharp-MigraDoc` | `io7.FreePDF.Layout` | .NET 8, 9, 10, .NET Standard 2.0 |
| `PDFsharp-GDI` | `io7.FreePDF.Gdi` | Windows .NET 8, 9, 10, .NET Framework 4.6.2 |
| `PDFsharp-MigraDoc-GDI` | `io7.FreePDF.Layout.Gdi` | Windows .NET 8, 9, 10, .NET Framework 4.6.2 |
| `PDFsharp-WPF` | `io7.FreePDF.Wpf` | Windows .NET 8, 9, 10, .NET Framework 4.6.2 |
| `PDFsharp-MigraDoc-WPF` | `io7.FreePDF.Layout.Wpf` | Windows .NET 8, 9, 10, .NET Framework 4.6.2 |

Each layout package depends on its matching core package. Build and test them with PowerShell 7:

```powershell
./eng/Test-Packages.ps1 -OutputDirectory /absolute/path/to/candidate-packages
# Windows also runs Framework consumers and the GDI/WPF consumers (net8.0-windows and net472 by default):
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
