# Consumer survey: PDFsharp 6.2.4 applications compiled against FreePDF

Run 2026-11-02. Question: which 6.2.4 APIs that real code uses no longer compile against FreePDF 7, so that
compatibility members go where they matter.

## Method

`repos.json` pins 26 public GitHub projects whose csproj references `PDFsharp` or `PDFsharp-MigraDoc` 6.2.4
(GitHub code search, ranked by stars and recent activity) to exact commits. `eng/research/Survey-Consumers.ps1`:

1. sparse-clones each project at its commit, widening the checkout to every referenced project;
2. compiles the project's C# sources (and those of referenced projects) twice, once against the PDFsharp packages the
   project declares and once against `io7.FreePDF` / `io7.FreePDF.Layout`;
3. reports the compiler errors that appear only in the FreePDF build.

Compilation uses Roslyn's `Compilation.GetDiagnostics()` through `eng/research/ConsumerCompile`, because `csc` stops
after the declaration phase when it finds errors there and would hide method-body errors. The projects are untrusted
input and are only read and compiled: none of their MSBuild files is evaluated, none of their other packages is
restored, and analyzers and source generators are off. Their other dependencies are therefore unresolved in both
builds; those errors cancel out.

Limitations: an expression whose type comes from an unresolved package is not checked, so a break hidden behind such
an expression is not counted. The sample is public code; private applications may use other APIs (legacy forms, for
example, see below). GDI/WPF-variant consumers would have been skipped; none of the 26 uses them.

Reproduce (PowerShell 7, .NET 10 SDK):

```powershell
dotnet build eng/research/ConsumerCompile -c Release
./eng/research/Survey-Consumers.ps1 -Manifest docs/research/consumer-survey/repos.json -WorkDirectory /tmp/survey
# against locally packed candidates:
./eng/research/Survey-Consumers.ps1 -Manifest docs/research/consumer-survey/repos.json -WorkDirectory /tmp/survey `
  -CandidateVersion 7.1.0-survey.1 -CandidateSource /path/to/packed
```

The harness was checked first against a local fixture that uses five removed 6.2.4 APIs (legacy AcroForm types, a
parameterless `SetPdfA()` and a `DateTime` read of `Info.CreationDate`); all five were reported.

## Results

| Project | 7.0.1 (nuget.org) | 7.1 candidate |
| --- | --- | --- |
| 23 projects (below) | 0 new errors | 0 new errors |
| APSIMInitiative/ApsimX | 1 | 1 |
| Altinn/altinn-storage | 6 | 6 |
| wieslawsoltes/PdfSpace | 6 | 3 |

The 23 that compile unchanged: ArthurHub/HTML-Renderer, abatsakidis/PDFDeSecure, EvotecIT/OfficeIMO,
InkCanvasForClass/community, OpenAC-Net/OpenAC.Net.NFSe, surveysolutions/surveysolutions, Gaurox/FrameShift,
manfromarce/DocSharp, tobitege/OpenSourceToolkit.NET, i3sam/OrbitDrop, KnudsenMorten/SecurityInsight,
brunoleocam/ZPL2PDF, allisterb/Camel, sql-bi/SQLBI-Whiteboard, porrey/PdfDocuments, ajisaacs/OpenNest,
homotechsual/dotMARC, muratgul/PdfTool, jonathanmadeley/hl-api-client-net, phongnguyend/DocumentKit,
YodasMyDad/Merchello, OpenAC-Net/OpenAC.Net.NFSe.Nacional, hasanzadee/convertino.

### What broke, and what 7.1 does about it

| Cause | Where | 7.1 |
| --- | --- | --- |
| `DictionaryElements.GetDateTime(string, DateTime)` returning `DateTime` | PdfSpace (2) | Compatibility overload returning UTC, as 6.2.4 did |
| `CString` subclassed with the removed parameterless constructor | PdfSpace (1) | `CString()`, `CInteger()`, `CReal()` restored |
| `CSequence.ToContent()` now returns `string`, not `byte[]` | PdfSpace (3) | Not shimmable (return type); migration guide gives the one-line conversion |
| `PdfSharp.WPFonts.FontDataHelper` (Segoe WP font bytes) | Altinn (6) | Not shipped: no licence to redistribute Microsoft's Segoe WP fonts was found; migration guide says to supply licensed fonts |
| Unused `using PdfSharp.Snippets;` | ApsimX (1) | Not shipped (upstream sample helpers); remove the using |

Not found in the sample but checked separately: the parameterless `PdfDocument.SetPdfA()` (restored in 7.1, declaring
PDF/A-1b) and legacy AcroForms (`using PdfSharp.Pdf.AcroForms`; about 144 C# files on GitHub against about 11,900
with `using PdfSharp.Pdf`, a count that includes vendored PDFsharp copies). Legacy forms need a larger change than a
compatibility member and are tracked in https://github.com/io7/FreePDF/issues/9.

The 7.1 compatibility members introduced no new error in any project, including the 23 that already compiled.
