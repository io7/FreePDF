# Initial fork validation

Validated locally on Linux on 2026-10-30 with .NET SDK 10.0.301.

Baseline: `9c3863a71b12067ff36e68a92c1a1f260114e2d1` (upstream `v6.2.4`),
plus the initial FreePDF documentation and workflow changes.

| Project | Target | Result |
| --- | --- | --- |
| MigraDoc.Rendering (including PDFsharp and layout dependencies) | net8.0 | Passed; 0 warnings, 0 errors |
| MigraDoc.Rendering | net10.0 | Passed; 0 warnings, 0 errors |
| MigraDoc.Rendering | netstandard2.0 | Passed; 0 warnings, 0 errors |
| MigraDoc.RtfRendering | net8.0 | Passed; 0 warnings, 0 errors |
| MigraDoc.RtfRendering | net10.0 | Passed; 0 warnings, 0 errors |
| MigraDoc.RtfRendering | netstandard2.0 | Passed; 0 warnings, 0 errors |

Each build used `--configuration Release -p:TargetFrameworks=<target>
-p:TargetFramework=<target>`. The root README contains complete example commands.

Additional checks:

- No difference in `src/` or `LICENSE` from the recorded upstream baseline.
- Archived upstream README matches the original byte for byte.
- Preserved upstream tags resolve to the recorded baseline commit.
- The original `v6.2.4` tag lets the inherited GitVersion task resolve `6.2.4`.
- New/edited text passes `git diff --check`; the verbatim upstream README
  retains its original Markdown line-break whitespace.

These are compilation and source-integrity checks. No rendering regression,
PDF conformance, digital-signature, Windows GDI/WPF, or external-asset test suite
was run. GitHub Actions configuration is included, but this record reports local
validation only. No package was published.
