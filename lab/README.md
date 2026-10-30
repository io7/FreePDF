# FreePDF comparison lab

A read-only ASP.NET Core workbench for recorded PDF comparisons. The same
original fixtures compile into **separate reference and candidate processes**.
The reference is the MIT `PDFsharp-MigraDoc 7.0.0-preview-1` NuGet package;
the candidate builds this checkout. Both run on .NET 10, or target .NET Framework
4.6.2 on Windows. The viewer itself targets .NET 10.

## What a run proves

Twelve cases cover text/styles/accents, transforms/clipping, transparency,
gradient-to-solid fills, disconnected paths, PNGs, multi-page paragraphs and
tables, full-width vertical merges, attachments/XMP, a checkbox appearance,
and the PDF/A generation path. Inputs contain only authored synthetic content.

Each engine gets a fresh process with a 30-second timeout. The exact same
renderer rasterizes both PDFs at 144 DPI in RGB. The report records renderer
version/hash, source revision, font hash, engine versions in `structure.json`,
page counts, attachment bytes, field count, and metadata presence. Dependencies
of the reference are pinned in `packages.lock.json`. Both engines override the
inherited cryptography dependency with patched `System.Security.Cryptography.Pkcs`
10.0.13. This is a version-pinned PDFsharp baseline with a documented dependency
override, not an untouched upstream application environment.

A pixel counts as changed when any channel differs by more than 8/255. RMSE and
maximum delta include *all* pixels. Dimension changes fail comparison. Current
matching cases have zero delta, not merely differences below the threshold.
Known upstream defects are amber **Review difference**, never auto-approved.
Candidate generation/invariant failures are always red. New differences fail
the CLI/CI. Review entries still require human judgment before release.

Pixel equality does **not** prove semantic text, reading order, accessibility,
PDF/A conformance, signature validity, encryption, or every inherited API. Add
veraPDF, text extraction, signature verification and malformed-input/fuzz tests
as separate gates. The PDF/A case deliberately claims no conformance result.

The viewer accepts no uploads and exposes no execution or mutation endpoint.
It is intended for trusted LAN use with synthetic fixtures, not public hosting.
CLI commands and source paths appear in reports; do not put secrets in arguments.

## Run locally

Install .NET 10 and an independently installed `mutool` command. With PowerShell 7:

```powershell
./lab/scripts/Test-Lab.ps1 -Renderer /usr/bin/mutool -DataDirectory /absolute/path/to/runs
# On Windows, also exercise Framework consumers:
./lab/scripts/Test-Lab.ps1 -Framework net462 -Renderer C:/tools/mutool.exe -DataDirectory D:/test-runs
```

Without PowerShell, build the three projects with `dotnet build -c Release
-f net10.0 -m:1`, run `FreePDF.Lab.dll --self-test`, then supply a JSON config to
`dotnet FreePDF.Lab.dll --run runner.json`. `Command.arguments` is an array;
commands run without a shell. Example:

```json
{
  "dataDirectory": "/absolute/path/to/runs",
  "reference": {"fileName": "/usr/bin/dotnet", "arguments": ["/absolute/path/Reference.Generator.dll"]},
  "candidate": {"fileName": "/usr/bin/dotnet", "arguments": ["/absolute/path/FreePDF.Generator.dll"]},
  "renderer": "/usr/bin/mutool",
  "sourceRevision": "full-commit-sha",
  "profile": ".NET 10",
  "retainRuns": 10,
  "retainBytes": 2000000000,
  "timeoutSeconds": 30
}
```

Serve from the viewer project directory (or published viewer directory):

```sh
dotnet FreePDF.Lab.dll --urls http://localhost:4817 --Lab:DataDirectory /absolute/path/to/runs
```

The runner retains up to ten completed runs and targets a 2 GB result budget,
keeping the newest run even if it alone exceeds that budget. It deletes only
its own completed, identified run directories. Interrupted runs are not pruned
automatically; inspect and remove those manually. PPM intermediates are removed
after comparison; PDFs, PNGs, differences, JSON and bounded logs remain.

## Permanent Tomahawk25 installation

The approved installation is available at `http://192.168.1.212:4817` on the LAN.
The viewer has a run selector, refresh action, side-by-side pages, opacity overlay,
pixel-difference image, page navigation, and PDF/structure/log downloads. It opens
the first failed/review case and hides document links when generation failed.
Both .NET 10 and Framework 4.6.2-targeted suites completed on 2026-10-30: each
produced nine zero-delta matches, three known-reference-defect reviews, and no failures.

Read-only host checks on 2026-10-30 found Windows host `192.168.1.212`, .NET 10
and PowerShell 7, about 32.9 GB free on D:, and port 4817 unused. No existing
FreePDFLab paths or PATH renderer were found. These checks are not deployment.

`scripts/Publish-Lab.ps1 -OutputDirectory <new-directory>` prepares a Windows
x64 bundle with .NET 10.0.13 self-contained viewer/generators and Framework
generators, checksums and provenance. It does not alter shared runtimes.

After operator approval, elevated PowerShell 7 can run the bundled
`Install-Tomahawk.ps1`. Its exact shared-host changes are:

- Create `D:\apps\FreePDFLab` and `D:\data\FreePDFLab`; refuse existing paths.
- Download the official MuPDF 1.28.5 Windows archive separately and check its
  recorded SHA-256 before extraction. Keep its COPYING/README notices.
- Install three verified Microsoft C++ runtime DLLs privately beside `mutool.exe`.
  The installer downloads and extracts the pinned redistributable without running
  its EXE/MSI installer or modifying shared runtime registration. It verifies
  both archive/file hashes and Microsoft Authenticode signatures.
- Create `FreePDFLab-Viewer`, a persistent startup scheduled task with automatic
  restart and a one-minute recovery trigger (ignore new starts while running),
  and `FreePDFLab-Nightly`, a daily 03:17 task for both runtime profiles.
  Both run as LocalService. The viewer uses port 4817; neither changes Acetate.
- Grant LocalService read/execute on the app and modify on its results directory.
- Add `FreePDFLab-LAN`, a TCP 4817 inbound firewall rule limited to
  `192.168.1.0/24`; start the viewer and initial comparisons.

Acceptance: `/health` returns read-only status; both tasks have the intended
identity/settings; both profiles produce reports; browser images, overlay,
difference view, page navigation and downloads work from the LAN; restart the
viewer task and verify recovery. No production-ready claim before these checks.

The first host run found the C++ DLLs absent, although CI's Windows images already
had them. `Install-RendererRuntime.ps1` now supplies them and verifies renderer
startup before registering tasks. Its source is the official Microsoft C++
14.51.36247.0 redistributable, SHA-256
`843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c`.
The script pins the immutable download URL and exact payload layout. Runtime
provenance is saved beside the renderer. These Microsoft binaries retain their
own licence and are not checked into this MIT repository.

Private runtime deployment is documented by
[Microsoft](https://learn.microsoft.com/en-us/cpp/windows/choosing-a-deployment-method?view=msvc-170).
It makes FreePDF's operator responsible for keeping these private DLLs updated;
shared Windows runtime servicing does not update this copy. Review the pinned
version as part of renderer updates.

Rollback: stop and unregister only the two `FreePDFLab-*` tasks and remove only
the `FreePDFLab-LAN` firewall rule. Preserve data/app directories for inspection;
delete them only with separate operator authorization.

## Licensing and reference policy

Original harness/fixtures are MIT. `Assets/DejaVuSans.ttf` is the unmodified
DejaVu Sans font, with its Bitstream Vera notice in `DejaVuSans.LICENSE.txt`;
DejaVu modifications are public domain. Font SHA-256:
`57f73e11f51999432bf7ab22ce55b6f945d5eca1bf824404cfa9ec2e3718c84e`.
`colors.png` is an original four-color fixture. Font project:
https://dejavu-fonts.github.io/.

MuPDF is a separately installed AGPL tool, not a dependency of the FreePDF
library or a NuGet package shipped by this project. Its licence/source remain
available at https://mupdf.com/releases and
https://github.com/ArtifexSoftware/mupdf/tree/1.28.5. Review its obligations for
any redistribution or network deployment; a process boundary alone is not a
blanket licence exemption. All harness source is public in this repository.
The Windows archive was downloaded from the official site on 2026-10-30:
`https://casper.mupdf.com/downloads/archive/mupdf-1.28.5-windows.zip`, SHA-256
`13476a53e84f5527983158acb6442c7853b3cdc022f3b24880780dcfba1fcc99`.
Linux runs record their actual installed renderer/hash; compare within each run,
not across different renderers or operating systems.

An AGPL PDFsharp reference can be added later as a separately licensed runner,
with its own exact package/version, notices and source obligations reviewed.
Executing it for observed behavior is different from importing its source.
Do not fetch/read/decompile its implementation or treat output parity as proof
of independent authorship. The default implemented reference remains MIT.
