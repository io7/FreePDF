# FreePDF 7 release candidate

FreePDF is io7's MIT continuation of the verified MIT PDFsharp/MigraDoc source.
Original copyright notices and the MIT licence are retained. New fixes use
independent reproductions; see PROVENANCE.md.

- `io7.FreePDF` provides PDF drawing, editing, barcodes, charts and cryptography.
- `io7.FreePDF.MigraDoc` provides document layout and PDF/RTF rendering, and depends
  on `io7.FreePDF`.

The namespaces and assembly names remain `PdfSharp` and `MigraDoc`. Replace
upstream package references when migrating; applications must resolve one
implementation of those assemblies. The v7 API changes still require review
when migrating from PDFsharp 6.x. This candidate is not a stable release.

Assets target .NET 8, 9, 10 and .NET Standard 2.0. Framework consumers use the
Standard assets; install tests cover .NET Framework 4.6.2 and 4.7.2 on Windows.
Use .NET 10 for new applications. Microsoft ends .NET 8/9 runtime servicing on
2026-11-10; library compatibility does not extend runtime servicing.
https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

Configure an `IFontResolver` for portable font access. This package contains the
portable Core implementation. GDI/WPF implementations remain available as source
builds. The comparison lab and its external renderer are not package dependencies.

Validation covers original PDF regressions, semantic save/reopen checks and
render comparisons. No general PDF/A, PDF/UA, secure-redaction, complex-script
shaping or cryptographic-signature conformance certification is claimed.
