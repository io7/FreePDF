# FreePDF accessibility preflight

An offline .NET 10 command-line tool that checks existing PDFs without changing
them. Reports include detected structural failures, check coverage, document hashes
and required human review. Coverage is partial PDF/UA-1; this tool does not certify
ADA, WCAG, Section 508 or complete PDF/UA compliance.

Package: `io7.FreePDF.Accessibility.Tool`. Command: `freepdf-a11y`.

```sh
freepdf-a11y document.pdf
freepdf-a11y document.pdf --format html --output audit.html
freepdf-a11y document.pdf --format sarif --output audit.sarif
freepdf-a11y document.pdf --format json --policy automated
freepdf-a11y --requirements --format json
freepdf-a11y document.pdf --verapdf /opt/verapdf/verapdf --format html --output combined.html
```

The first command prints a plain-English summary: problems found, human review
required, or scan incomplete. It lists up to 20 findings with suggested next steps.
The HTML report opens in a browser and contains every finding, check coverage,
structure order and limitations. JSON and SARIF are for automation.

For a published version, install the package with
`dotnet tool install --global io7.FreePDF.Accessibility.Tool`. To install from a
local package directory, add `--source /path/to/candidate-packages --version 7.14.0`.
With the .NET 10 SDK, you can also run it from a source checkout:

```sh
dotnet run --project src/tools/FreePDF.Accessibility -c Release -- document.pdf
```

Output files must not already exist. Use `--password-env NAME` to supply a password
through an environment variable. Reports can contain text from the input PDF.
`--timeout SECONDS` (1 to 3600, default 120) ends the native scan as incomplete when
it takes longer; failures found until then are kept.

Exit codes: `0` explicit automated policy satisfied (review still required), `1`
failed checks (also in an incomplete scan), `2` human review required, `3`
incomplete scan without detected failures, `64` usage or I/O error. The default policy returns `2` even when all implemented checks pass.
That means a person still needs to check the document; it is not a tool crash.

The optional veraPDF adapter uses a separately installed validator. It supports
explicit PDF/UA-1 machine validation and retains native findings and human tasks.
Use `--verapdf-timeout SECONDS` to change its 60-second timeout. Missing tools,
unsupported profiles and incomplete reports leave the assessment incomplete.
A failure from either validator still gives exit code 1. Native-only JSON keeps
its existing shape. Combined JSON uses a version 2.0 envelope with both results.

`--requirements` lists all 137 Matterhorn 1.1 conditions with coverage and source
references. This inventory does not assign document verdicts. Its source data is
attributed to the PDF Association under CC-BY-4.0 in `THIRD-PARTY/Matterhorn-NOTICE.txt`.

See [the full scanner guide](https://github.com/io7/FreePDF/blob/main/docs/accessibility-preflight.md)
for the rule list, limitations and native .NET API.
