# FreePDF

FreePDF is io7's MIT continuation of the verified MIT PDFsharp/MigraDoc source.
It is maintained independently by io7 and is not an empira release. Original
copyright notices and the MIT licence are retained. New fixes use independent
reproductions; see PROVENANCE.md.

| Package | Replaces | Provides |
| --- | --- | --- |
| `io7.FreePDF` | `PDFsharp` | Portable PDF drawing, editing, barcodes, charts and cryptography |
| `io7.FreePDF.Layout` | `PDFsharp-MigraDoc` | MigraDoc document layout and PDF/RTF rendering on `io7.FreePDF` |
| `io7.FreePDF.Gdi` | `PDFsharp-GDI` | The same, drawing through GDI+ (System.Drawing), for Windows and .NET Framework |
| `io7.FreePDF.Layout.Gdi` | `PDFsharp-MigraDoc-GDI` | MigraDoc layout on `io7.FreePDF.Gdi` |
| `io7.FreePDF.Wpf` | `PDFsharp-WPF` | The same, drawing through WPF, for Windows and .NET Framework |
| `io7.FreePDF.Layout.Wpf` | `PDFsharp-MigraDoc-WPF` | MigraDoc layout on `io7.FreePDF.Wpf` |

The namespaces and assembly names remain `PdfSharp` and `MigraDoc`. Replace
upstream package references when migrating, and use one variant only: the
portable, GDI and WPF packages define the same types, so an application must
resolve exactly one implementation of those assemblies. The v7 API changes still
require review when migrating from PDFsharp 6.x. See the migration guide and support policy:
https://github.com/io7/FreePDF/blob/main/docs/migrating-to-7.md
https://github.com/io7/FreePDF/blob/main/docs/support-policy.md
Use the published NuGet packages or the packages attached to an official GitHub
release. CI rehearsal artifacts and a plain version label alone are not release evidence.

The portable packages target .NET 8, 9, 10 and .NET Standard 2.0; Framework
consumers use the Standard assets, and install tests cover .NET Framework 4.6.2
and 4.7.2 on Windows. The GDI and WPF packages target Windows .NET 8, 9, 10 and
.NET Framework 4.6.2; install tests cover net8.0-windows and .NET Framework 4.7.2.
Use .NET 10 for new applications. Microsoft ends .NET 8/9 runtime servicing on
2026-11-10; library compatibility does not extend runtime servicing.
https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

Without an `IFontResolver`, the portable packages use the fonts installed on
Windows, Linux and macOS, and substitute Liberation or DejaVu fonts for Arial,
Times New Roman and Courier New where those are missing. Set an `IFontResolver`
for identical output on every machine. The GDI and WPF variants use the fonts
installed on Windows. The comparison lab and its external renderer are not
package dependencies.

Validation covers original PDF regressions, semantic save/reopen checks,
installed-package workflows, render comparisons and independent output checks.
PDF/A-1b/2b/3b fixture conformance and CMS integrity tests do not certify arbitrary
documents, trusted signer identity or legal signature status. No PDF/UA,
secure-redaction, complex-script shaping or FIPS certification is claimed.
