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
when migrating from PDFsharp 6.x. See the migration guide and support policy:
https://github.com/io7/FreePDF/blob/feature/7.0.0-mit-preview-CZ/docs/migrating-to-7.md
https://github.com/io7/FreePDF/blob/feature/7.0.0-mit-preview-CZ/docs/support-policy.md
RC versions are candidates. Plain-version CI rehearsals remain unpublished
until an explicit release is authorized; a version label alone is not release evidence.

Assets target .NET 8, 9, 10 and .NET Standard 2.0. Framework consumers use the
Standard assets; install tests cover .NET Framework 4.6.2 and 4.7.2 on Windows.
Use .NET 10 for new applications. Microsoft ends .NET 8/9 runtime servicing on
2026-11-10; library compatibility does not extend runtime servicing.
https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

Configure an `IFontResolver` for portable font access. This package contains the
portable Core implementation. GDI/WPF implementations remain available as source
builds. The comparison lab and its external renderer are not package dependencies.

Validation covers original PDF regressions, semantic save/reopen checks,
installed-package workflows, render comparisons and independent output checks.
PDF/A-1b/2b/3b fixture conformance and CMS integrity tests do not certify arbitrary
documents, trusted signer identity or legal signature status. No PDF/UA,
secure-redaction, complex-script shaping or FIPS certification is claimed.
